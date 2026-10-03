using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RusK.Mods.Model.Vrm;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.ItemModel;

/// <summary>装備 ID ごとの置き換えの設定</summary>
internal sealed class Assignment
{
    public string File;               // RusK\props からの相対パス
    public Vector3 Position;          // 元の武器・装備品からのずれ
    public Vector3 Rotation;          // 回転 (度)
    public float Scale = 1f;
    /// <summary>発光 (ON なら glb の発光より優先)</summary>
    public bool Glow;
    public int GlowColor;            // GlowColors の番号
    public float GlowStrength = 2f;
    /// <summary>設定を変えるたびに増やす (発光を当て直すため)</summary>
    public int Revision;

    public static readonly (string name, Color color)[] GlowColors =
    {
        ("白", Color.white), ("赤", new Color(1f, 0.2f, 0.2f)), ("橙", new Color(1f, 0.55f, 0.15f)),
        ("黄", new Color(1f, 0.9f, 0.2f)), ("緑", new Color(0.3f, 1f, 0.4f)), ("水色", new Color(0.3f, 0.9f, 1f)),
        ("青", new Color(0.25f, 0.45f, 1f)), ("紫", new Color(0.7f, 0.3f, 1f)), ("桃", new Color(1f, 0.45f, 0.8f)),
    };
}

/// <summary>
/// 武器・装備品の見た目を glb / PMX に置き換える。
///
/// 割り当ては装備 ID ごと (assignments.txt の「1041|...」)。キャラ ID も付けると (「9001:1041|...」)、
/// そのキャラが持っているときだけ置き換える (新しいキャラが土台のキャラの武器を借りているときに、自分の武器にする)。
///
/// ゲームの武器・装備品は、どれも装備 ID ごとの Equip_&lt;ID&gt; (WeaponController) で、手や頭・胸・腰の差し込み口
/// (WeaponHolder_0～4) に付く。しまった武器はゲームの置き場 (ResourceManager) に戻される。
/// そこで Equip_&lt;ID&gt; 自体の下に glb のモデルを付け、元の見た目を隠す。持つ・しまう・キャラが変わるといった動きは、
/// ゲームの Equip_ にそのまま付いていく。
/// </summary>
internal static class ItemModels
{
    private sealed class Tracked
    {
        /// <summary>武器・装飾品のモデル (Equip_&lt;ID&gt;)</summary>
        public GameObject Target;
        public long Id;
        /// <summary>今持っているキャラの ID (しまってある間は、最後に持っていたキャラ)</summary>
        public long Owner = -1;
        public IntPtr OwnerParent;
        public GameObject Attached;
        public string File;
        public readonly List<Renderer> Originals = new();
        public readonly List<Renderer> Mine = new();
        /// <summary>置き換えたモデルの材質 (描画部品ごと)。ゲームの材質の管理 (MaterialController) がキャラの下の材質を書き換えるので、毎フレーム戻す</summary>
        public readonly List<Material[]> MineMaterials = new();
        public int AppliedRevision = -1;
        /// <summary>glb 自身の発光 (発光の設定を OFF にしたときに戻すため)。この装備品用に複製した材質ごと</summary>
        public readonly List<(Material mat, Color color, Texture tex)> OwnEmission = new();
    }

    /// <summary>"装備 ID" または "キャラ ID:装備 ID" → 置き換え</summary>
    private static readonly Dictionary<string, Assignment> Assignments = new();
    private static readonly Dictionary<IntPtr, Tracked> TrackedWeapons = new();
    private static readonly Dictionary<string, GameObject> Prototypes = new();
    private static readonly HashSet<string> FailedFiles = new();
    private static GameObject _protoHolder;
    private static float _nextScan;

    public static string PropsDir => Path.GetFullPath(Path.Combine(VrmEnv.Ctx.DataDirectory, "..", "..", "props"));
    private static string AssignFile => Path.Combine(VrmEnv.Ctx.DataDirectory, "assignments.txt");

