using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RusK.Mods.Shared;

/// <summary>
/// キャラの Mod Pack (RusK\characters\〈フォルダ〉\character.json)。フォルダを置くだけで、見た目・武器・動き・声まで付くように、
/// 各 Mod がここから自分の割り当てを読む (手で書いた割り当てがあれば、そちらが優先)。
/// <code>
/// {
///   "id": 9001, "base": 1006, "name": {"ja": "名前", "en": "NAME", "zh": "名字"},
///   "model": "model/chara.pmx",
///   "weapon": {"equip": 1041, "file": "weapon/sword.pmx", "position": [0,0,0], "rotation": [0,180,0], "scale": 1.0,
///              "glow": false, "glowColor": 0, "glowStrength": 2},
///   "motions": {"RedLightCombo0": "motions/slash.glb#Slash@0.30"},
///   "voices": "voices"
/// }
/// </code>
/// ファイルの場所は、キャラのフォルダから見た場所。motions の値は「ファイル#アニメーションの名前@当たる瞬間の秒」(# と @ は省略可)
/// </summary>
internal sealed class CharacterPack
{
    public string Folder;
    public long Id;
    public long Base;
    public string Model;          // 絶対パス (無ければ null)
    public long WeaponEquip = -1;
    public string WeaponFile;     // 絶対パス
    public float[] WeaponPosition = { 0, 0, 0 };
    public float[] WeaponRotation = { 0, 0, 0 };
    public float WeaponScale = 1f;
    public bool WeaponGlow;
    public int WeaponGlowColor;
    public float WeaponGlowStrength = 2f;
    /// <summary>動作の名前 → (絶対パス, アニメーションの名前 or null, 当たる瞬間の秒 or null)</summary>
    public readonly Dictionary<string, (string file, string anim, float? hit)> Motions = new();
    public string Voices;         // 絶対パス (フォルダ)

    public string Path(string rel) => rel == null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(Folder, rel));
}

internal static class CharacterPacks
{
    /// <summary>RusK\data\〈mod〉 (Mod のデータのフォルダ) から見た RusK\characters</summary>
    public static string FolderFrom(string dataDirectory) =>
        Path.GetFullPath(Path.Combine(dataDirectory, "..", "..", "characters"));

    public static List<CharacterPack> Load(string dataDirectory, Action<string> warn = null) =>
        FromFolder(FolderFrom(dataDirectory), warn);

    public static List<CharacterPack> FromFolder(string dir, Action<string> warn = null)
    {
        var result = new List<CharacterPack>();
        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.GetFiles(dir, "character.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var r = doc.RootElement;
                var p = new CharacterPack { Folder = Path.GetDirectoryName(file)! };
                p.Id = r.GetProperty("id").GetInt64();
                if (r.TryGetProperty("base", out var b)) p.Base = b.GetInt64();
                if (r.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) p.Model = p.Path(m.GetString());
                if (r.TryGetProperty("weapon", out var w) && w.ValueKind == JsonValueKind.Object)
                {
                    if (w.TryGetProperty("equip", out var e)) p.WeaponEquip = e.GetInt64();
                    if (w.TryGetProperty("file", out var f)) p.WeaponFile = p.Path(f.GetString());
                    if (w.TryGetProperty("position", out var pos)) p.WeaponPosition = Floats(pos, p.WeaponPosition);
                    if (w.TryGetProperty("rotation", out var rot)) p.WeaponRotation = Floats(rot, p.WeaponRotation);
                    if (w.TryGetProperty("scale", out var sc)) p.WeaponScale = sc.GetSingle();
                    if (w.TryGetProperty("glow", out var g)) p.WeaponGlow = g.GetBoolean();
                    if (w.TryGetProperty("glowColor", out var gc)) p.WeaponGlowColor = gc.GetInt32();
                    if (w.TryGetProperty("glowStrength", out var gs)) p.WeaponGlowStrength = gs.GetSingle();
                }
                if (r.TryGetProperty("motions", out var ms) && ms.ValueKind == JsonValueKind.Object)
                    foreach (var kv in ms.EnumerateObject())
                    {
                        var v = kv.Value.GetString();
                        if (string.IsNullOrEmpty(v)) continue;
                        float? hit = null;
                        int at = v.LastIndexOf('@');
                        if (at > 0 && float.TryParse(v.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
                        { hit = h; v = v.Substring(0, at); }
                        string anim = null;
                        int hash = v.IndexOf('#');
                        if (hash > 0) { anim = v.Substring(hash + 1); v = v.Substring(0, hash); }
                        p.Motions[kv.Name] = (p.Path(v), anim, hit);
                    }
                if (r.TryGetProperty("voices", out var vo) && vo.ValueKind == JsonValueKind.String) p.Voices = p.Path(vo.GetString());
                else if (Directory.Exists(Path.Combine(p.Folder, "voices"))) p.Voices = Path.Combine(p.Folder, "voices");
                result.Add(p);
            }
            catch (Exception e) { warn?.Invoke($"{file} を読めません: {e.Message}"); }
        }
        return result;
    }

    private static float[] Floats(JsonElement a, float[] fallback)
    {
        if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 3) return fallback;
        return new[] { a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle() };
    }
}
