using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RusK.API;
using RusK.Mods.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// VRM をゲームのキャラの見た目にする (D-2 / D-3)。
/// ゲームのキャラ (骨格・アニメーション・IK・当たり判定) はそのまま動かし、体のメッシュを隠して、
/// VRM をキャラの根元の下に置く。毎フレーム、アニメーションの後にゲームのキャラの骨の回転を VRM に写す。
///
/// どのキャラにどの VRM を付けるかは覚えておき (RusK/data/model/assignments.txt)、
/// ステージの移動などでキャラが作り直されたら付け直す。演出でゲームが元の体を表示し直したら、また隠す。
/// </summary>
internal static class VrmSwap
{
    private sealed class Entry
    {
        public IntPtr Key;            // キャラの根元 (Transform) のポインタ
        public Transform Root;        // キャラの根元 (骨格 Bip001 の親)
        public long Id;               // キャラの ID
        public PlayerController Player; // 操作キャラなら。タイトル画面・キャラクター画面の見せるためのモデルは null
        public bool IsShow;           // 見せるためのモデル (CharacterShowController)
        public string File;
        public VrmModel Model;
        public Retargeter Retargeter;
        public readonly List<Renderer> Hidden = new();
        /// <summary>影だけを描くメッシュ (一人称で見た目を隠しても影は残すため、見た目のメッシュと分けて持つ)</summary>
        public readonly List<Renderer> Shadows = new();
        public MaterialController Materials;
        public (bool shown, bool firstPerson) LastState = (true, false);
        /// <summary>元のキャラの Animator と、付ける前のカリングの設定 (外すときに戻す)</summary>
        public Animator Animator;
        public AnimatorCullingMode OriginalCulling;
    }

    private static readonly Dictionary<IntPtr, Entry> Entries = new();

    /// <summary>キャラの ID → VRM のファイル名 (RusK\models からの相対パス)</summary>
    private static readonly Dictionary<long, string> Assignments = new();

    /// <summary>付けようとして失敗したキャラ (同じキャラに何度も試さない)</summary>
    private static readonly HashSet<IntPtr> Failed = new();

    private static float _nextAuto;
    private static bool _loggedTemplate;

    /// <summary>VRM を置くフォルダ (RusK\models)</summary>
    public static string ModelsDir =>
        Path.GetFullPath(Path.Combine(ModelLab.Ctx.DataDirectory, "..", "..", "models"));

    private static string AssignFile => Path.Combine(ModelLab.Ctx.DataDirectory, "assignments.txt");