    public static IEnumerable<string> Files()
    {
        try
        {
            Directory.CreateDirectory(PropsDir);
            return Directory.GetFiles(PropsDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static Assignment Get(long id) => Assignments.TryGetValue(id.ToString(), out var a) ? a : null;

    /// <summary>キャラ owner が持っている装備 id の置き換え (キャラ専用があればそちら)</summary>
    public static Assignment GetFor(long owner, long id) =>
        owner >= 0 && Assignments.TryGetValue($"{owner}:{id}", out var a) ? a : Get(id);

    /// <summary>今シーンに出ている武器・装備品の ID (画面で「装備中」を先に並べるため)</summary>
    public static HashSet<long> InScene() => TrackedWeapons.Values.Where(t => t.Target != null).Select(t => t.Id).ToHashSet();

    // ------------------------------------------------------------------ 設定

    public static void Assign(long id, string file)
    {
        var key = id.ToString();
        if (file == null) Assignments.Remove(key);
        else
        {
            var rel = Path.GetRelativePath(PropsDir, file);
            if (!Assignments.TryGetValue(key, out var a)) Assignments[key] = a = new Assignment();
            a.File = rel;
            FailedFiles.Remove(rel);
        }
        Save();
    }

    public static void Load()
    {
        Assignments.Clear();
        try
        {
            if (!System.IO.File.Exists(AssignFile)) return;
            foreach (var line in System.IO.File.ReadAllLines(AssignFile))
            {
                var p = line.Split('|');
                if (p.Length < 5) continue;
                var key = p[0].Trim();
                var ids = key.Split(':');
                if (ids.Length > 2 || ids.Any(x => !long.TryParse(x, out _))) continue;
                var a = new Assignment
                {
                    File = p[1], Position = Vec(p[2]), Rotation = Vec(p[3]),
                    Scale = float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1f,
                };
                if (p.Length >= 8)
                {
                    a.Glow = p[5] == "1";
                    int.TryParse(p[6], out a.GlowColor);
                    if (float.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var gs)) a.GlowStrength = gs;
                }
                Assignments[key] = a;
            }
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"ItemModel: assignments.txt を読めません: {e.Message}"); }
        AddPacks();
    }

    /// <summary>キャラの Mod Pack (character.json の "weapon") の武器。手で割り当てたものが優先。保存はしない</summary>
    private static readonly HashSet<string> PackKeys = new();

    private static void AddPacks()
    {
        PackKeys.Clear();
        try
        {
            foreach (var p in RusK.Mods.Shared.CharacterPacks.Load(VrmEnv.Ctx.DataDirectory, s => VrmEnv.Ctx?.Log.Warning("ItemModel: " + s)))
            {
                if (p.WeaponFile == null || p.WeaponEquip < 0) continue;
                var key = $"{p.Id}:{p.WeaponEquip}";
                if (Assignments.ContainsKey(key)) continue;
                if (!System.IO.File.Exists(p.WeaponFile)) { VrmEnv.Ctx?.Log.Warning($"ItemModel: キャラ {p.Id} の武器がありません: {p.WeaponFile}"); continue; }
                Assignments[key] = new Assignment
                {
                    File = p.WeaponFile,
                    Position = new Vector3(p.WeaponPosition[0], p.WeaponPosition[1], p.WeaponPosition[2]),
                    Rotation = new Vector3(p.WeaponRotation[0], p.WeaponRotation[1], p.WeaponRotation[2]),
                    Scale = p.WeaponScale, Glow = p.WeaponGlow, GlowColor = p.WeaponGlowColor, GlowStrength = p.WeaponGlowStrength,
                };
                PackKeys.Add(key);
            }
            if (PackKeys.Count > 0) VrmEnv.Ctx?.Log.Info($"ItemModel: Mod Pack の武器 {PackKeys.Count} 個");
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"ItemModel: Mod Pack を読めません: {e.Message}"); }
    }

    private static DateTime _stamp;

