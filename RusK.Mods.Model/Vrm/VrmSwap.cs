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
        /// <summary>ゲームのキャラの顔 (表情・口パクのブレンドシェイプ) と、名前 → 番号</summary>
        public SkinnedMeshRenderer Face;
        public readonly Dictionary<string, int> FaceShapes = new();
        /// <summary>装飾品の差し込み口 (番号 1～4 → WeaponHolder_n) と、表示の設定で隠した装飾品の描画部品</summary>
        public List<(int slot, Transform holder)> Holders;
        public readonly Dictionary<IntPtr, Renderer> HiddenAccessories = new();
    }

    private static readonly Dictionary<IntPtr, Entry> Entries = new();

    /// <summary>キャラの ID → VRM のファイル名 (RusK\models からの相対パス)</summary>
    private static readonly Dictionary<long, string> Assignments = new();

    /// <summary>付けようとして失敗したキャラ (同じキャラに何度も試さない)</summary>
    private static readonly HashSet<IntPtr> Failed = new();

    private static float _nextAuto;

    /// <summary>
    /// 作ったモデルの置き場 (ファイルごと)。キャラが作り直されたら (ステージの移動・画面の切り替え)、モデルを捨てずに隠して置いておき、
    /// 次に同じモデルのキャラが出たら付け直すだけにする (作り直しは重く、初めての場面ではモデルが遅れて付き、かくつく)。
    /// タイトル画面・キャラの画面で見せたモデルも入るので、初めての出撃のときにはできあがっている
    /// </summary>
    private sealed class Pooled
    {
        public VrmModel Model;
        public Dictionary<HumanBodyBones, (Transform bone, Humanoid.Rest rest)> Rest;
        public readonly List<Renderer> Shadows = new();
    }

    private static readonly Dictionary<string, Stack<Pooled>> Pool = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<IntPtr, Dictionary<HumanBodyBones, (Transform bone, Humanoid.Rest rest)>> RestOf = new();

    private static VrmLoader.Templates _lastTemplates;
    private static readonly HashSet<string> Prebuilt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// まだ一度も作っていない割り当てのモデルを、先に作って置き場に入れておく (1 回に 1 つ)。
    /// 材質の元 (ゲームの体の材質) が要るので、何かのキャラに一度モデルを付けた後 (タイトル画面など) から
    /// </summary>
    private static void Prebuild(bool loading)
    {
        // 作るのは重い (1 体 0.3〜1.2 秒止まる) ので、戦っていない間 (読み込み中・タイトル・メニュー) だけ
        var cur = PlayerRef.Current;
        bool playing = cur != null && cur.gameObject.activeInHierarchy;
        if (!loading && playing) return;
        if (_lastTemplates?.Opaque == null) _lastTemplates = FindTemplates();
        if (_lastTemplates?.Opaque == null) return;
        foreach (var rel in Assignments.Values.Distinct())
        {
            var file = Path.Combine(ModelsDir, rel);
            if (!Prebuilt.Add(file)) continue;
            if (!File.Exists(file) || (Pool.TryGetValue(file, out var st) && st.Count > 0) || Entries.Values.Any(e => string.Equals(e.File, file, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var log = VrmEnv.Ctx?.Log;
                var model = file.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase)
                    ? Pmx.PmxLoader.Load(file, _lastTemplates, s => log?.Info("Model: " + s))
                    : VrmLoader.Load(file, _lastTemplates, s => log?.Info("Model: " + s));
                var rest = Humanoid.VrmRest(model);
                RestOf[model.Root.Pointer] = rest;
                Object.DontDestroyOnLoad(model.Root);
                foreach (var r in model.Renderers) r.shadowCastingMode = ShadowCastingMode.Off;
                var pooled = new Pooled { Model = model, Rest = rest };
                AddShadowCasters(model, model.Root.layer, pooled.Shadows);
                model.Root.SetActive(false);
                if (!Pool.TryGetValue(file, out var stack)) Pool[file] = stack = new Stack<Pooled>();
                stack.Push(pooled);
                log?.Info($"Model: '{rel}' を先に作っておきました ({watch.ElapsedMilliseconds} ms)");
            }
            catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"Model: '{rel}' を先に作れません: {e.Message}"); }
            return; // 1 回に 1 つ
        }
    }

    /// <summary>その場にいるゲームのキャラ (操作キャラ・見せるためのモデル) の体から、材質の元を取る</summary>
    private static VrmLoader.Templates FindTemplates()
    {
        try
        {
            var roots = new List<Transform>();
            foreach (var pc in SceneChars.Players()) roots.Add(pc.transform);
            foreach (var sc in SceneChars.Shows()) roots.Add(sc.transform);
            foreach (var r in roots)
            {
                var body = BodyRenderers(r).ToList();
                var opaque = body.SelectMany(x => x.sharedMaterials).FirstOrDefault(m => m != null && m.renderQueue < 2500);
                if (opaque == null) continue;
                return new VrmLoader.Templates
                {
                    Opaque = opaque,
                    Transparent = body.SelectMany(x => x.sharedMaterials).FirstOrDefault(m => m != null && m.renderQueue >= 3000),
                    OutlineWidth = 0f,
                };
            }
        }
        catch { }
        return null;
    }

    /// <summary>使い終わったモデルを置き場に戻す (割り当てから外れたファイルなら捨てる)</summary>
    private static void Park(Entry e)
    {
        var model = e.Model;
        if (model?.Root == null) return;
        bool assigned = e.File != null && Assignments.Values.Any(rel => string.Equals(Path.Combine(ModelsDir, rel), e.File, StringComparison.OrdinalIgnoreCase));
        if (!assigned || !RestOf.TryGetValue(model.Root.Pointer, out var rest)) { RestOf.Remove(model.Root.Pointer); model.Destroy(); return; }
        model.Root.SetActive(false);
        var p = new Pooled { Model = model, Rest = rest };
        p.Shadows.AddRange(e.Shadows);
        if (!Pool.TryGetValue(e.File, out var stack)) Pool[e.File] = stack = new Stack<Pooled>();
        stack.Push(p);
    }
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
            // VRM と PMX (MMD のモデル。テクスチャと同じフォルダごと置く)
            return Directory.GetFiles(ModelsDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f).ToList();
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

    /// <summary>割り当てたモデルのファイル (先読み用)</summary>
    public static IEnumerable<string> AssignedFiles() =>
        Assignments.Values.Distinct().Select(rel => Path.Combine(ModelsDir, rel)).ToList();

    public static void LoadAssignments()
    {
        MaskModes.Load();
        SkirtTuning.Load();
        AccessoryVisibility.Load();
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
            _lastTemplates ??= templates;
            LogTemplate(templates.Opaque);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Pooled pooled = null;
            if (Pool.TryGetValue(file, out var stack))
                while (stack.Count > 0 && pooled == null)
                {
                    var p = stack.Pop();
                    if (p.Model?.Root != null) pooled = p;
                }
            Dictionary<HumanBodyBones, (Transform bone, Humanoid.Rest rest)> dstRest;
            if (pooled != null)
            {
                entry.Model = pooled.Model;
                entry.Model.Root.SetActive(true);
                dstRest = pooled.Rest;
                entry.Shadows.AddRange(pooled.Shadows);
            }
            else
            {
                entry.Model = file.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase)
                    ? Pmx.PmxLoader.Load(file, templates, s => log?.Info("Model: " + s))
                    : VrmLoader.Load(file, templates, s => log?.Info("Model: " + s));
                // 基準の姿勢 (VRM は読み込み直後の T ポーズ、根元が原点のうちに覚える)
                dstRest = Humanoid.VrmRest(entry.Model);
                RestOf[entry.Model.Root.Pointer] = dstRest;
            }
            var root = entry.Model.Root.transform;
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
            int shadows = pooled != null ? entry.Shadows.Count : AddShadowCasters(entry.Model, layer, entry.Shadows);
            foreach (var r in entry.Shadows)
                if (r != null) r.gameObject.layer = layer;
            try { entry.Materials = player != null ? player.GetMaterialController() : null; } catch { }

            entry.Retargeter = new Retargeter(character, srcRest, root, dstRest);

            // 元の体を隠す。描画だけを止める (forceRenderingOff) ので、ゲームが演出で体を表示・非表示にした状態
            // (enabled) はそのまま残り、VRM の表示をそれに合わせられる
            foreach (var r in body)
            {
                r.forceRenderingOff = true;
                entry.Hidden.Add(r);
            }

            // ゲームのキャラの顔 (口パク・表情・まばたきのブレンドシェイプを持つメッシュ) を探しておく
            foreach (var r in body)
            {
                var mesh = r.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                var names = new Dictionary<string, int>();
                for (int i = 0; i < mesh.blendShapeCount; i++) names[mesh.GetBlendShapeName(i)] = i;
                if (!names.ContainsKey("Eyes_Closed") && !names.ContainsKey("Mouth_Shout")) continue;
                entry.Face = r;
                foreach (var kv in names) entry.FaceShapes[kv.Key] = kv.Value;
                break;
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
            entry.Model.AfterPose?.Invoke();
            entry.Model.Springs?.Reset();
            log?.Info($"Model: {CharacterNames.Get(id)}{(entry.IsShow ? " (画面に見せるモデル)" : "")} の見た目を VRM '{entry.Model.Title}' に " +
                      $"(動きを写す骨 {entry.Retargeter.PairCount} 本、影のメッシュ {shadows} 個、{(pooled != null ? "使い回し" : "読み込み")} {watch.ElapsedMilliseconds} ms)");
        }
        catch (Exception e)
        {
            Failed.Add(key);
            log?.Error($"Model: VRM の読み込みに失敗 ({Path.GetFileName(file)}): {e}");
            ModelLab.Ctx?.Notify(L.T("VRM を読み込めませんでした: {0}", e.Message), RusK.API.NotifyLevel.Error);
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
        foreach (var r in e.HiddenAccessories.Values)
            if (r != null) r.forceRenderingOff = false;
        e.HiddenAccessories.Clear();
        e.Retargeter?.RestoreAttachments();
        Park(e);
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
    internal static IEnumerable<SkinnedMeshRenderer> BodyRenderers(Transform character)
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

    /// <summary>
    /// 装飾品の表示の設定 (AccessoryVisibility) を当てる。隠す部位の装飾品は描画だけを止め、表示に戻したら止めたものだけ戻す
    /// (Custom Item Model が元の装飾品を隠しているのは触らない)。装備の付け替えで中身が変わるので 10 フレームごとに見直す
    /// </summary>
    private static void UpdateAccessories(Entry e)
    {
        if (e.Holders != null && Time.frameCount % 10 != 0) return;
        if (e.Holders == null)
        {
            e.Holders = new List<(int, Transform)>();
            foreach (var t in e.Root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("WeaponHolder_")) continue;
                if (int.TryParse(t.name.Substring("WeaponHolder_".Length), out var slot) && slot >= 1 && slot <= 4)
                    e.Holders.Add((slot, t));
            }
        }
        foreach (var (slot, holder) in e.Holders)
        {
            if (holder == null) continue;
            bool hide = !AccessoryVisibility.IsShown(slot);
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (hide)
                {
                    if (!r.forceRenderingOff)
                    {
                        r.forceRenderingOff = true;
                        e.HiddenAccessories[r.Pointer] = r;
                    }
                }
                else if (e.HiddenAccessories.Remove(r.Pointer))
                    r.forceRenderingOff = false;
            }
        }
    }

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
        long t = Spike.Begin();
        try { LateTickCore(shows); }
        finally { Spike.End(shows ? "Model: 見せるモデルの更新" : "Model: キャラのモデルの更新 (動きを写す・揺れ物・表情)", t); }
    }

    private static void LateTickCore(bool shows)
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
            if (!e.IsShow && Id(e.Player) != e.Id)
            {
                // 同じ体が別のキャラに使い回された (Custom Character の新しいキャラと土台のキャラの切り替え)。付け直す
                Entries.Remove(e.Key);
                Cleanup(e);
                Failed.Remove(e.Key);
                _nextAuto = 0f;
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
            UpdateAccessories(e);

            try
            {
                long t0 = Prof.ElapsedTicks;
                e.Retargeter.Update();
                e.Model.AfterPose?.Invoke();
                e.Model.Skirt?.Update();
                long t1 = Prof.ElapsedTicks;
                // 揺れ物と表情は、動きを写した後に (ゲームの時間の流れ = スローや一時停止に合わせる)
                e.Model.Springs?.Update(Time.deltaTime);
                long t2 = Prof.ElapsedTicks;
                // 表情: ゲームのキャラの顔を写す (口パク・表情・まばたき)。顔が無ければ自動のまばたき
                if (e.Face != null)
                    e.Model.Expressions?.FromGame(n => e.FaceShapes.TryGetValue(n, out var i) ? e.Face.GetBlendShapeWeight(i) : 0f, Time.deltaTime);
                else
                    e.Model.Expressions?.Update(Time.deltaTime);
                MotionRecorder.Sample(e.Root);
                long t3 = Prof.ElapsedTicks;
                ProfAdd(e.Model.Title, t1 - t0, t2 - t1, t3 - t2);
            }
            catch (Exception ex)
            {
                ModelLab.Ctx?.Log.Error($"Model: 動きを写せません: {ex.Message}");
                Entries.Remove(e.Key);
                Cleanup(e);
            }
        }

        long ta = Spike.Begin();
        AutoApply();
        Spike.End("Model: 自動で付ける (AutoApply)", ta);
    }

    /// <summary>
    /// 1 秒に 1 回、設定された VRM が付いていないキャラに付ける。
    /// 今の操作キャラと、タイトル画面・キャラクター画面・装備画面の見せるためのモデル (CharacterShowController)
    /// </summary>
    private static void AutoApply()
    {
        if (Assignments.Count == 0 || Time.unscaledTime < _nextAuto) return;
        // 短い場面 (移動の演出など) でも付くように、こまめに見る
        _nextAuto = Time.unscaledTime + 0.2f;

        // 読み込み中 (移動の演出など) は、置き場にできあがったモデルがあるときだけ付ける (読み込みは重いので後で)
        bool loading = false;
        try
        {
            var util = GameUtil.Instance;
            loading = util != null && util.GetInLoading();
        }
        catch { }
        _poolOnly = loading;
        Prebuild(loading);

        var p = PlayerRef.Current;
        if (p != null && p.gameObject.activeInHierarchy) TryAuto(p.transform, Id(p), p);

        // Party の仲間 (一緒に戦うキャラ・控え) など、場面にいるほかのプレイヤーキャラにも付ける
        try
        {
            foreach (var other in SceneChars.Players())
                if (other != null && other != p && other.gameObject.activeInHierarchy) TryAuto(other.transform, Id(other), other);
        }
        catch { }

        foreach (var show in SceneChars.Shows())
        {
            if (show == null || !show.gameObject.activeInHierarchy) continue;
            long id = ShowIds.TryGetValue(show.Pointer, out var known) ? known : GuessId(show.gameObject.name);
            if (id < 0) continue;
            TryAuto(show.transform, id, null);
        }
    }

    private static bool _poolOnly;

    // ---- 重さの記録: モデルごとの 1 フレームあたりの時間 (動きを写す / 揺れ物 / 表情)。10 秒ごとにログへ
    private static readonly System.Diagnostics.Stopwatch Prof = System.Diagnostics.Stopwatch.StartNew();
    private static readonly Dictionary<string, (long pose, long spring, long face, int frames)> ProfSum = new();
    private static float _nextProf = 10f;
    /// <summary>重さの記録をログに出す (調べるときだけ)</summary>
    public static bool LogWeight = false;

    private static void ProfAdd(string title, long pose, long spring, long face)
    {
        ProfSum.TryGetValue(title, out var v);
        ProfSum[title] = (v.pose + pose, v.spring + spring, v.face + face, v.frames + 1);
        if (Time.unscaledTime < _nextProf || !LogWeight) return;
        _nextProf = Time.unscaledTime + 10f;
        double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        foreach (var kv in ProfSum)
        {
            var (po, sp, fa, n) = kv.Value;
            if (n == 0) continue;
            VrmEnv.Ctx?.Log.Info($"Model: (重さ) '{kv.Key}' 1 フレームあたり 動きを写す {po * ms / n:0.00} ms / 揺れ物 {sp * ms / n:0.00} ms / 表情 {fa * ms / n:0.00} ms ({n} フレーム)");
        }
        ProfSum.Clear();
    }

    private static void TryAuto(Transform character, long id, PlayerController player)
    {
        var key = character.Pointer;
        if (Entries.ContainsKey(key) || Failed.Contains(key)) return;
        if (!Assignments.TryGetValue(id, out var rel)) return;
        var file = Path.Combine(ModelsDir, rel);
        if (_poolOnly && !(Pool.TryGetValue(file, out var ready) && ready.Count > 0)) return;
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
