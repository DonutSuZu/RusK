using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    /// <summary>声: 要る声の名前 (土台のキャラの声の名前、_JP 付き) → 選んだファイル (絶対パス。名前は何でもよい)</summary>
    public readonly Dictionary<string, string> VoiceFiles = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>中国語の声の設定のための名前 (_JP なし) でも置く</summary>
    public bool VoiceChinese = true;
    /// <summary>絵: 絵の名前 (images_template と同じ) → 選んだファイル (絶対パス。名前は何でもよい)</summary>
    public readonly Dictionary<string, string> ImageFiles = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>縦横の比がお手本と違う絵に、透明の余白を足して比を合わせる (そのままだと伸びる)</summary>
    public bool ImageFit = true;

    public static readonly string[] ImageKinds =
    {
        "blackBar_n", "rolechoose", "character_s", "choose_n", "BGrole", "name_s", "NameBar", "roleName_s",
        "Profile", "leftFrame", "leftName", "leftNameMask", "buffResuiltProfile", "dialogBox", "character",
        "blackBar_h", "bg_s", "BGtext1", "BGtext2", "blackline_s", "btn_fight", "difficult_s", "skillLvColor", "SkillIconAttack", "SkillIconP",
    };

    /// <summary>絵の大きさ (お手本、土台が赤悠のとき)、中身、出る場所。飾りは大きさを決めていない</summary>
    public static readonly (string kind, int w, int h, string what, string where)[] ImageInfo =
    {
        ("blackBar_n", 204, 106, "顔と英語の名前のカード", "キャラ画面の一覧"),
        ("rolechoose", 210, 100, "顔のアイコン", "パーティ・選択"),
        ("character_s", 365, 1440, "立ち絵 (カラー)", "出撃前の選択 (選んだとき)"),
        ("choose_n", 365, 1440, "立ち絵 (灰色)", "出撃前の選択"),
        ("BGrole", 1200, 1440, "大きな背景の絵 (灰色・暗め)", "キャラ画面"),
        ("name_s", 449, 173, "名前 (自分の言語の文字)", "キャラ画面"),
        ("NameBar", 451, 67, "名前の帯 (英語)", "キャラ画面"),
        ("roleName_s", 206, 30, "英語の名前", "選択"),
        ("Profile", 888, 1440, "横顔", "リザルト画面"),
        ("leftFrame", 888, 1440, "横顔 + 背景の紙", "リザルト画面"),
        ("leftName", 888, 1440, "英語の名前を 8 段に重ねた文字", "リザルト画面"),
        ("leftNameMask", 1360, 1440, "横顔の形の切り抜き (黒)", "リザルト画面"),
        ("buffResuiltProfile", 1645, 1440, "暗いバストアップ", "バフの結果"),
        ("dialogBox", 256, 256, "会話の顔", "会話"),
        ("character", 158, 559, "小さな全身", "(画面による)"),
    };

    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp" };

    /// <summary>絵のお手本の大きさ。Pack に images_template があればその大きさ (土台のキャラの本当の大きさ)、無ければ上の表</summary>
    public static Size? ImageSize(string packDir, string kind)
    {
        try
        {
            var t = packDir == null ? null : Path.Combine(packDir, "images_template", kind + ".png");
            if (t != null && File.Exists(t)) using (var img = Image.FromFile(t)) return img.Size;
        }
        catch { }
        foreach (var i in ImageInfo) if (i.kind == kind) return new Size(i.w, i.h);
        return null;
    }

    /// <summary>縦横の比が 2% 以上違うか</summary>
    public static bool AspectDiffers(Size a, Size b) =>
        Math.Abs(a.Width / (double)a.Height - b.Width / (double)b.Height) > 0.02 * (b.Width / (double)b.Height);

    public static IEnumerable<string> ImageFilesIn(string folder) =>
        folder == null || !Directory.Exists(folder) ? Enumerable.Empty<string>()
            : Directory.GetFiles(folder).Where(f => ImageExt.Contains(Path.GetExtension(f).ToLowerInvariant()));

    /// <summary>フォルダの中の、絵の名前に合うファイル</summary>
    public static Dictionary<string, string> MatchImages(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = ImageFilesIn(folder).ToList();
        foreach (var k in ImageKinds)
        {
            var f = files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals(k, StringComparison.OrdinalIgnoreCase));
            if (f != null) map[k] = f;
        }
        return map;
    }

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
        var voices = d.TryGetValue("voices", out var vo) ? P(vo) : Path.Combine(folder, "voices");
        if (voices != null && Directory.Exists(voices))
        {
            // Pack の声のファイル: 名前 (_JP 付き) で表の行に入れる。_JP なしだけのものは _JP 付きの行に
            var files = AudioFiles(voices).ToList();
            foreach (var f in files.Where(f => BaseName(f).EndsWith("_JP", StringComparison.OrdinalIgnoreCase)))
                if (!p.VoiceFiles.ContainsKey(BaseName(f))) p.VoiceFiles[BaseName(f)] = f;
            foreach (var f in files.Where(f => !BaseName(f).EndsWith("_JP", StringComparison.OrdinalIgnoreCase)))
                if (!p.VoiceFiles.ContainsKey(BaseName(f) + "_JP")) p.VoiceFiles[BaseName(f) + "_JP"] = f;
            p.VoiceChinese = files.Count == 0 || files.Any(f => !BaseName(f).EndsWith("_JP", StringComparison.OrdinalIgnoreCase));
        }
        foreach (var kv in MatchImages(Path.Combine(folder, "images"))) p.ImageFiles[kv.Key] = kv.Value;
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
        foreach (var kv in VoiceFiles)
            if (!string.IsNullOrEmpty(kv.Value) && !File.Exists(kv.Value)) list.Add(Strings.T("声 ({0}) のファイルがありません", kv.Key));
        foreach (var kv in ImageFiles)
            if (!string.IsNullOrEmpty(kv.Value) && !File.Exists(kv.Value)) list.Add(Strings.T("絵 ({0}) のファイルがありません", kv.Key));
        return list;
    }

    public static IEnumerable<string> AudioFiles(string folder) =>
        folder == null || !Directory.Exists(folder) ? Enumerable.Empty<string>()
            : Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).Where(f => AudioExt.Contains(Path.GetExtension(f).ToLowerInvariant()));

    /// <summary>"名前#2.ogg" → "名前" (# から後ろは、同じ声を何通りか置くときの印)</summary>
    public static string BaseName(string file)
    {
        var n = Path.GetFileNameWithoutExtension(file);
        int h = n.IndexOf('#');
        return h > 0 ? n.Substring(0, h) : n;
    }

    /// <summary>フォルダの中の、土台のキャラの声の名前に合うファイル (_JP なしの名前も合わせる)</summary>
    public static Dictionary<string, string> MatchVoices(string folder, BaseChara b)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = AudioFiles(folder).ToList();
        foreach (var v in b.Voices)
        {
            var f = files.FirstOrDefault(x => BaseName(x).Equals(v, StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(x => BaseName(x).Equals(v.Substring(0, v.Length - 3), StringComparison.OrdinalIgnoreCase));
            if (f != null) map[v] = f;
        }
        return map;
    }


    // ------------------------------------------------------------------ 作る

    /// <summary>
    /// 絵: 選んだファイルを images\〈絵の名前〉.png に置く (PNG でなければ PNG にする)。
    /// 縦横の比がお手本と違えば、透明の余白を足して比を合わせる (ゲームは比が同じなら大きさの違いは合わせてくれる)。
    /// 前に置いた絵で、今は選んでいないものは消す
    /// </summary>
    private void PlaceImages(string dest)
    {
        var to = Path.Combine(dest, "images");
        // コピー元が images の中のファイル (開いた Pack) のこともあるので、先に全部メモリに読んでから書く
        var ready = new List<(string kind, byte[] png)>();
        foreach (var kv in ImageFiles)
        {
            if (string.IsNullOrEmpty(kv.Value) || !File.Exists(kv.Value)) continue;
            var want = ImageSize(dest, kv.Key);
            using var src = new Bitmap(new MemoryStream(File.ReadAllBytes(kv.Value)));
            bool fit = ImageFit && want is Size w && AspectDiffers(src.Size, w);
            bool png = Path.GetExtension(kv.Value).Equals(".png", StringComparison.OrdinalIgnoreCase);
            if (!fit && png) { ready.Add((kv.Key, File.ReadAllBytes(kv.Value))); continue; }
            Bitmap outBmp = src;
            if (fit)
            {
                // 絵がはみ出さない、お手本と同じ比の大きさ (絵の解像度のまま)
                var ws = want.Value;
                double ratio = ws.Width / (double)ws.Height;
                int cw = src.Width, ch = src.Height;
                if (src.Width / (double)src.Height > ratio) ch = (int)Math.Round(src.Width / ratio);
                else cw = (int)Math.Round(src.Height * ratio);
                outBmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(outBmp);
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle((cw - src.Width) / 2, (ch - src.Height) / 2, src.Width, src.Height));
            }
            using (var ms = new MemoryStream())
            {
                outBmp.Save(ms, ImageFormat.Png);
                ready.Add((kv.Key, ms.ToArray()));
            }
            if (!ReferenceEquals(outBmp, src)) outBmp.Dispose();
        }
        var keep = new HashSet<string>(ready.Select(r => r.kind + ".png"), StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(to))
            foreach (var f in ImageFilesIn(to).ToList())
                if (ImageKinds.Contains(Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase) && !keep.Contains(Path.GetFileName(f))) File.Delete(f);
        foreach (var (kind, data) in ready)
        {
            Directory.CreateDirectory(to);
            File.WriteAllBytes(Path.Combine(to, kind + ".png"), data);
        }
    }

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
        // 声: 選んだファイルを、要る声の名前に変えて voices に置く (中国語の設定のための _JP なしの名前でも)。
        // 前に置いた声で、今は選んでいないものは消す (選び直したときに古い声が残らないように)
        bool voices = false;
        var vto = Path.Combine(dest, "voices");
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var staged = new List<(string from, string to)>();
        foreach (var kv in VoiceFiles)
        {
            if (string.IsNullOrEmpty(kv.Value) || !File.Exists(kv.Value)) continue;
            var ext = Path.GetExtension(kv.Value).ToLowerInvariant();
            var names = new List<string> { kv.Key };
            if (VoiceChinese && kv.Key.EndsWith("_JP", StringComparison.OrdinalIgnoreCase)) names.Add(kv.Key.Substring(0, kv.Key.Length - 3));
            foreach (var n in names)
            {
                var to = Path.Combine(vto, n + ext);
                keep.Add(Path.GetFullPath(to));
                staged.Add((kv.Value, to));
            }
        }
        // コピー元が voices の中のファイル (開いた Pack) のこともあるので、先に一時ファイルへ写してから置く
        var temp = staged.Select(x => (tmp: Path.GetTempFileName(), x.to)).ToList();
        for (int i = 0; i < staged.Count; i++) File.Copy(staged[i].from, temp[i].tmp, true);
        if (Directory.Exists(vto))
            foreach (var f in AudioFiles(vto).ToList())
                if (!keep.Contains(Path.GetFullPath(f))) File.Delete(f);
        foreach (var (tmp, to) in temp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(tmp, to, true);
            File.Delete(tmp);
        }
        voices = staged.Count > 0;
        PlaceImages(dest);

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