    /// <summary>assignments.txt を手で書き換えたら読み直す (位置・回転・大きさの微調整をゲームを起動し直さずに試せる)</summary>
    private static void Watch()
    {
        try
        {
            var stamp = System.IO.File.Exists(AssignFile) ? System.IO.File.GetLastWriteTimeUtc(AssignFile) : default;
            if (_stamp != default && stamp != _stamp)
            {
                Load();
                foreach (var a in Assignments.Values) a.Revision++;
                VrmEnv.Ctx?.Log.Info("ItemModel: assignments.txt が変わったので読み直しました");
            }
            _stamp = stamp;
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(VrmEnv.Ctx.DataDirectory);
            string V(Vector3 v) => string.Join(",", new[] { v.x, v.y, v.z }.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture)));
            System.IO.File.WriteAllLines(AssignFile, Assignments.Where(kv => !PackKeys.Contains(kv.Key)).Select(kv =>
                $"{kv.Key}|{kv.Value.File}|{V(kv.Value.Position)}|{V(kv.Value.Rotation)}|" +
                kv.Value.Scale.ToString("0.###", CultureInfo.InvariantCulture) +
                $"|{(kv.Value.Glow ? 1 : 0)}|{kv.Value.GlowColor}|" +
                kv.Value.GlowStrength.ToString("0.##", CultureInfo.InvariantCulture)), new UTF8Encoding(false));
            _stamp = System.IO.File.GetLastWriteTimeUtc(AssignFile);
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"ItemModel: assignments.txt を保存できません: {e.Message}"); }
    }

    private static Vector3 Vec(string s)
    {
        var f = s.Split(',').Select(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f).ToArray();
        return f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : Vector3.zero;
    }

    // ------------------------------------------------------------------ 毎フレーム

    public static void Update()
    {
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.5f;
            Scan();
            Watch();
        }

        foreach (var t in TrackedWeapons.Values.ToList())
        {
            if (t.Target == null) continue; // 破棄されたものは下でまとめて外す
            try { Apply(t); }
            catch (Exception e)
            {
                VrmEnv.Ctx?.Log.Warning($"ItemModel: 装備 {t.Id} の置き換えに失敗: {e.Message}");
                Detach(t);
            }
        }
        // 破棄された武器を一覧から外す
        foreach (var key in TrackedWeapons.Where(kv => kv.Value.Target == null).Select(kv => kv.Key).ToList())
            TrackedWeapons.Remove(key);
    }

    /// <summary>
    /// シーン内の武器・装飾品を探す。
    /// 武器は WeaponController が付いている (しまってゲームの置き場にあるものも含む)。
    /// 肩・腰・頭の装飾品には付いていないので、差し込み口 (WeaponHolder) が持っている装備のモデル (m_equipGb) から探す
    /// </summary>
    private static void Scan()
    {
        // ゲームの全部の部品から探すと重い (1 回 40 ms 以上かかり、引っかかる)。武器・装飾品が付くのはキャラの差し込み口なので、
        // 場にいるキャラ (操作キャラ・仲間) と見せるためのモデルの中だけを探す。しまった武器は、手に戻ったときに見つかる
        try
        {
            var roots = new List<Transform>();
            foreach (var pc in RusK.Mods.Shared.SceneChars.Players()) roots.Add(pc.transform);
            foreach (var sc in RusK.Mods.Shared.SceneChars.Shows()) roots.Add(sc.transform);
            var weapons = new List<WeaponController>();
            var holders = new List<WeaponHolder>();
            foreach (var r in roots)
            {
                weapons.AddRange(r.GetComponentsInChildren<WeaponController>(true));
                holders.AddRange(r.GetComponentsInChildren<WeaponHolder>(true));
            }
            foreach (var w in weapons)
            {
                if (w == null) continue;
                long id = -1;
                try { id = (long)Math.Round(w.GetEquipSetting()?.equipId ?? -1); } catch { }
                Track(w.gameObject, id);
            }
            foreach (var h in holders)
            {
                if (h == null) continue;
                GameObject gb = null;
                long id = -1;
                try
                {
                    gb = h.m_equipGb;
                    id = (long)Math.Round(h.m_equipSetting?.equipId ?? -1);
                }
                catch { }
                if (gb != null) Track(gb, id);
            }
        }
        catch { }
    }

    private static void Track(GameObject target, long id)
    {
        if (target == null || id < 0 || TrackedWeapons.ContainsKey(target.Pointer)) return;
        TrackedWeapons[target.Pointer] = new Tracked { Target = target, Id = id };
    }

    private static void Apply(Tracked t)
    {
        UpdateOwner(t);
        var a = GetFor(t.Owner, t.Id);
        if (a == null || string.IsNullOrEmpty(a.File) || FailedFiles.Contains(a.File))
        {
            if (t.Attached != null) Detach(t);
            return;
        }

        if (t.Attached == null || t.File != a.File)
        {
            Detach(t);
            Attach(t, a.File);
            if (t.Attached == null) return;
        }

        // 位置・回転・大きさの調整
        var tr = t.Attached.transform;
        tr.localPosition = a.Position;
        tr.localEulerAngles = a.Rotation;
        tr.localScale = Vector3.one * a.Scale;

        if (t.AppliedRevision != a.Revision)
        {
            ApplyGlow(t, a);
            t.AppliedRevision = a.Revision;
        }

        // ゲームが材質を書き換えていたら戻す (元の武器の材質・テクスチャが貼られて模様がおかしくなる)
        for (int i = 0; i < t.Mine.Count && i < t.MineMaterials.Count; i++)
        {
            var r = t.Mine[i];
            if (r == null) continue;
            var cur = r.sharedMaterials;
            var mine = t.MineMaterials[i];
            bool same = cur.Length == mine.Length;
            for (int k = 0; same && k < cur.Length; k++) same = cur[k] == mine[k];
            if (!same) r.sharedMaterials = mine;
        }

        // 元の見た目は描かない (ゲームが表示し直しても映らない forceRenderingOff を使う)。
        // ゲームが武器をしまって元の見た目を消したとき (enabled = false) は、置き換えたモデルも消す
        bool visible = false;
        foreach (var r in t.Originals)
        {
            if (r == null) continue;
            r.forceRenderingOff = true;
            if (r.enabled) visible = true;
        }
        if (t.Originals.Count == 0) visible = true;
        foreach (var r in t.Mine)
            if (r != null && r.enabled != visible) r.enabled = visible;
    }

    /// <summary>武器の親が変わったら (手に持った・しまった)、持っているキャラを調べ直す。しまってある間は前の持ち主のまま</summary>
    private static void UpdateOwner(Tracked t)
    {
        var parent = t.Target.transform.parent;
        var ptr = parent != null ? parent.Pointer : IntPtr.Zero;
        if (ptr == t.OwnerParent) return;
        t.OwnerParent = ptr;
        try
        {
            var p = t.Target.GetComponentInParent<PlayerController>();
            if (p != null) t.Owner = (long)Math.Round(p.GetPlayerId());
        }
        catch { }
    }

    private static void Attach(Tracked t, string file)
    {
        var proto = Prototype(file, t.Target);
        if (proto == null) return;

        t.Originals.Clear();
        foreach (var r in t.Target.GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetIl2CppType().Name == "ParticleSystemRenderer") continue; // エフェクトは残す
            if (r.transform.IsChildOf(t.Target.transform) && r.gameObject.name.StartsWith("RusK_")) continue;
            t.Originals.Add(r);
        }

        var go = Object.Instantiate(proto, t.Target.transform, false);
        go.name = "RusK_ItemModel";
        go.SetActive(true);
        int layer = t.Originals.Count > 0 ? t.Originals[0].gameObject.layer : t.Target.layer;
        foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
        t.Mine.Clear();
        t.Mine.AddRange(go.GetComponentsInChildren<Renderer>(true));

        // 発光の設定は装備品ごとに違うので、材質をこの装備品用に複製し、glb 自身の発光を覚えておく
        t.OwnEmission.Clear();
        t.MineMaterials.Clear();
        foreach (var r in t.Mine)
        {
            var mats = r.sharedMaterials.Select(m => m != null ? new Material(m) : null).ToArray();
            r.sharedMaterials = mats;
            t.MineMaterials.Add(mats);
            foreach (var m in mats)
            {
                if (m == null) continue;
                var c = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                var tex = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
                bool on = m.HasProperty("_UseEmission") && m.GetFloat("_UseEmission") > 0.5f;
                t.OwnEmission.Add((m, on ? c : Color.black, tex));
            }
        }
        t.AppliedRevision = -1;
        t.Attached = go;
        t.File = file;
        VrmEnv.Ctx?.Log.Info($"ItemModel: 装備 {t.Id} ('{t.Target.name}'、持ち主 {t.Owner}) を '{file}' に置き換え");
    }

    /// <summary>発光の設定を当てる。OFF なら glb 自身の発光に戻す</summary>
    private static void ApplyGlow(Tracked t, Assignment a)
    {
        var glow = Assignment.GlowColors[Mathf.Clamp(a.GlowColor, 0, Assignment.GlowColors.Length - 1)].color * a.GlowStrength;
        foreach (var (m, color, tex) in t.OwnEmission)
        {
            if (m == null) continue;
            if (a.Glow) VrmLoader.SetEmission(m, glow, m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
            else VrmLoader.SetEmission(m, color, tex);
        }
    }

    private static void Detach(Tracked t)
    {
        foreach (var (m, _, _) in t.OwnEmission)
            if (m != null) Object.Destroy(m);
        t.OwnEmission.Clear();
        if (t.Attached != null) Object.Destroy(t.Attached);
        t.Attached = null;
        t.File = null;
        foreach (var r in t.Originals)
            if (r != null) r.forceRenderingOff = false;
        t.Originals.Clear();
        t.Mine.Clear();
        t.MineMaterials.Clear();
    }

    /// <summary>glb を一度だけ読み込み、非表示の元として取っておく (装備品ごとに複製して付ける)</summary>
    private static GameObject Prototype(string file, GameObject sample)
    {
        if (Prototypes.TryGetValue(file, out var proto) && proto != null) return proto;
        var path = Path.Combine(PropsDir, file);
        try
        {
            if (!System.IO.File.Exists(path)) throw new FileNotFoundException("ファイルがありません", path);

            // 材質の元: 元の武器・装備品の材質 (ゲームのトゥーンシェーダー)。
            // 武器を出す瞬間は、溶けて現れる演出の材質 (EasyGameStudio/disslove2 など) に差し替わっているので、
            // トゥーンシェーダーの材質だけを使う。無ければゲームが読み込んでいる材質から探す
            static bool Toon(Material m) => m != null && m.shader != null && m.shader.name.Contains("ToonLit");
            var mats = sample.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.GetIl2CppType().Name != "ParticleSystemRenderer")
                .SelectMany(r => r.sharedMaterials).Where(Toon).ToList();
            if (mats.Count == 0)
                mats = Resources.FindObjectsOfTypeAll<Material>().Where(Toon)
                    .OrderByDescending(m => m.name.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var templates = new VrmLoader.Templates
            {
                Opaque = mats.FirstOrDefault(m => m.renderQueue < 2500) ?? mats.FirstOrDefault(),
                Transparent = mats.FirstOrDefault(m => m.renderQueue >= 3000),
            };
            if (templates.Opaque == null) throw new InvalidOperationException("元の武器・装備品の材質が見つかりません");

            Action<string> log = s => VrmEnv.Ctx?.Log.Info("ItemModel: " + s);
            var model = path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase)
                ? RusK.Mods.Model.Vrm.Pmx.PmxLoader.Load(path, templates, log, prop: true)
                : VrmLoader.Load(path, templates, log, plainGltf: true);
            if (_protoHolder == null)
            {
                _protoHolder = new GameObject("RusK_ItemModelPrototypes");
                _protoHolder.SetActive(false);
                Object.DontDestroyOnLoad(_protoHolder);
            }
            model.Root.transform.SetParent(_protoHolder.transform, false);
            Prototypes[file] = model.Root;
            return model.Root;
        }
        catch (Exception e)
        {
            FailedFiles.Add(file);
            VrmEnv.Ctx?.Log.Error($"ItemModel: glb を読み込めません ({file}): {e}");
            VrmEnv.Ctx?.Notify(L.T("glb を読み込めませんでした: {0}", Path.GetFileName(file)), RusK.API.NotifyLevel.Error);
            return null;
        }
    }

    public static void RestoreAll()
    {
        foreach (var t in TrackedWeapons.Values) Detach(t);
        TrackedWeapons.Clear();
        foreach (var p in Prototypes.Values)
            if (p != null) Object.Destroy(p);
        Prototypes.Clear();
        if (_protoHolder != null) Object.Destroy(_protoHolder);
        _protoHolder = null;
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(PropsDir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{PropsDir}\"");
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"フォルダを開けません: {e.Message}"); }
    }
}
