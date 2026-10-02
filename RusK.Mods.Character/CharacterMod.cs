using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RusK.API;

namespace RusK.Mods.Character;

/// <summary>
/// Custom Character (試作): 新しいキャラ枠を足す。RusK/characters/〈フォルダ〉/character.json に定義を置く。
/// 動作・能力値は土台のゲームのキャラを複製する。見た目は Custom VRM Loader、動きは Custom Motion、声は Voice Replacer で付ける
/// </summary>
[RuskMod("character", "Custom Character", "0.1.0",
    Author = "you",
    GameVersion = "0.0.1878",
    Description = "新しいキャラ枠を足す (RusK/characters の character.json。土台のキャラの動作を複製する) (試作)")]
public sealed class CharacterMod : RuskMod
{
    internal static IModContext Ctx;

    /// <summary>キャラの定義を置くフォルダ (RusK\characters)</summary>
    internal static string Folder =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Ctx.DataDirectory)!, "..", "characters"));

    protected override void OnLoad()
    {
        Ctx = Context;
        CharacterRegistry.Defs = CharacterDef.LoadAll(Folder, s => Context.Log.Warning("Character: " + s));
        Context.Log.Info($"Character: 定義 {CharacterRegistry.Defs.Count} 個 ({string.Join(", ", CharacterRegistry.Defs.Select(d => $"{d.Key}={d.Id}"))})");
        Context.Harmony.PatchAll(typeof(SaveGamePatch));
        Context.Harmony.PatchAll(typeof(SaveBackupPatch));
        Context.Harmony.PatchAll(typeof(LocalePatch));
        Context.Harmony.PatchAll(typeof(ResourcesLoadPatch));
        Context.Harmony.PatchAll(typeof(SetDataPatch));
        Context.Harmony.PatchAll(typeof(UnlockPatch));
        Context.Harmony.PatchAll(typeof(CharacterShowPatch));
        Context.RegisterModule(new CharacterModule());
    }
}

internal sealed class CharacterModule : Module
{
    private int _next;

    public CharacterModule() : base("CustomCharacter", "Party", "新しいキャラ枠 (試作)")
    {
        AddSetting(new ButtonSetting("SwitchTo", SwitchTo, "新しいキャラに切り替える (押すたびに次のキャラ)"));
        AddSetting(new ButtonSetting("OpenFolder", OpenFolder, "characters フォルダを開く"));
        AddSetting(new ButtonSetting("DumpChoose", DumpChoose, "(調査) 開いているキャラ選択の画面をログに書き出す"));
        Enabled = true;
    }

    private float _nextDump;
    private IntPtr _dumped;

    public override void OnUpdate()
    {
        CharacterRegistry.Tick();
        // (調査) キャラ選択の画面が開いたら、一度だけ自動で書き出す
        if (UnityEngine.Time.unscaledTime < _nextDump) return;
        _nextDump = UnityEngine.Time.unscaledTime + 1f;
        try
        {
            var w = UnityEngine.Object.FindObjectOfType<WindowBattleCharacterChoose>();
            if (w != null && w.Pointer != _dumped && w.m_crtCards != null && w.m_crtCards.Count > 0)
            {
                _dumped = w.Pointer;
                DumpChoose();
            }
        }
        catch { }
    }

    private void SwitchTo()
    {
        var defs = CharacterRegistry.Defs;
        if (defs.Count == 0) return;
        var def = defs[_next++ % defs.Count];
        try
        {
            GameUtil.Instance.ChangeCharacter(def.Id);
            CharacterMod.Ctx.Log.Info($"Character: {def.Key} に切り替え");
        }
        catch (Exception e) { CharacterMod.Ctx.Log.Warning($"Character: 切り替えに失敗: {e}"); }
    }

    /// <summary>調査: 開いているキャラ選択の画面 (WindowBattleCharacterChoose) のカード・解放済みのキャラ・並び順</summary>
    private static void DumpChoose()
    {
        var log = CharacterMod.Ctx.Log;
        try
        {
            var w = UnityEngine.Object.FindObjectOfType<WindowBattleCharacterChoose>();
            if (w == null) { log.Info("Character: (調査) キャラ選択の画面が開いていません"); return; }
            var sb = new System.Text.StringBuilder($"Character: (調査) 選択画面 cards={w.m_crtCards?.Count} unlock={w.m_unlockCrts?.Count} order=[{(w.m_unlockIndexOrder == null ? "" : string.Join(",", w.m_unlockIndexOrder.ToArray()))}] cur={w.m_curCardIdx}");
            if (w.m_unlockCrts != null)
                for (int i = 0; i < w.m_unlockCrts.Count; i++) sb.Append($"\n  unlock[{i}] = {w.m_unlockCrts[i]?.id} {w.m_unlockCrts[i]?.name}");
            if (w.m_crtCards != null)
                for (int i = 0; i < w.m_crtCards.Count; i++)
                {
                    var c = w.m_crtCards[i];
                    if (c == null) { sb.Append($"\n  card[{i}] null"); continue; }
                    sb.Append($"\n  card[{i}] {c.name} active={c.gameObject.activeSelf} interactable={c.isInteractable} children=[{string.Join(",", Enumerable.Range(0, c.transform.childCount).Select(k => c.transform.GetChild(k).name))}]");
                }
            if (w.m_cardRoot != null)
                sb.Append($"\n  cardRoot children=[{string.Join(",", Enumerable.Range(0, w.m_cardRoot.childCount).Select(k => w.m_cardRoot.GetChild(k).name + (w.m_cardRoot.GetChild(k).gameObject.activeSelf ? "" : "(off)")))}]");
            log.Info(sb.ToString());
        }
        catch (Exception e) { log.Warning($"Character: (調査) 失敗: {e}"); }
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(CharacterMod.Folder);
            Process.Start("explorer.exe", $"\"{CharacterMod.Folder}\"");
        }
        catch (Exception e) { CharacterMod.Ctx.Log.Warning($"Character: フォルダを開けません: {e.Message}"); }
    }
}