    public static IEnumerable<string> Files()
    {
        try
        {
            Directory.CreateDirectory(ModelsDir);
            return Directory.GetFiles(ModelsDir, "*.vrm", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>付けている VRM (切り抜きの方式の切り替え用)</summary>
    public static IEnumerable<VrmModel> Models() => Entries.Values.Where(e => e.Model != null).Select(e => e.Model).ToList();

    public static string AppliedFile(PlayerController p) =>
        p != null && Entries.TryGetValue(KeyOf(p), out var e) ? e.Model.Title : null;

    private static IntPtr KeyOf(PlayerController p) => p.transform.Pointer;

    /// <summary>見せるためのモデル (CharacterShowController) → キャラの ID (InitialSetting で渡されたもの)</summary>
    private static readonly Dictionary<IntPtr, long> ShowIds = new();

    /// <summary>CharacterShowController.InitialSetting の後に呼ばれる (VrmShowPatch)</summary>
    public static void OnShowInitialized(CharacterShowController show, double id)
    {
        if (show == null) return;
        long newId = (long)Math.Round(id);
        ShowIds[show.Pointer] = newId;
        // 同じモデルを別のキャラに使い回したら、付けていた VRM を外して付け直す
        var key = show.transform.Pointer;
        if (Entries.TryGetValue(key, out var e) && e.Id != newId)
        {
            Entries.Remove(key);
            Cleanup(e);
        }
        Failed.Remove(key);
        _nextAuto = 0f;
    }

    /// <summary>このキャラに設定されている VRM のファイル名 (無ければ null)</summary>
    public static string AssignedFile(PlayerController p)
    {
        if (p == null) return null;
        return Assignments.TryGetValue(Id(p), out var f) ? f : null;
    }

    // ------------------------------------------------------------------ キャラごとの設定

    public static void LoadAssignments()
    {
        MaskModes.Load();
        SkirtTuning.Load();
        Assignments.Clear();
        try
        {
            if (!System.IO.File.Exists(AssignFile)) return;
            foreach (var line in System.IO.File.ReadAllLines(AssignFile))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (long.TryParse(line.Substring(0, eq).Trim(), out var id))
                    Assignments[id] = line.Substring(eq + 1).Trim();
            }
        }
        catch (Exception e) { ModelLab.Ctx?.Log.Warning($"Model: assignments.txt を読めません: {e.Message}"); }
    }

    private static void SaveAssignments()
    {
        try
        {
            Directory.CreateDirectory(ModelLab.Ctx.DataDirectory);
            System.IO.File.WriteAllLines(AssignFile, Assignments.Select(kv => $"{kv.Key}={kv.Value}"), new UTF8Encoding(false));
        }
        catch (Exception e) { ModelLab.Ctx?.Log.Warning($"Model: assignments.txt を保存できません: {e.Message}"); }
    }

    private static long Id(PlayerController p)
    {
        try { return (long)Math.Round(p.GetPlayerId()); }
        catch { return -1; }
    }

    /// <summary>このキャラに VRM を設定して付ける (設定は保存される)</summary>
    public static void Assign(PlayerController player, string file)
    {
        var rel = Path.GetRelativePath(ModelsDir, file);
        Assignments[Id(player)] = rel;
        SaveAssignments();
        RestoreId(Id(player));
        Apply(player.transform, Id(player), player, file);
    }

    /// <summary>
    /// キャラの ID で設定する (file が null なら外す)。そのキャラに付けている VRM (操作キャラ・見せるためのモデル) は
    /// 外して、自動の付け直しで新しい VRM を付ける
    /// </summary>
    public static void AssignById(long id, string file)
    {
        if (file == null) Assignments.Remove(id);
        else Assignments[id] = Path.GetRelativePath(ModelsDir, file);
        SaveAssignments();
        RestoreId(id);
        _nextAuto = 0f;
    }

    /// <summary>このキャラの ID に付けている VRM をすべて外す (次の自動の付け直しを許す)</summary>
    private static void RestoreId(long id)
    {
        foreach (var e in Entries.Values.Where(e => e.Id == id).ToList())
        {
            Entries.Remove(e.Key);
            Cleanup(e);
        }
        Failed.Clear();
    }

    /// <summary>キャラの ID に設定されている VRM のファイル名 (無ければ null)</summary>
    public static string AssignedById(long id) => Assignments.TryGetValue(id, out var f) ? f : null;

    public static void OpenModelsFolder()
    {
        try
        {
            Directory.CreateDirectory(ModelsDir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{ModelsDir}\"");
        }
        catch (Exception e) { ModelLab.Ctx?.Log.Warning($"フォルダを開けません: {e.Message}"); }
    }

    /// <summary>このキャラの VRM の設定を外し、元の見た目に戻す</summary>
    public static void Unassign(PlayerController player)
    {
        if (player == null) return;
        Assignments.Remove(Id(player));
        SaveAssignments();
        RestoreId(Id(player));
    }

    // ------------------------------------------------------------------ 付ける / 外す

    /// <summary>
    /// キャラ (根元 root の下に骨格 Bip001 と体のメッシュがあるもの) に VRM を付ける。
    /// 操作キャラも、タイトル画面・キャラクター画面の見せるためのモデルも、骨格が同じなので同じ仕組みで付けられる
    /// </summary>
    private static void Apply(Transform character, long id, PlayerController player, string file)
    {
        var log = ModelLab.Ctx?.Log;
        var key = character.Pointer;
        if (Entries.TryGetValue(key, out var old))
        {
            Entries.Remove(key);
            Cleanup(old);
        }
        if (player != null) ModelSwap.Restore(player);

        var entry = new Entry { Key = key, Root = character, Id = id, Player = player, IsShow = player == null, File = file };
        try
        {
            var body = BodyRenderers(character).ToList();
            if (body.Count == 0) throw new InvalidOperationException("キャラの体のメッシュが見つかりません");

            // 材質の元: ゲームの体の材質 (不透明) と、頬の赤み (半透明)
            var templates = new VrmLoader.Templates
            {
                Opaque = body.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.renderQueue < 2500),
                Transparent = body.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.renderQueue >= 3000),
                OutlineWidth = 0f,
            };
            if (templates.Opaque == null) throw new InvalidOperationException("ゲームの材質が見つかりません");
            LogTemplate(templates.Opaque);

            entry.Model = VrmLoader.Load(file, templates, s => log?.Info("Model: " + s));
            var root = entry.Model.Root.transform;

            // 基準の姿勢 (VRM は読み込み直後の T ポーズ、根元が原点のうちに覚える)
            var dstRest = Humanoid.VrmRest(entry.Model);
            var srcRest = Humanoid.BipedRest(character, body);

            // キャラの下には置かない: ゲームの材質の管理 (MaterialController) がキャラの下の描画部品を集めて
            // 材質を書き換える (ステージの移動で VRM の縁が黒くなっていた)。位置と向きだけ毎フレーム合わせる。
            // ステージの移動で消えないようにしておく (キャラが作り直されたら付け直す)
            Object.DontDestroyOnLoad(root.gameObject);
            root.SetPositionAndRotation(character.position, character.rotation);
            root.localScale = character.lossyScale; // 見せるためのモデルは大きさを変えて置かれていることがある

            // 描画のレイヤー・影はゲームの体に合わせる
            int layer = body[0].gameObject.layer;
            foreach (var t in entry.Model.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in entry.Model.Renderers)
            {
                // 影は「影だけのメッシュ」(AddShadowCasters) が落とす。見た目のメッシュには両面表示のための裏向きの面があり、
                // それが影を落とすとキャラ自身に黒い影が出る
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = body[0].receiveShadows;
            }
            int shadows = AddShadowCasters(entry.Model, layer, entry.Shadows);
            try { entry.Materials = player != null ? player.GetMaterialController() : null; } catch { }

            entry.Retargeter = new Retargeter(character, srcRest, root, dstRest);

            // 元の体を隠す。描画だけを止める (forceRenderingOff) ので、ゲームが演出で体を表示・非表示にした状態
            // (enabled) はそのまま残り、VRM の表示をそれに合わせられる
            foreach (var r in body)
            {
                r.forceRenderingOff = true;
                entry.Hidden.Add(r);
            }

            // 元の体を描かないと、Animator が「見えていないキャラ」としてアニメーションを止めることがある
            // (見せるためのモデルは呼吸などの動きが止まっていた)。付けている間は常にアニメーションさせる
            entry.Animator = character.GetComponent<Animator>();
            if (entry.Animator != null)
            {
                entry.OriginalCulling = entry.Animator.cullingMode;
                entry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            Entries[key] = entry;
            entry.Retargeter.Update();
            entry.Model.Springs?.Reset();
            log?.Info($"Model: {CharacterNames.Get(id)}{(entry.IsShow ? " (画面に見せるモデル)" : "")} の見た目を VRM '{entry.Model.Title}' に " +
                      $"(動きを写す骨 {entry.Retargeter.PairCount} 本、影のメッシュ {shadows} 個)");
        }
        catch (Exception e)
        {
            Failed.Add(key);
            log?.Error($"Model: VRM の読み込みに失敗 ({Path.GetFileName(file)}): {e}");
            ModelLab.Ctx?.Notify($"VRM を読み込めませんでした: {e.Message}", RusK.API.NotifyLevel.Error);
            Cleanup(entry);
        }
    }

    public static void Restore(PlayerController player)
    {
        if (player == null || !Entries.TryGetValue(KeyOf(player), out var e)) return;
        Entries.Remove(e.Key);
        Cleanup(e);
    }

    public static void RestoreAll()
    {
        foreach (var e in Entries.Values.ToList()) Cleanup(e);
        Entries.Clear();
    }

    private static void Cleanup(Entry e)
    {
        foreach (var r in e.Hidden)
            if (r != null) r.forceRenderingOff = false;
        if (e.Animator != null) e.Animator.cullingMode = e.OriginalCulling;
        e.Retargeter?.RestoreAttachments();
        e.Model?.Destroy();
    }

    /// <summary>
    /// 影を落とすためのメッシュを重ねて置く。見た目に使う URP Unlit は影を落とさないので、
    /// 同じメッシュ・骨で「影だけ」を描く SkinnedMeshRenderer を作り、影を落とせる URP Simple Lit の材質を当てる
    /// (透明部分の切り抜きも付けるので、帽子のつばのギザギザなども影に出る)
    /// </summary>
    private static int AddShadowCasters(VrmModel model, int layer, List<Renderer> made)
    {
        Shader shader = null;
        shader = MaskModes.FindShader("Universal Render Pipeline/Simple Lit");
        if (shader == null) return 0;

        var cache = new Dictionary<(IntPtr, bool, float), Material>();
        Material ShadowMaterial(Material src)
        {
            Texture tex = null;
            try { tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture; } catch { }
            // 切り抜き: URP の方式は _ALPHATEST_ON、ゲームのシェーダーはキャラ用の切り抜き (閾値は反転して入れてある)
            bool clip = src.IsKeywordEnabled("_ALPHATEST_ON");
            float cutoff = clip && src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f;
            if (!clip && src.IsKeywordEnabled("_USEALPHACLIPPING_ON") && src.HasProperty("_UseCharacterAlphaClipMap")
                && src.GetFloat("_UseCharacterAlphaClipMap") > 0f)
            {
                clip = true;
                cutoff = 1f - src.GetFloat("_CharacterAlphaClipCutoff");
            }
            var key = (tex != null ? tex.Pointer : IntPtr.Zero, clip, cutoff);
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(shader) { name = "RusK_Shadow" };
            m.SetFloat("_Cull", 0f); // 両面表示の布も、どちら向きでも影を落とす
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (clip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            model.Assets.Add(m);
            cache[key] = m;
            return m;
        }

        int count = 0;
        foreach (var r in model.Renderers.ToList())
        {
            var src = r.TryCast<SkinnedMeshRenderer>();
            if (src == null) continue;
            var go = new GameObject(src.gameObject.name + "_shadow") { layer = layer };
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition;
            go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale = src.transform.localScale;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = model.ShadowMeshes.TryGetValue(src.sharedMesh.Pointer, out var sm) ? sm : src.sharedMesh;
            smr.bones = src.bones;
            smr.rootBone = src.rootBone;
            smr.updateWhenOffscreen = true;
            smr.sharedMaterials = src.sharedMaterials.Select(ShadowMaterial).ToArray();
            smr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            smr.receiveShadows = false;
            made.Add(smr);
            count++;
        }
        return count;
    }

    /// <summary>キャラの体のメッシュ (装備・武器は除く)</summary>
    private static IEnumerable<SkinnedMeshRenderer> BodyRenderers(Transform character)
    {
        foreach (var r in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!r.enabled) continue;
            bool equip = false;
            for (var c = r.transform; c != null && c != character; c = c.parent)
                if (c.name.StartsWith("WeaponHolder") || c.name.StartsWith("Equip_") || c.name.StartsWith("RusK_"))
                    equip = true;
            if (!equip) yield return r;
        }
    }

    /// <summary>
    /// VRM の表示を、ゲームの元の体の状態に合わせる。
    ///   ゲームが演出で体を消した (元の体の enabled が全部 OFF) → VRM も消す
    ///   Camera View が一人称で体を隠している (共有の値 camera.hideBody) → 見た目だけ消して影は残す
    /// </summary>
    private static void UpdateVisibility(Entry e)
    {
        bool shown = e.Hidden.Count == 0;
        foreach (var r in e.Hidden)
        {
            if (r == null) continue;
            if (!r.forceRenderingOff) r.forceRenderingOff = true; // ゲームや他の Mod が戻していたら、また止める
            if (r.enabled) shown = true;
        }
        // MaterialController の InTelepoting() は普段から True のことがある (テレポートの演出中という意味ではない) ので使わない。
        // 透明度 (GetLastAppliedCharacterAlpha) も、一度も変えていないときの値が分からないので使わない

        bool firstPerson = !e.IsShow && RuskShared.Get("camera.hideBody", false) && PlayerRef.Current?.transform.Pointer == e.Key;

        // 調査用: 表示の状態が変わったら理由をログに出す
        var state = (shown, firstPerson);
        if (state != e.LastState)
        {
            e.LastState = state;
            int on = e.Hidden.Count(r => r != null && r.enabled);
            ModelLab.Ctx?.Log.Info($"Model: VRM の表示 見た目={(shown && !firstPerson ? "表示" : "非表示")} 影={(shown ? "表示" : "非表示")} " +
                                   $"(元の体の表示 ON {on}/{e.Hidden.Count}、camera.hideBody={RuskShared.Get("camera.hideBody", false)})");
        }
        foreach (var r in e.Model.Renderers)
        {
            if (r == null) continue;
            bool on = shown && !firstPerson;
            if (r.enabled != on) r.enabled = on;
        }
        foreach (var r in e.Shadows)
            if (r != null && r.enabled != shown) r.enabled = shown;
    }

    // ------------------------------------------------------------------ 毎フレーム (CameraController.LateUpdate の後)

    private static int _lastShowFrame = -1;

    /// <summary>調査用: 動きを記録するキャラ (見せるためのモデルを優先) と、VRM に写している骨</summary>
    public static (Transform root, HashSet<IntPtr> mapped, string name)? RecordTarget()
    {
        var e = Entries.Values.Where(x => x.Root != null && x.Root.gameObject.activeInHierarchy)
            .OrderByDescending(x => x.IsShow).FirstOrDefault();
        if (e == null) return null;
        var mapped = new HashSet<IntPtr>(e.Retargeter.MappedSources.Where(t => t != null).Select(t => t.Pointer));
        return (e.Root, mapped, e.Root.name);
    }

    /// <summary>
    /// 毎フレーム、アニメーションの後に呼ぶ。shows = false: 操作キャラ (CameraController.LateUpdate の後)。
    /// shows = true: 見せるためのモデル (CinemachineBrain.LateUpdate の後。タイトル画面では CameraController が動かないため)
    /// </summary>
    public static void LateTick(bool shows = false)
    {
        if (shows)
        {
            if (Time.frameCount == _lastShowFrame) return; // カメラが複数あっても 1 フレームに 1 回
            _lastShowFrame = Time.frameCount;
        }
        foreach (var e in Entries.Values.Where(x => x.IsShow == shows).ToList())
        {
            if (e.Root == null || (!e.IsShow && e.Player == null) || e.Model?.Root == null)
            {
                // キャラが作り直された (ステージの移動など)。次の自動の付け直しで新しいキャラに付ける
                Entries.Remove(e.Key);
                Cleanup(e);
                continue;
            }
            // キャラが控えに回って非表示のとき (見せるためのモデルは画面を閉じたとき) は VRM も隠す
            bool active = e.Root.gameObject.activeInHierarchy;
            if (e.Model.Root.activeSelf != active) e.Model.Root.SetActive(active);
            if (!active) continue;
            var vrmRoot = e.Model.Root.transform;
            vrmRoot.SetPositionAndRotation(e.Root.position, e.Root.rotation);
            var scale = e.Root.lossyScale;
            if (vrmRoot.localScale != scale) vrmRoot.localScale = scale;

            UpdateVisibility(e);

            try
            {
                e.Retargeter.Update();
                e.Model.Skirt?.Update();
                // 揺れ物と表情は、動きを写した後に (ゲームの時間の流れ = スローや一時停止に合わせる)
                e.Model.Springs?.Update(Time.deltaTime);
                e.Model.Expressions?.Update(Time.deltaTime);
                MotionRecorder.Sample(e.Root);
            }
            catch (Exception ex)
            {
                ModelLab.Ctx?.Log.Error($"Model: 動きを写せません: {ex.Message}");
                Entries.Remove(e.Key);
                Cleanup(e);
            }
        }

        AutoApply();
    }

    /// <summary>
    /// 1 秒に 1 回、設定された VRM が付いていないキャラに付ける。
    /// 今の操作キャラと、タイトル画面・キャラクター画面・装備画面の見せるためのモデル (CharacterShowController)
    /// </summary>
    private static void AutoApply()
    {
        if (Assignments.Count == 0 || Time.unscaledTime < _nextAuto) return;
        _nextAuto = Time.unscaledTime + 1f;

        try
        {
            var util = GameUtil.Instance;
            if (util != null && util.GetInLoading()) return;
        }
        catch { }

        var p = PlayerRef.Current;
        if (p != null && p.gameObject.activeInHierarchy) TryAuto(p.transform, Id(p), p);

        foreach (var show in Resources.FindObjectsOfTypeAll<CharacterShowController>())
        {
            if (show == null || show.gameObject.scene.name == null || !show.gameObject.activeInHierarchy) continue;
            long id = ShowIds.TryGetValue(show.Pointer, out var known) ? known : GuessId(show.gameObject.name);
            if (id < 0) continue;
            TryAuto(show.transform, id, null);
        }
    }

    private static void TryAuto(Transform character, long id, PlayerController player)
    {
        var key = character.Pointer;
        if (Entries.ContainsKey(key) || Failed.Contains(key)) return;
        if (!Assignments.TryGetValue(id, out var rel)) return;
        var file = Path.Combine(ModelsDir, rel);
        if (!System.IO.File.Exists(file))
        {
            Failed.Add(key);
            ModelLab.Ctx?.Log.Warning($"Model: 設定された VRM がありません: {file}");
            return;
        }
        Apply(character, id, player, file);
    }

    /// <summary>
    /// 見せるためのモデルの名前 (例: WhiteLight_show_Instance_0) から、キャラの ID を推測する。
    /// InitialSetting を見逃したとき (Custom Model を読み込み直したときなど) 用。キャラのプレハブ名 (WhiteLight_skin) の頭と比べる
    /// </summary>
    private static long GuessId(string name)
    {
        int cut = name.IndexOf('_');
        string head = cut > 0 ? name.Substring(0, cut) : name;
        foreach (var m in ModelSwap.Characters())
        {
            string prefab = Path.GetFileNameWithoutExtension(m.prefabPath ?? "");
            int c = prefab.IndexOf('_');
            string ph = c > 0 ? prefab.Substring(0, c) : prefab;
            if (string.Equals(ph, head, StringComparison.OrdinalIgnoreCase)) return (long)Math.Round(m.id);
        }
        return -1;
    }

    /// <summary>調査用: 材質の元にしたゲームの材質の設定を一度だけログに出す</summary>
    private static void LogTemplate(Material m)
    {
        if (_loggedTemplate) return;
        _loggedTemplate = true;
        try
        {
            var sh = m.shader;
            var sb = new StringBuilder();
            sb.AppendLine($"Model: 材質の元 '{m.name}' シェーダー '{sh.name}' キーワード [{string.Join(" ", m.shaderKeywords)}]");
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                var type = sh.GetPropertyType(i);
                int id = sh.GetPropertyNameId(i);
                string v = type switch
                {
                    ShaderPropertyType.Vector => m.GetVector(id).ToString(),
                    ShaderPropertyType.Color => m.GetColor(id).ToString(),
                    ShaderPropertyType.Float or ShaderPropertyType.Range => m.GetFloat(id).ToString("0.###"),
                    ShaderPropertyType.Texture => m.GetTexture(id)?.name ?? "-",
                    _ => "?",
                };
                sb.AppendLine($"    {sh.GetPropertyName(i)} ({type}) = {v}");
            }
            ModelLab.Ctx?.Log.Info(sb.ToString());
        }
        catch { }
    }
}
