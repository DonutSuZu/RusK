using System.Collections.Generic;
using System.Globalization;

namespace RusK.PackCreator;

/// <summary>表示の言語 (日本語・英語・中国語)。日本語の文をキーにして、英語と中国語の訳を引く (Mod Manager と同じ作り)</summary>
internal static class Strings
{
    public static readonly string[] Codes = { "ja", "en", "zh" };
    public static readonly string[] Names = { "日本語", "English", "中文" };

    public static string Current { get; set; } = Detect();
    public static int CurrentIndex => System.Array.IndexOf(Codes, Current);

    private static string Detect()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("ja")) return "ja";
        if (name.StartsWith("zh")) return "zh";
        return "en";
    }

    public static string T(string ja)
    {
        if (Current == "ja" || !Table.TryGetValue(ja, out var t)) return ja;
        var s = Current == "en" ? t.en : t.zh;
        return string.IsNullOrEmpty(s) ? ja : s;
    }

    public static string T(string ja, params object[] args) => string.Format(T(ja), args);

    private static readonly Dictionary<string, (string en, string zh)> Table = new()
    {
        ["ゲームのフォルダを選ぶ..."] = ("Choose the game folder...", "选择游戏文件夹..."),
        ["ゲームに入れる"] = ("Put into the game", "放入游戏"),
        ["zip で書き出す"] = ("Export as zip", "导出为 zip"),
        ["Pack を開く..."] = ("Open a Pack...", "打开 Pack..."),
        ["新しく作る"] = ("New", "新建"),
        ["選ぶ..."] = ("Choose...", "选择..."),
        ["外す"] = ("Clear", "清除"),
        ["1. パック"] = ("1. Pack", "1. Pack"),
        ["新しいキャラの枠です。動作・能力値・攻撃の判定は「土台のキャラ」を複製します。"] =
            ("A new character slot. Moves, stats and attack hitboxes are copied from the \"base character\".", "新角色栏位。动作、能力值和攻击判定复制自\"基础角色\"。"),
        ["フォルダ名 (英数字)"] = ("Folder name (letters/numbers)", "文件夹名 (英文字母和数字)"),
        ["キャラ番号"] = ("Character ID", "角色编号"),
        ["土台のキャラ"] = ("Base character", "基础角色"),
        ["名前 (日本語)"] = ("Name (Japanese)", "名字 (日语)"),
        ["名前 (英語)"] = ("Name (English)", "名字 (英语)"),
        ["名前 (中国語)"] = ("Name (Chinese)", "名字 (中文)"),
        ["2. 見た目"] = ("2. Looks", "2. 外观"),
        ["VRM か PMX (MMD)。PMX はテクスチャの入ったフォルダごとコピーします。無ければ土台のキャラの見た目のままです。"] =
            ("VRM or PMX (MMD). For PMX, the whole folder with the textures is copied. Without one, the base character's looks are used.", "VRM 或 PMX (MMD)。PMX 会连同贴图所在的文件夹一起复制。不设置则使用基础角色的外观。"),
        ["モデル"] = ("Model", "模型"),
        ["3. 武器"] = ("3. Weapon", "3. 武器"),
        ["glb か PMX。新しいキャラが「置き換える装備」を持つときだけ置き換えます (土台のキャラの武器は変わりません)。位置・回転・大きさは、ゲームで見ながら合わせてください。"] =
            ("glb or PMX. Replaced only while the new character holds the \"equipment to replace\" (the base character's weapon does not change). Adjust position, rotation and size while watching the game.", "glb 或 PMX。仅在新角色持有\"要替换的装备\"时替换 (基础角色的武器不变)。位置、旋转和大小请边看游戏边调整。"),
        ["武器"] = ("Weapon", "武器"),
        ["置き換える装備の番号"] = ("Equipment ID to replace", "要替换的装备编号"),
        ["大きさ"] = ("Size", "大小"),
        ["回転 (度) X / Y / Z"] = ("Rotation (deg) X / Y / Z", "旋转 (度) X / Y / Z"),
        ["位置 X / Y / Z"] = ("Position X / Y / Z", "位置 X / Y / Z"),
        ["4. 動き"] = ("4. Motions", "4. 动作"),
        ["動作ごとに、自作の動き (Blender で作った glb) を割り当てます。「当たる瞬間」は自分の動きで剣が当たる秒です (攻撃の動作だけ)。右の「土台」は、土台のキャラの動作の長さと攻撃判定の位置です。"] =
            ("Assign your own motions (glb made in Blender) per action. \"Hit time\" is the second at which your motion hits (attacks only). \"Base\" on the right shows the base character's motion length and hit position.", "为每个动作分配自制动作 (用 Blender 制作的 glb)。\"命中瞬间\"是你的动作命中的秒数 (仅攻击动作)。右侧的\"基础\"是基础角色动作的长度和攻击判定位置。"),
        ["動作"] = ("Action", "动作"),
        ["動きのファイル (ダブルクリックで選ぶ)"] = ("Motion file (double-click to choose)", "动作文件 (双击选择)"),
        ["アニメーションの名前 (省略可)"] = ("Animation name (optional)", "动画名 (可省略)"),
        ["当たる瞬間 (秒)"] = ("Hit time (s)", "命中瞬间 (秒)"),
        ["土台"] = ("Base", "基础"),
        ["＋ 動作を足す"] = ("+ Add action", "+ 添加动作"),
        ["＋ 通常攻撃 5 段を足す"] = ("+ Add 5-hit normal attack", "+ 添加 5 段普通攻击"),
        ["選んだ行を消す"] = ("Remove selected rows", "删除选中的行"),
        ["5. 声"] = ("5. Voices", "5. 语音"),
        ["ogg / wav / mp3 を入れたフォルダ。ファイル名は「土台のキャラの声の名前」と同じにします (例 LightAttackVoice_1006_1_JP.ogg)。新しいキャラが場にいるときだけ使われます。"] =
            ("A folder with ogg / wav / mp3 files. Name each file after the base character's voice (e.g. LightAttackVoice_1006_1_JP.ogg). Used only while the new character is on the field.", "放有 ogg / wav / mp3 的文件夹。文件名与\"基础角色的语音名\"相同 (例 LightAttackVoice_1006_1_JP.ogg)。仅在新角色在场时使用。"),
        ["声のフォルダ"] = ("Voice folder", "语音文件夹"),
        ["声のファイルを入れたフォルダ"] = ("The folder with the voice files", "放有语音文件的文件夹"),
        ["要る声の一覧"] = ("List of voices needed", "所需语音一览"),
        ["6. 絵"] = ("6. Images", "6. 图片"),
        ["カード・立ち絵・名前などの PNG を入れたフォルダ (名前と大きさは、ゲームが書き出すお手本 images_template と同じ)。無い絵は土台のキャラの絵のままです。"] =
            ("A folder with PNGs for the card, portraits, name, ... (same names and sizes as the templates the game writes to images_template). Missing images stay as the base character's.", "放有卡片、立绘、名字等 PNG 的文件夹 (名字和大小与游戏导出的模板 images_template 相同)。缺少的图片使用基础角色的图片。"),
        ["絵のフォルダ"] = ("Image folder", "图片文件夹"),
        ["絵 (PNG) を入れたフォルダ"] = ("The folder with the images (PNG)", "放有图片 (PNG) 的文件夹"),
        ["土台のキャラの動作: {0} 個"] = ("Base character's actions: {0}", "基础角色的动作: {0} 个"),
        ["(土台に無い動作)"] = ("(not in the base)", "(基础角色没有的动作)"),
        [" / 当たる {0:0.00}"] = (" / hit {0:0.00}", " / 命中 {0:0.00}"),
        [" / ループ"] = (" / loop", " / 循环"),
        ["土台のキャラの声: {0} 個"] = ("Base character's voices: {0}", "基础角色的语音: {0} 个"),
        ["ファイル {0} 個 / 名前が合うもの {1} / {2}"] = ("{0} files / {1} of {2} names match", "文件 {0} 个 / 名字匹配 {1} / {2}"),
        ["足りない声 ({0} 個)。一覧をクリップボードにコピーしました。"] = ("Missing voices ({0}). The list was copied to the clipboard.", "缺少的语音 ({0} 个)。已将列表复制到剪贴板。"),
        ["要る声 ({0} 個)。一覧をクリップボードにコピーしました。"] = ("Voices needed ({0}). The list was copied to the clipboard.", "所需语音 ({0} 个)。已将列表复制到剪贴板。"),
        ["中国語の声の設定で遊ぶ人のためには、最後の _JP を外した名前のファイルも置きます。"] =
            ("For players using Chinese voices, also add files without the trailing _JP.", "为使用中文语音的玩家, 也请放入去掉末尾 _JP 的文件。"),
        ["絵は {0} 種類 (キャラの絵 15 + 飾り 10)"] = ("{0} kinds of images (15 character art + 10 decorations)", "图片共 {0} 种 (角色图 15 + 装饰 10)"),
        ["{0} / {1} 種類 (無い絵: {2})"] = ("{0} / {1} kinds (missing: {2})", "{0} / {1} 种 (缺少: {2})"),
        ["新しい Pack です。上から順に埋めて「ゲームに入れる」を押してください。"] = ("A new Pack. Fill in from the top and press \"Put into the game\".", "新的 Pack。请从上往下填写, 然后点击\"放入游戏\"。"),
        ["開く Pack のフォルダ (character.json があるフォルダ)"] = ("The Pack folder to open (the folder with character.json)", "要打开的 Pack 文件夹 (有 character.json 的文件夹)"),
        ["このフォルダには character.json がありません"] = ("This folder has no character.json", "此文件夹中没有 character.json"),
        ["開きました: {0}"] = ("Opened: {0}", "已打开: {0}"),
        ["直してください"] = ("Please fix", "请修改"),
        ["{0} はもうあります。上書きしますか?"] = ("{0} already exists. Overwrite?", "{0} 已存在。要覆盖吗?"),
        ["ゲームに入れました: {0}  (ゲームを起動し直すと出ます)"] = ("Put into the game: {0}  (restart the game to see it)", "已放入游戏: {0}  (重启游戏后出现)"),
        ["先に「ゲームに入れる」で Pack を作ってください"] = ("Create the Pack with \"Put into the game\" first", "请先用\"放入游戏\"创建 Pack"),
        ["書き出しました: {0}"] = ("Exported: {0}", "已导出: {0}"),
        ["  (ゲームから書き出した物は除きました: {0})"] = ("  (files exported from the game were left out: {0})", "  (已排除从游戏导出的文件: {0})"),
        ["先にゲームのフォルダを選んでください"] = ("Choose the game folder first", "请先选择游戏文件夹"),
        ["VED:Recure のフォルダ (ved.exe がある場所) を選んでください"] = ("Choose the VED:Recure folder (where ved.exe is)", "请选择 VED:Recure 的文件夹 (ved.exe 所在位置)"),
        ["このフォルダには ved.exe がありません"] = ("This folder has no ved.exe", "此文件夹中没有 ved.exe"),
        ["ゲームのフォルダが見つかりません"] = ("Game folder not found", "找不到游戏文件夹"),
        ["ゲーム: {0}"] = ("Game: {0}", "游戏: {0}"),
        ["(対応: {0})"] = ("(supports: {0})", "(支持: {0})"),
        ["フォルダ名に使えない文字があります"] = ("The folder name has characters that can't be used", "文件夹名中有不能使用的字符"),
        ["キャラ番号は 9000 以上にしてください"] = ("The character ID must be 9000 or more", "角色编号请设为 9000 以上"),
        ["キャラ番号 {0} は、ほかの Pack ({1}) が使っています"] = ("Character ID {0} is used by another Pack ({1})", "角色编号 {0} 已被其他 Pack ({1}) 使用"),
        ["名前を入れてください"] = ("Enter a name", "请输入名字"),
        ["モデルのファイルがありません: {0}"] = ("Model file not found: {0}", "找不到模型文件: {0}"),
        ["武器のファイルがありません: {0}"] = ("Weapon file not found: {0}", "找不到武器文件: {0}"),
        ["動き ({0}) のファイルがありません"] = ("Motion file for {0} not found", "找不到动作 ({0}) 的文件"),
    };
}
