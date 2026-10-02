using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RusK.Mods.Character;

/// <summary>
/// 新しいキャラの定義 (RusK/characters/〈フォルダ〉/character.json)。
/// <code>{ "id": 9001, "base": 1006, "name": { "ja": "ホムラ", "en": "Pyra", "zh": "焰" } }</code>
/// - id: 新しいキャラの ID (ゲームのキャラと重ならない数。9000 番台を推奨)
/// - base: 土台にするゲームのキャラの ID。動作 (攻撃・回避など)・能力値・スキルを複製する
/// - name: 表示名 (言語ごと。無い言語は en → ja の順で代わりに使う)
/// </summary>
internal sealed class CharacterDef
{
    public long Id { get; set; }
    public long Base { get; set; }
    public Dictionary<string, string> Name { get; set; } = new();

    /// <summary>フォルダの名前 (定義の見分け用)</summary>
    public string Key { get; set; }
    public string Folder { get; set; }

    public string NameFor(string lang)
    {
        if (Name.TryGetValue(lang, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
        if (Name.TryGetValue("en", out n) && !string.IsNullOrWhiteSpace(n)) return n;
        if (Name.TryGetValue("ja", out n) && !string.IsNullOrWhiteSpace(n)) return n;
        return Key;
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>フォルダの character.json を全部読む (ID が重なるもの・ゲームのキャラの ID のものは除く)</summary>
    public static List<CharacterDef> LoadAll(string root, Action<string> warn)
    {
        var list = new List<CharacterDef>();
        try
        {
            Directory.CreateDirectory(root);
            foreach (var file in Directory.GetFiles(root, "character.json", SearchOption.AllDirectories).OrderBy(f => f))
            {
                try
                {
                    var def = JsonSerializer.Deserialize<CharacterDef>(File.ReadAllText(file), Options);
                    if (def == null || def.Id <= 0 || def.Base <= 0) { warn($"{file}: id と base が必要です"); continue; }
                    if (def.Id < 9000) { warn($"{file}: id はゲームのキャラと重ならないよう 9000 以上にしてください ({def.Id})"); continue; }
                    if (list.Any(d => d.Id == def.Id)) { warn($"{file}: id {def.Id} が重なっています"); continue; }
                    def.Folder = Path.GetDirectoryName(file);
                    def.Key = Path.GetFileName(def.Folder);
                    list.Add(def);
                }
                catch (Exception e) { warn($"{file}: 読めません ({e.Message})"); }
            }
        }
        catch (Exception e) { warn($"characters フォルダを読めません: {e.Message}"); }
        return list;
    }
}
