using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Character;

/// <summary>
/// 新しいキャラをゲームに足す:
/// - キャラの一覧 (CharacterContainer.characters) に、土台のキャラの定義 (MotionManager、ScriptableObject) を複製して ID を変えたものを足す
/// - 遊んでいる間だけ、セーブ (GameSave) にそのキャラの解放とスキルのレベル (土台からもらう) を足す
/// - 保存 (GameUtil.SaveGame) の瞬間だけ、それらを抜き、最後に使ったキャラが新しいキャラなら土台の ID にする。保存が終わったら戻す
///   (Mod を外してもセーブがそのまま読めるように。セーブは Steam クラウドにも上がる)
/// </summary>
internal static class CharacterRegistry
{
    public static List<CharacterDef> Defs = new();
    public static readonly Dictionary<long, MotionManager> Registered = new();
    private static IntPtr _container, _save;
    private static float _next;

    public static CharacterDef Find(double id)
    {
        long key = (long)Math.Round(id);
        return Defs.FirstOrDefault(d => d.Id == key);
    }

    public static bool IsCustom(double id) => Find(id) != null;

    /// <summary>
    /// 毎フレーム: キャラの一覧が新しくなっていたら足し直す (選択画面などが一覧を使う前に間に合うよう毎フレーム。確かめるのは一覧が前と同じかだけ)。
    /// セーブは 1 秒ごと。ゲームの関数 (GetCharacterContainer / GetGameSave) には割り込まない: ゲームは別のスレッドからも呼んでいて、
    /// そこで Mod の処理が動くとゲームごと落ちる (拠点の「キャラクター」メニューやタイトルで落ちていた)
    /// </summary>
    public static void Tick()
    {
        if (Defs.Count == 0) return;
        try
        {
            var util = GameUtil.Instance;
            if (util == null) return;
            EnsureContainer(util.GetCharacterContainer());
            EnsureResourceTags();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            EnsureSave(GameUtil.GetGameSave());
        }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: 準備に失敗: {e.Message}"); }
    }

    private static bool _busy;

    /// <summary>
    /// ステージを読み込むとき、ゲームは「そのキャラのために先に読んでおくデータ」(リソースのタグ Character_〈ID〉) を
    /// ResourceManager.CharacterIdToTag (キャラの ID → タグ) で引く。新しいキャラの ID が無いと KeyNotFoundException で
    /// 読み込みが止まり、無限ロードになる (ホムラがリーダーで戦闘へ)。土台のキャラのタグを入れておく
    /// </summary>
    private static void EnsureResourceTags()
    {
        var map = ResourceManager.CharacterIdToTag;
        if (map == null) return;
        foreach (var def in Defs)
        {
            if (map.ContainsKey(def.Id) || !map.ContainsKey(def.Base)) continue;
            map[def.Id] = map[def.Base];
            CharacterMod.Ctx?.Log.Info($"Character: {def.Key} の読み込みのタグを土台 ({def.Base}) と同じにしました");
        }
    }

    /// <summary>キャラの一覧に新しいキャラが無ければ足す (ゲームが一覧を取り出した瞬間にも呼ぶ)</summary>
    public static void EnsureContainer(CharacterContainer container)
    {
        if (_busy || Defs.Count == 0 || container?.characters == null) return;
        if (container.Pointer == _container && Registered.Count == Defs.Count && Registered.Values.All(m => m != null)) return;
        _busy = true;
        try
        {
            if (container.Pointer != _container || Defs.Any(d => !Contains(container, d.Id)))
            {
                _container = container.Pointer;
                Register(container);
            }
        }
        finally { _busy = false; }
    }

    /// <summary>セーブに新しいキャラの解放などが無ければ足す (1 秒ごとと、新しいキャラの準備の直前)</summary>
    public static void EnsureSave(GameSave save)
    {
        if (_busy || Defs.Count == 0 || save == null || _stripping) return;
        _busy = true;
        try
        {
            if (save.characterUnlock != null && (save.Pointer != _save || Defs.Any(d => !save.characterUnlock.ToArray().Any(u => u != null && (long)Math.Round(u.id) == d.Id)
                    || (save.playerEquips != null && !save.playerEquips.ToArray().Any(e => e != null && (long)Math.Round(e.id) == d.Id)))))
            {
                _save = save.Pointer;
                Inject(save);
            }
        }
        finally { _busy = false; }
    }

