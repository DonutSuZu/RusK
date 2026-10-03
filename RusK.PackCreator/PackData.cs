using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace RusK.PackCreator;

/// <summary>土台にできるゲームのキャラ (packdata.json: 動作と声の名前の一覧)</summary>
internal sealed class BaseChara
{
    public long Id;
    public string[] Names;        // ja, en (ゲームの中の名前), zh
    public long? Weapon;          // 武器の装備の番号 (分かっているものだけ)
    public readonly List<(string name, double sec, double? hit, bool loop)> Motions = new();
    public readonly List<string> Voices = new();   // 日本語の声の名前 (_JP)

    public string Name(string lang) => Names[lang == "en" ? 1 : lang == "zh" ? 2 : 0];
    public override string ToString() => $"{Id}  {Name(Strings.Current)}";

    public static List<BaseChara> All { get; } = Load();
    public static string GameVersion { get; private set; }

    private static List<BaseChara> Load()
    {
        var list = new List<BaseChara>();
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("packdata.json");
        using var r = new StreamReader(s, Encoding.UTF8);
        var root = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(r.ReadToEnd());
        GameVersion = root["gameVersion"] as string;
        foreach (var kv in (Dictionary<string, object>)root["bases"])
        {
            var d = (Dictionary<string, object>)kv.Value;
            var b = new BaseChara { Id = long.Parse(kv.Key), Names = ((object[])d["name"]).Select(o => (string)o).ToArray() };
            if (d.TryGetValue("weapon", out var w) && w != null) b.Weapon = Convert.ToInt64(w);
            foreach (Dictionary<string, object> m in (object[])d["motions"])
                b.Motions.Add(((string)m["name"], Convert.ToDouble(m["sec"]), m["hit"] == null ? null : Convert.ToDouble(m["hit"]), (bool)m["loop"]));
            foreach (var v in (object[])d["voices"]) b.Voices.Add((string)v);
            list.Add(b);
        }
        return list.OrderBy(b => b.Id).ToList();
    }
}

/// <summary>動きの割り当て 1 行</summary>
internal sealed class MotionRow
{
    public string Action;
    public string File;     // 絶対パス
    public string Anim;     // 無ければ glb の最初のアニメーション
    public double? Hit;     // 当たる瞬間 (秒)
}

/// <summary>作っている Mod Pack (character.json と、コピーする元のファイル)</summary>
internal sealed class Pack
{
    public string Key = "MyChara";
    public long Id = 9001;
    public long Base = 1006;
    public string NameJa = "", NameEn = "", NameZh = "";
    public string Model;          // 絶対パス
    public string Weapon;         // 絶対パス
    public long WeaponEquip = 1041;
    public double[] WeaponPos = { 0, 0, 0 }, WeaponRot = { 0, 0, 0 };
    public double WeaponScale = 1;
    public readonly List<MotionRow> Motions = new();
    public string Voices;         // フォルダ
    public string Images;         // フォルダ

    public static readonly string[] ImageKinds =
    {
        "blackBar_n", "rolechoose", "character_s", "choose_n", "BGrole", "name_s", "NameBar", "roleName_s",
        "Profile", "leftFrame", "leftName", "leftNameMask", "buffResuiltProfile", "dialogBox", "character",
        "blackBar_h", "bg_s", "BGtext1", "BGtext2", "blackline_s", "btn_fight", "difficult_s", "skillLvColor", "SkillIconAttack", "SkillIconP",
    };

    private static readonly string[] AudioExt = { ".ogg", ".wav", ".mp3" };

    public static string CharactersDir(string game) => Path.Combine(game, "RusK", "characters");

    /// <summary>ほかの Pack が使っているキャラ番号 (自分のフォルダは除く)</summary>
    public static Dictionary<long, string> UsedIds(string game, string exceptFolder)
    {
        var used = new Dictionary<long, string>();
        var dir = CharactersDir(game);
        if (!Directory.Exists(dir)) return used;
        foreach (var f in Directory.GetFiles(dir, "character.json", SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(f);
            if (exceptFolder != null && string.Equals(Path.GetFullPath(folder), Path.GetFullPath(exceptFolder), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f));
                used[Convert.ToInt64(d["id"])] = Path.GetFileName(folder);
            }
            catch { }
        }
        return used;
    }

    public static long FreeId(string game)
    {
        var used = UsedIds(game, null);
        long id = 9001;
        while (used.ContainsKey(id)) id++;
        return id;
    }

    // ------------------------------------------------------------------ 開く

