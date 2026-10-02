using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RusK.API;

namespace RusK.Mods.Character;

/// <summary>
/// Custom Character: 新しいキャラ枠を足す。RusK/characters/〈フォルダ〉/character.json に定義を置く。
/// 動作・能力値は土台のゲームのキャラを複製する。見た目は Custom VRM Loader、動きは Custom Motion、声は Voice Replacer で付ける
/// </summary>
[RuskMod("character", "Custom Character", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1878",
    Description = "新しいキャラ枠を足す (RusK/characters の character.json。動作・能力は土台のキャラを複製する)")]
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

    public CharacterModule() : base("CustomCharacter", "Party", "新しいキャラ枠 (RusK/characters)")
    {
        AddSetting(new ButtonSetting("SwitchTo", SwitchTo, "新しいキャラに切り替える (押すたびに次のキャラ)"));
        AddSetting(new ButtonSetting("OpenFolder", OpenFolder, "characters フォルダを開く"));
        AddSetting(new ButtonSetting("Capture", () => { var p = RusK.Mods.Shared.PlayerRef.Current; if (p != null) CharacterCapture.Request((long)Math.Round(p.GetPlayerId())); },
            "今のキャラ (新しいキャラ) の絵の素材を撮り直す (captures フォルダ)"));
        Enabled = true;
    }

    public override void OnUpdate()
    {
        CharacterRegistry.Tick();
        try { CharacterCapture.Tick(); } catch { }
        try { DevCommands.Tick(); } catch { }
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