    private static bool Contains(CharacterContainer c, long id)
    {
        foreach (var m in c.characters)
            if (m != null && (long)Math.Round(m.id) == id) return true;
        return false;
    }

    private static MotionManager Get(CharacterContainer c, long id)
    {
        foreach (var m in c.characters)
            if (m != null && (long)Math.Round(m.id) == id) return m;
        return null;
    }

    private static void Register(CharacterContainer container)
    {
        foreach (var def in Defs)
        {
            if (Contains(container, def.Id)) continue;
            var baseMm = Get(container, def.Base);
            if (baseMm == null)
            {
                CharacterMod.Ctx?.Log.Warning($"Character: {def.Key}: 土台のキャラ {def.Base} がありません");
                continue;
            }
            // ScriptableObject を丸ごと複製する (動作の一覧なども複製される)
            var clone = Object.Instantiate(baseMm.Cast<Object>()).Cast<MotionManager>();
            clone.id = def.Id;
            // 内部の名前も変える (土台と同じだと、名前から ID を引くところで土台のキャラに戻ってしまう)。画面に出る名前は ActorName_〈ID〉
            clone.name = def.Key;
            clone.isLock = false;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            container.characters.Add(clone);
            Registered[def.Id] = clone;
            CharacterMod.Ctx?.Log.Info($"Character: {def.Key} (ID {def.Id}) を足しました (土台 {def.Base}、動作 {clone.motions?.Count})");
            WriteMotionList(def, clone);
        }
    }