    public static Pack Open(string folder)
    {
        var json = File.ReadAllText(Path.Combine(folder, "character.json"));
        var d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
        string P(object rel) => rel is string s && s.Length > 0 ? Path.GetFullPath(Path.Combine(folder, s)) : null;
        var p = new Pack { Key = Path.GetFileName(folder), Id = Convert.ToInt64(d["id"]) };
        if (d.TryGetValue("base", out var b)) p.Base = Convert.ToInt64(b);
        if (d.TryGetValue("name", out var n) && n is Dictionary<string, object> names)
        {
            p.NameJa = names.TryGetValue("ja", out var ja) ? ja as string : "";
            p.NameEn = names.TryGetValue("en", out var en) ? en as string : "";
            p.NameZh = names.TryGetValue("zh", out var zh) ? zh as string : "";
        }
        if (d.TryGetValue("model", out var m)) p.Model = P(m);
        if (d.TryGetValue("weapon", out var w) && w is Dictionary<string, object> wd)
        {
            if (wd.TryGetValue("equip", out var e)) p.WeaponEquip = Convert.ToInt64(e);
            if (wd.TryGetValue("file", out var f)) p.Weapon = P(f);
            if (wd.TryGetValue("position", out var pos) && pos is object[] pa && pa.Length >= 3) p.WeaponPos = pa.Select(Convert.ToDouble).ToArray();
            if (wd.TryGetValue("rotation", out var rot) && rot is object[] ra && ra.Length >= 3) p.WeaponRot = ra.Select(Convert.ToDouble).ToArray();
            if (wd.TryGetValue("scale", out var sc)) p.WeaponScale = Convert.ToDouble(sc);
        }
        if (d.TryGetValue("motions", out var ms) && ms is Dictionary<string, object> md)
            foreach (var kv in md)
            {
                var v = kv.Value as string ?? "";
                double? hit = null;
                int at = v.LastIndexOf('@');
                if (at > 0 && double.TryParse(v.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) { hit = h; v = v.Substring(0, at); }
                string anim = null;
                int hash = v.IndexOf('#');
                if (hash > 0) { anim = v.Substring(hash + 1); v = v.Substring(0, hash); }
                p.Motions.Add(new MotionRow { Action = kv.Key, File = P(v), Anim = anim, Hit = hit });
            }
        if (d.TryGetValue("voices", out var vo)) p.Voices = P(vo);
        else if (Directory.Exists(Path.Combine(folder, "voices"))) p.Voices = Path.Combine(folder, "voices");
        if (Directory.Exists(Path.Combine(folder, "images"))) p.Images = Path.Combine(folder, "images");
        return p;
    }

    // ------------------------------------------------------------------ 調べる

    public List<string> Problems(string game)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(Key) || Key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) list.Add(Strings.T("フォルダ名に使えない文字があります"));
        if (Id < 9000) list.Add(Strings.T("キャラ番号は 9000 以上にしてください"));
        if (UsedIds(game, Path.Combine(CharactersDir(game), Key ?? "")).TryGetValue(Id, out var other))
            list.Add(Strings.T("キャラ番号 {0} は、ほかの Pack ({1}) が使っています", Id, other));
        if (string.IsNullOrWhiteSpace(NameJa) && string.IsNullOrWhiteSpace(NameEn)) list.Add(Strings.T("名前を入れてください"));
        if (Model != null && !File.Exists(Model)) list.Add(Strings.T("モデルのファイルがありません: {0}", Model));
        if (Weapon != null && !File.Exists(Weapon)) list.Add(Strings.T("武器のファイルがありません: {0}", Weapon));
        foreach (var m in Motions)
            if (m.File == null || !File.Exists(m.File)) list.Add(Strings.T("動き ({0}) のファイルがありません", m.Action));
        return list;
    }

    public static IEnumerable<string> AudioFiles(string folder) =>
        folder == null || !Directory.Exists(folder) ? Enumerable.Empty<string>()
            : Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).Where(f => AudioExt.Contains(Path.GetExtension(f).ToLowerInvariant()));

    /// <summary>声のファイルのうち、土台のキャラの声の名前に合うもの ("名前#2.ogg" の # から後ろは無視)</summary>
    public static (int matched, int total, List<string> missing) CheckVoices(string folder, BaseChara b)
    {
        var have = new HashSet<string>(AudioFiles(folder).Select(f =>
        {
            var n = Path.GetFileNameWithoutExtension(f);
            int h = n.IndexOf('#');
            return h > 0 ? n.Substring(0, h) : n;
        }), StringComparer.OrdinalIgnoreCase);
        var missing = b.Voices.Where(v => !have.Contains(v)).ToList();
        return (b.Voices.Count - missing.Count, b.Voices.Count, missing);
    }

    public static (int count, List<string> missing) CheckImages(string folder)
    {
        var have = folder != null && Directory.Exists(folder)
            ? new HashSet<string>(Directory.GetFiles(folder, "*.png").Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();
        var missing = ImageKinds.Where(k => !have.Contains(k)).ToList();
        return (ImageKinds.Length - missing.Count, missing);
    }

    // ------------------------------------------------------------------ 作る

    /// <summary>ゲームの RusK\characters\〈Key〉 に、ファイルをコピーして character.json を書く。書いたフォルダを返す</summary>
    public string Build(string game)
    {
        var dest = Path.Combine(CharactersDir(game), Key);
        Directory.CreateDirectory(dest);
        string model = null, weapon = null;
        if (Model != null) model = Place(Model, dest, "model", Model.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase));
        if (Weapon != null) weapon = Place(Weapon, dest, "weapon", Weapon.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase));
        var motions = new List<(string action, string value)>();
        foreach (var m in Motions)
        {
            var rel = Place(m.File, dest, "motions", false);
            var v = rel + (string.IsNullOrEmpty(m.Anim) ? "" : "#" + m.Anim) + (m.Hit is double h ? "@" + h.ToString("0.###", CultureInfo.InvariantCulture) : "");
            motions.Add((m.Action, v));
        }
        bool voices = false;
        if (Voices != null && Directory.Exists(Voices))
        {
            var to = Path.Combine(dest, "voices");
            foreach (var f in AudioFiles(Voices)) CopyInto(f, Path.Combine(to, Path.GetFileName(f)));
            voices = Directory.Exists(to);
        }
        if (Images != null && Directory.Exists(Images))
            foreach (var f in Directory.GetFiles(Images, "*.png")) CopyInto(f, Path.Combine(dest, "images", Path.GetFileName(f)));

        File.WriteAllText(Path.Combine(dest, "character.json"), Json(model, weapon, motions, voices), new UTF8Encoding(false));
        return dest;
    }

    /// <summary>
    /// ファイルを Pack の sub フォルダに置き、Pack から見た場所を返す。withFolder なら、ファイルのあるフォルダごと (PMX のテクスチャ) 置く。
    /// もう Pack の中にあるファイルはそのまま
    /// </summary>
    private static string Place(string file, string dest, string sub, bool withFolder)
    {
        var full = Path.GetFullPath(file);
        var destFull = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        if (full.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) return Rel(full.Substring(destFull.Length));
        var to = Path.Combine(dest, sub);
        if (withFolder)
        {
            var src = Path.GetDirectoryName(full);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                CopyInto(f, Path.Combine(to, f.Substring(src.Length).TrimStart('\\', '/')));
            return Rel(Path.Combine(sub, Path.GetFileName(full)));
        }
        CopyInto(full, Path.Combine(to, Path.GetFileName(full)));
        return Rel(Path.Combine(sub, Path.GetFileName(full)));
    }

    private static string Rel(string p) => p.Replace('\\', '/');

    private static void CopyInto(string from, string to)
    {
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        File.Copy(from, to, true);
    }

    private static string Str(string s) => new JavaScriptSerializer().Serialize(s ?? "");
    private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Vec(double[] v) => $"[{Num(v[0])}, {Num(v[1])}, {Num(v[2])}]";

    private string Json(string model, string weapon, List<(string action, string value)> motions, bool voices)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"id\": {Id},\n  \"base\": {Base},\n");
        sb.Append($"  \"name\": {{\"ja\": {Str(NameJa)}, \"en\": {Str(NameEn)}, \"zh\": {Str(NameZh)}}}");
        if (model != null) sb.Append($",\n  \"model\": {Str(model)}");
        if (weapon != null)
            sb.Append($",\n  \"weapon\": {{\"equip\": {WeaponEquip}, \"file\": {Str(weapon)}, \"position\": {Vec(WeaponPos)}, \"rotation\": {Vec(WeaponRot)}, \"scale\": {Num(WeaponScale)}}}");
        if (motions.Count > 0)
            sb.Append(",\n  \"motions\": {\n" + string.Join(",\n", motions.Select(m => $"    {Str(m.action)}: {Str(m.value)}")) + "\n  }");
        if (voices) sb.Append(",\n  \"voices\": \"voices\"");
        sb.Append("\n}\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ zip

    /// <summary>配ってはいけないもの (ゲームから書き出した物) を除いて zip にする。除いたものの名前を返す</summary>
    public static List<string> Zip(string packFolder, string zipFile)
    {
        var skipped = new List<string>();
        var name = Path.GetFileName(packFolder);
        if (File.Exists(zipFile)) File.Delete(zipFile);
        using var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create);
        foreach (var f in Directory.GetFiles(packFolder, "*", SearchOption.AllDirectories))
        {
            var rel = f.Substring(packFolder.Length).TrimStart('\\', '/');
            var top = rel.Split('\\', '/')[0];
            if (top.Equals("images_template", StringComparison.OrdinalIgnoreCase) || top.Equals("captures", StringComparison.OrdinalIgnoreCase)
                || rel.Equals("motions.txt", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith("rig_", StringComparison.OrdinalIgnoreCase))
            {
                if (!skipped.Contains(top)) skipped.Add(top);
                continue;
            }
            zip.CreateEntryFromFile(f, name + "/" + rel.Replace('\\', '/'), CompressionLevel.Optimal);
        }
        return skipped;
    }
}
