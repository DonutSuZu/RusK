using System;
using System.Collections.Generic;
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

    /// <summary>毎フレーム: キャラの一覧とセーブが新しくなっていたら足し直す (1 秒ごとに確かめる)</summary>
    public static void Tick()
    {
        if (Defs.Count == 0 || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        try
        {
            var util = GameUtil.Instance;
            if (util == null) return;
            var container = util.GetCharacterContainer();
            if (container?.characters != null && (container.Pointer != _container || Defs.Any(d => !Contains(container, d.Id))))
            {
                _container = container.Pointer;
                Register(container);
            }
            var save = GameUtil.GetGameSave();
            if (save?.characterUnlock != null && (save.Pointer != _save || Defs.Any(d => !save.characterUnlock.ToArray().Any(u => u != null && (long)Math.Round(u.id) == d.Id))))
            {
                _save = save.Pointer;
                Inject(save);
            }
        }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: 準備に失敗: {e.Message}"); }
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
            clone.name = def.Key;
            clone.isLock = false;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            container.characters.Add(clone);
            Registered[def.Id] = clone;
            CharacterMod.Ctx?.Log.Info($"Character: {def.Key} (ID {def.Id}) を足しました (土台 {def.Base}、動作 {clone.motions?.Count})");
        }
    }

    /// <summary>セーブに解放とスキルを足す (メモリの中だけ。保存のときは抜く)</summary>
    private static void Inject(GameSave save)
    {
        foreach (var def in Defs)
        {
            if (!save.characterUnlock.ToArray().Any(u => u != null && (long)Math.Round(u.id) == def.Id))
                save.characterUnlock.Add(new CharacterUnlock { id = def.Id, notifyBefore = true });
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

    public static Stripped Strip()
    {
        if (Defs.Count == 0) return null;
        var save = GameUtil.GetGameSave();
        if (save == null) return null;
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
        if (st == null) return;
        var save = GameUtil.GetGameSave();
        if (save == null) return;
        foreach (var u in st.Unlocks) save.characterUnlock.Add(u);
        foreach (var s in st.Skills) save.skills.Add(s);
        foreach (var e in st.Equips) save.playerEquips.Add(e);
        if (st.LastCrtId is double id) save.lastCrtId = id;
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