    /// <summary>
    /// 動作の一覧を 〈キャラのフォルダ〉/motions.txt に書き出す (Custom Motion で差し替えるときの名前を調べる用)。
    /// 名前・クリップ・長さ (秒)・攻撃判定が出る位置 (0〜1、AttackBoxOn)・ループするか
    /// </summary>
    private static void WriteMotionList(CharacterDef def, MotionManager mm)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {def.Key} (ID {def.Id}, 土台 {def.Base}) の動作。Custom Motion の割り当ては {def.Id}/〈名前〉=〈glb〉#〈アニメーション〉");
            sb.AppendLine("# 名前	クリップ	秒	攻撃判定	ループ");
            foreach (var m in mm.motions)
            {
                if (m == null) continue;
                var clip = m.bindAnimClip?.Clip;
                float sec = clip != null ? clip.length / Mathf.Max(0.05f, m.playSpeed) : 0f;
                var hits = new List<float>();
                if (m.animEvent != null)
                    foreach (var e in m.animEvent)
                        if (e?.callBack != null)
                            foreach (var f in e.callBack)
                                if (f?.funcName != null && f.funcName.EndsWith("AttackBoxOn")) hits.Add(e.playProcess);
                sb.AppendLine($"{m.name}	{(clip != null ? clip.name : "-")}	{sec:0.00}	{(hits.Count > 0 ? string.Join(",", hits.OrderBy(h => h).Select(h => h.ToString("0.00"))) : "-")}	{(clip != null && clip.isLooping ? "loop" : "")}");
            }
            File.WriteAllText(Path.Combine(def.Folder, "motions.txt"), sb.ToString());
        }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: 動作の一覧を書き出せません: {e.Message}"); }
    }

    /// <summary>セーブに解放とスキルを足す (メモリの中だけ。保存のときは抜く)</summary>
    private static void Inject(GameSave save)
    {
        foreach (var def in Defs)
        {
            if (!save.characterUnlock.ToArray().Any(u => u != null && (long)Math.Round(u.id) == def.Id))
                save.characterUnlock.Add(new CharacterUnlock { id = def.Id, notifyBefore = true });
            // 装備: 土台のキャラの装備をそのまま見せる (キャラの準備は、装備の武器を持たせるところから始まる。
            // 装備が無いと武器が見つからず「複製する元が null」になって、動作の準備まで届かない)
            if (save.playerEquips != null && !save.playerEquips.ToArray().Any(e => e != null && (long)Math.Round(e.id) == def.Id))
            {
                var baseEquip = save.playerEquips.ToArray().FirstOrDefault(e => e != null && (long)Math.Round(e.id) == def.Base);
                var equips = new Il2CppSystem.Collections.Generic.List<string>();
                if (baseEquip?.equips != null)
                    foreach (var uid in baseEquip.equips) equips.Add(uid);
                save.playerEquips.Add(new PlayerEquip { id = def.Id, equips = equips });
            }
            if (save.skills != null && !save.skills.ToArray().Any(s => s != null && (long)Math.Round(s.playerId) == def.Id))
                foreach (var s in save.skills.ToArray())
                    if (s != null && (long)Math.Round(s.playerId) == def.Base)
                        save.skills.Add(new PlayerSkill { playerId = def.Id, pattern = s.pattern, level = s.level });
        }
    }

    // ---- 保存の瞬間だけ抜く

    internal sealed class Stripped
    {
        public readonly List<CharacterUnlock> Unlocks = new();
        public readonly List<PlayerSkill> Skills = new();
        public readonly List<PlayerEquip> Equips = new();
        public double? LastCrtId;
    }

    private static bool _stripping;

    public static Stripped Strip()
    {
        if (Defs.Count == 0) return null;
        _stripping = true;
        var save = GameUtil.GetGameSave();
        if (save == null) { _stripping = false; return null; }
        var st = new Stripped();
        if (save.characterUnlock != null)
            foreach (var u in save.characterUnlock.ToArray())
                if (u != null && IsCustom(u.id)) { st.Unlocks.Add(u); save.characterUnlock.Remove(u); }
        if (save.skills != null)
            foreach (var s in save.skills.ToArray())
                if (s != null && IsCustom(s.playerId)) { st.Skills.Add(s); save.skills.Remove(s); }
        if (save.playerEquips != null)
            foreach (var e in save.playerEquips.ToArray())
                if (e != null && IsCustom(e.id)) { st.Equips.Add(e); save.playerEquips.Remove(e); }
        if (Find(save.lastCrtId) is CharacterDef def)
        {
            st.LastCrtId = save.lastCrtId;
            save.lastCrtId = def.Base;
        }
        int n = st.Unlocks.Count + st.Skills.Count + st.Equips.Count + (st.LastCrtId != null ? 1 : 0);
        if (n > 0) CharacterMod.Ctx?.Log.Info($"Character: 保存の前に新しいキャラの分を抜きました (解放 {st.Unlocks.Count}、スキル {st.Skills.Count}、装備 {st.Equips.Count}、最後のキャラ {(st.LastCrtId != null ? "あり" : "なし")})");
        return st;
    }

    public static void Restore(Stripped st)
    {
        try
        {
            if (st == null) return;
            var save = GameUtil.GetGameSave();
            if (save == null) return;
            foreach (var u in st.Unlocks) save.characterUnlock.Add(u);
            foreach (var s in st.Skills) save.skills.Add(s);
            foreach (var e in st.Equips) save.playerEquips.Add(e);
            if (st.LastCrtId is double id) save.lastCrtId = id;
        }
        finally { _stripping = false; } // 戻し終わるまでは足し直さない (二重に入らないように)
    }

    // ---- 名前

    public static string Language()
    {
        try
        {
            var lang = LocalizationManager.Instance?.GetCurLanguage() ?? SystemLanguage.Japanese;
            return lang switch
            {
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Chinese or SystemLanguage.ChineseSimplified or SystemLanguage.ChineseTraditional => "zh",
                _ => "en",
            };
        }
        catch { return "ja"; }
    }
}

[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.SaveGame))]
internal static class SaveGamePatch
{
    private static void Prefix(out CharacterRegistry.Stripped __state) => __state = CharacterRegistry.Strip();

    private static Exception Finalizer(Exception __exception, CharacterRegistry.Stripped __state)
    {
        CharacterRegistry.Restore(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.GameSaveBackUp))]
internal static class SaveBackupPatch
{
    private static void Prefix(out CharacterRegistry.Stripped __state) => __state = CharacterRegistry.Strip();

    private static Exception Finalizer(Exception __exception, CharacterRegistry.Stripped __state)
    {
        CharacterRegistry.Restore(__state);
        return __exception;
    }
}

/// <summary>
/// 新しいキャラの文: ActorName_〈ID〉 は定義の名前。ほかの 〈ID〉 の付いた文 (説明など) は、無ければ土台のキャラの文を使う
/// </summary>
[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.GetLocale))]
internal static class LocalePatch
{
    private static void Postfix(string key, ref string __result)
    {
        if (key == null || CharacterRegistry.Defs.Count == 0 || key.IndexOf('_') < 0) return;
        foreach (var def in CharacterRegistry.Defs)
        {
            var id = def.Id.ToString();
            if (!key.EndsWith("_" + id) && !key.Contains("_" + id + "_")) continue;
            if (key == "ActorName_" + id)
            {
                __result = def.NameFor(CharacterRegistry.Language());
                return;
            }
            if (string.IsNullOrEmpty(__result) || __result == key)
                __result = GameUtil.GetLocale(key.Replace(id, def.Base.ToString()));
            return;
        }
    }
}

/// <summary>
/// キャラの準備 (PlayerController.SetData) は、キャラの定義をキャラの一覧ではなく、ID の付いたパスで
/// ゲームのデータ (Resources.Load("〜" + ID)) から読み込んで複製する。新しいキャラのデータはゲームに無いので、
/// パスの最後が新しいキャラの ID で何も見つからないときは、こちらで作った定義を返す
/// </summary>
[HarmonyPatch]
internal static class ResourcesLoadPatch
{
    private static readonly HashSet<string> Logged = new();

    // Load(string) と Load(string, Type) の両方 (ゲームはどちらを直接呼ぶか分からない)。型を指定する版 Load<T> は除く
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        typeof(Resources).GetMethods().Where(m => m.Name == nameof(Resources.Load) && !m.IsGenericMethod
            && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(string));

    private static void Postfix(string path, object[] __args, ref Object __result)
    {
        if (__result != null || path == null || CharacterRegistry.Registered.Count == 0) return;
        foreach (var (id, mm) in CharacterRegistry.Registered)
        {
            var ids = id.ToString();
            if (!path.EndsWith("_" + ids)) continue;
            var def = CharacterRegistry.Find(id);
            if (path.EndsWith("MotionList_" + ids))
            {
                // キャラの定義 (動作の一覧): こちらで作ったもの
                if (mm == null) return;
                __result = mm;
            }
            else if (def != null)
            {
                // ほかのキャラごとのデータ (BuffList_〈ID〉 = キャラのバフ、RoleChoose の絵など): 土台のキャラのもの。
                // 型を指定して読んでいるときは同じ型で (Sprite で読むところに Texture2D を渡すと、画面を作る処理が止まる)
                var basePath = path.Substring(0, path.Length - ids.Length) + def.Base;
                var type = __args.Length > 1 ? __args[1] as Il2CppSystem.Type : null;
                __result = type != null ? Resources.Load(basePath, type) : Resources.Load(basePath);
                // 絵: 土台の絵をお手本として書き出し、キャラのフォルダの images/〈種類〉.png があればそれを使う
                int slash = path.LastIndexOf('/');
                var kind = path.Substring(slash + 1, path.Length - slash - 1 - ids.Length - 1);
                CharacterImages.ExportTemplate(def, kind, __result);
                var custom = CharacterImages.Load(def, kind, type, __result);
                if (custom != null) __result = custom;
            }
            if (Logged.Add(path)) CharacterMod.Ctx?.Log.Info($"Character: ゲームのデータ '{path}' の代わりに {(__result == null ? "(見つからない)" : __result.name)} を渡しました");
            return;
        }
    }
}

/// <summary>
/// キャラの ID は PlayerController.m_id で、体のモデル (土台のキャラのプレハブ) に土台の ID が書き込まれている。
/// キャラの準備 (SetData) は ID を変えないので、次のフレームの Start が m_id (土台の ID) で準備し直して土台のキャラに戻ってしまう。
/// 新しいキャラの ID で準備するときは、m_id も新しい ID にする
/// </summary>
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.SetData))]
internal static class SetDataPatch
{
    private static void Prefix(PlayerController __instance, double id)
    {
        // 準備の直前に、セーブに新しいキャラの装備・スキルがあることを確かめる (準備はそれを使う)
        if (CharacterRegistry.IsCustom(id)) CharacterRegistry.EnsureSave(GameUtil.GetGameSave());
        // 新しいキャラにするとき・新しいキャラから元のキャラに戻すとき (同じプレハブの体が使い回される)
        if ((CharacterRegistry.IsCustom(id) || CharacterRegistry.IsCustom(__instance.m_id)) && Math.Abs(__instance.m_id - id) > 0.5)
            __instance.m_id = id;
    }
}

/// <summary>新しいキャラはいつでも解放済み (選択画面などは、作った瞬間の解放の状態で並べるので、セーブに足すのが間に合わないことがある)</summary>
[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.IsCharacterUnlock))]
internal static class UnlockPatch
{
    private static void Postfix(double id, ref bool __result)
    {
        if (!__result && CharacterRegistry.IsCustom(id)) __result = true;
    }
}


