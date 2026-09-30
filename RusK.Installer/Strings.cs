using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RusK.Installer;

/// <summary>
/// セットアップの表示の言語 (日本語・英語・中国語)。日本語の文をキーにして、英語と中国語の訳を引く。
/// "{0}" などを含む文は T("...{0}...", 値) のように値を渡す
/// </summary>
internal static class Strings
{
    public static readonly string[] Codes = { "ja", "en", "zh" };
    public static readonly string[] Names = { "日本語", "English", "中文" };

    /// <summary>今の言語 (ja / en / zh)</summary>
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

    /// <summary>日本語の文 → (英語, 中国語)</summary>
    private static readonly Dictionary<string, (string en, string zh)> Table = new()
    {
        // 枠
        ["RusK セットアップ v{0}"] = ("RusK Setup v{0}", "RusK 安装程序 v{0}"),
        ["ようこそ"] = ("Welcome", "欢迎"),
        ["インストール先"] = ("Location", "安装位置"),
        ["コンポーネント"] = ("Components", "组件"),
        ["インストール"] = ("Install", "安装"),
        ["完了"] = ("Finish", "完成"),
        ["対応ゲーム:"] = ("Supported game:", "支持的游戏:"),
        ["< 戻る"] = ("< Back", "< 上一步"),
        ["次へ >"] = ("Next >", "下一步 >"),
        ["キャンセル"] = ("Cancel", "取消"),
        ["処理中は閉じられません。"] = ("You can't close the setup while it is working.", "处理中无法关闭。"),
        ["表示の言語"] = ("Language", "显示语言"),

        // ようこそ
        ["RusK のセットアップを始めます"] = ("Let's set up RusK", "开始安装 RusK"),
        ["VED:Recure 用の Mod ローダー「RusK」をインストールします。\n\n" +
         "RusK でできること\n" +
         "  ・ゲーム内メニュー (Insert キー) で Mod の機能を ON/OFF\n" +
         "  ・Mod の読み込み / 取り外し / 再読み込み\n" +
         "  ・キー割り当て、アクショントリガー、設定プロファイル\n\n" +
         "必要なもの\n" +
         "  ・VED:Recure (Steam 版)  … 対応バージョン {0}\n" +
         "  ・BepInEx 6 (IL2CPP 版)  … 入っていなければ一緒にダウンロードしてインストールします\n" +
         "  ・インターネット接続  … 選んだ Mod を GitHub のリリースからダウンロードします\n\n" +
         "注意\n" +
         "  ・インストール中はゲームを終了しておいてください"] = (
            "This installs RusK, a mod loader for VED:Recure.\n\n" +
            "What RusK does\n" +
            "  - Turn mod features on and off from the in-game menu (Insert key)\n" +
            "  - Load / unload / reload mods\n" +
            "  - Key bindings, action triggers and setting profiles\n\n" +
            "Requirements\n" +
            "  - VED:Recure (Steam)  … supported version {0}\n" +
            "  - BepInEx 6 (IL2CPP)  … if it's missing, it is downloaded and installed too\n" +
            "  - An internet connection  … the mods you choose are downloaded from GitHub releases\n\n" +
            "Note\n" +
            "  - Please close the game while installing",
            "将为 VED:Recure 安装 Mod 加载器「RusK」。\n\n" +
            "RusK 的功能\n" +
            "  ・在游戏内菜单 (Insert 键) 中开关 Mod 功能\n" +
            "  ・加载 / 卸载 / 重新加载 Mod\n" +
            "  ・按键设置、动作触发器、设置方案\n\n" +
            "需要\n" +
            "  ・VED:Recure (Steam 版)  … 支持版本 {0}\n" +
            "  ・BepInEx 6 (IL2CPP 版)  … 如未安装, 会一并下载并安装\n" +
            "  ・网络连接  … 所选的 Mod 会从 GitHub 的发布页下载\n\n" +
            "注意\n" +
            "  ・安装时请先关闭游戏"),

        // インストール先
        ["VED:Recure のフォルダを選んでください"] = ("Choose the VED:Recure folder", "请选择 VED:Recure 的文件夹"),
        ["ゲームのフォルダ (ved.exe がある場所)"] = ("Game folder (where ved.exe is)", "游戏文件夹 (ved.exe 所在位置)"),
        ["参照..."] = ("Browse...", "浏览..."),
        ["自動検出"] = ("Detect", "自动检测"),
        ["Steam のライブラリに VED:Recure が見つかりませんでした。\n「参照...」から選んでください。"] = (
            "VED:Recure was not found in your Steam libraries.\nPlease choose it with \"Browse...\".",
            "在 Steam 库中找不到 VED:Recure。\n请通过「浏览...」选择。"),
        ["BepInEx 6 (IL2CPP 版) が必要です。動作確認済みの be.788 を入れます。"] = (
            "BepInEx 6 (IL2CPP) is required. The tested version be.788 will be installed.",
            "需要 BepInEx 6 (IL2CPP 版)。将安装已验证的 be.788。"),
        ["自動でダウンロードしてインストールする (おすすめ・約 33 MB)"] = ("Download and install it automatically (recommended, about 33 MB)", "自动下载并安装 (推荐, 约 33 MB)"),
        ["ダウンロードした zip を選ぶ"] = ("Choose a zip you downloaded", "选择已下载的 zip"),
        ["be.788 の zip のリンク"] = ("Link to the be.788 zip", "be.788 的 zip 链接"),
        ["BepInEx: 入っていません (RusK と一緒に入れます)"] = ("BepInEx: not installed (it will be installed with RusK)", "BepInEx: 未安装 (将与 RusK 一起安装)"),
        ["BepInEx {0} をダウンロードしています..."] = ("Downloading BepInEx {0}...", "正在下载 BepInEx {0}..."),
        ["ダウンロードしたファイルが壊れています (SHA256 が一致しません)"] = ("The downloaded file is corrupted (SHA256 mismatch)", "下载的文件已损坏 (SHA256 不一致)"),
        ["zip を選択..."] = ("Choose zip...", "选择 zip..."),
        ["BepInEx の zip を選択"] = ("Choose the BepInEx zip", "选择 BepInEx 的 zip"),
        ["RusK はすでにインストールされています。どうしますか？"] = ("RusK is already installed. What would you like to do?", "RusK 已安装。要做什么?"),
        ["更新・修復する (設定はそのまま)"] = ("Update / repair (settings are kept)", "更新 / 修复 (保留设置)"),
        ["アンインストールする"] = ("Uninstall", "卸载"),
        ["VED:Recure のフォルダ (ved.exe がある場所) を選んでください"] = ("Choose the VED:Recure folder (where ved.exe is)", "请选择 VED:Recure 的文件夹 (ved.exe 所在位置)"),
        ["ゲーム: 見つかりました"] = ("Game: found", "游戏: 已找到"),
        ["ゲーム: ved.exe がありません (フォルダを確認してください)"] = ("Game: ved.exe not found (check the folder)", "游戏: 找不到 ved.exe (请确认文件夹)"),
        ["BepInEx: 導入済み ({0})"] = ("BepInEx: installed ({0})", "BepInEx: 已安装 ({0})"),
        ["BepInEx: 入っていません (下で zip を選ぶと一緒に導入します)"] = ("BepInEx: not installed (choose the zip below to install it too)", "BepInEx: 未安装 (在下方选择 zip 后会一并安装)"),
        ["●  RusK: v{0} がインストール済み → v{1} で更新できます"] = ("●  RusK: v{0} is installed → can be updated to v{1}", "●  RusK: 已安装 v{0} → 可更新为 v{1}"),
        ["●  RusK: 未インストール"] = ("●  RusK: not installed", "●  RusK: 未安装"),

        // コンポーネント
        ["インストールするものを選んでください"] = ("Choose what to install", "请选择要安装的内容"),
        ["RusK 本体 (必須)"] = ("RusK core (required)", "RusK 本体 (必需)"),
        ["Mod ローダー・メニュー (TabGUI / ClickGUI)・HUD・Config・Check (Mod の点検)・言語"] = (
            "Mod loader, menus (TabGUI / ClickGUI), HUD, config, Check (mod diagnostics), languages",
            "Mod 加载器、菜单 (TabGUI / ClickGUI)、HUD、配置、Check (Mod 检查)、语言"),
        ["ゼンゼロ風ボタン HUD (液体ゲージ・追加攻撃が光る)・攻撃予兆・キー追加"] = (
            "Zenless Zone Zero–style button HUD (liquid gauge, glowing follow-up), attack warnings, extra keys",
            "绝区零风格按键 HUD (液体能量槽、追加攻击发光)、攻击预警、追加按键"),
        ["難易度 EXTREME を解放。敵の HP・攻撃力・攻撃頻度・シールドを強化"] = (
            "Unlocks the EXTREME difficulty. Stronger enemy HP, attack, attack rate and shields",
            "解锁 EXTREME 难度。强化敌人的生命值、攻击力、攻击频率和护盾"),
        ["戦闘中の BGM を RusK\\music の曲 (mp3 / ogg / wav) に置き換える"] = (
            "Replaces the battle music with songs in RusK\\music (mp3 / ogg / wav)",
            "将战斗 BGM 替换为 RusK\\music 中的歌曲 (mp3 / ogg / wav)"),
        ["視点の切り替え (近い肩越し / 真後ろ / 一人称 / カスタム)"] = (
            "Camera views (close over-the-shoulder / behind / first person / custom)",
            "切换视角 (近距离越肩 / 正后方 / 第一人称 / 自定义)"),
        ["アクティブ3人。仲間 2 人と戦闘中にキーで交代。切り替えパリィ・戦闘不能時の自動交代・パッシブバフの共有"] = (
            "Three active characters. Switch between you and two companions in battle; switch parries; auto-switch when knocked out; shared passive buffs",
            "三人同时上场。战斗中用按键与两名队友切换。切换招架、无法战斗时自动切换、共享被动增益"),
        ["連携攻撃。300 ヒットためて追加攻撃を当てると時間が止まり、仲間の追加攻撃を繋げる (Party が必要)"] = (
            "Chain attack. After 300 hits, landing a follow-up attack stops time and chains your companions' follow-up attacks (requires Party)",
            "连携攻击。累计 300 次命中后用追加攻击命中, 时间会停止, 可接上队友的追加攻击 (需要 Party)"),
        ["キャラの見た目を VRM にする (RusK\\models に .vrm を置く)。口パク・表情・揺れ物に対応"] = (
            "Turns characters into VRM models (put .vrm files in RusK\\models). Lip sync, expressions and physics",
            "将角色外观替换为 VRM (把 .vrm 放入 RusK\\models)。支持口型、表情和物理摆动"),
        ["武器・装飾品の見た目を glb にする (RusK\\props に .glb を置く)。発光も設定できる"] = (
            "Replaces weapons and accessories with glb models (put .glb files in RusK\\props). Glow can be set",
            "将武器和饰品替换为 glb 模型 (把 .glb 放入 RusK\\props)。可设置发光"),
        ["最新: v{0}"] =("latest: v{0}", "最新: v{0}"),
        ["最新版を確認中..."] = ("Checking the latest versions...", "正在检查最新版本..."),
        ["最新版を確認できませんでした: {0}"] = ("Could not check the latest versions: {0}", "无法检查最新版本: {0}"),
        ["リリースにありません"] = ("not released", "未发布"),
        ["選んだ Mod は GitHub のリリースから最新版をダウンロードします。チェックを外した Mod は取り外します (設定は残します)。"] = (
            "The mods you choose are downloaded (latest version) from GitHub releases. Unchecked mods are removed (settings are kept).",
            "所选 Mod 会从 GitHub 发布页下载最新版本。取消勾选的 Mod 会被移除 (保留设置)。"),
        ["music フォルダを作る (RusK\\music と RusK\\music\\boss)"] = ("Create the music folder (RusK\\music and RusK\\music\\boss)", "创建 music 文件夹 (RusK\\music 和 RusK\\music\\boss)"),
        ["アンインストール"] = ("Uninstall", "卸载"),
        ["削除する内容を確認してください"] = ("Check what will be removed", "请确认要删除的内容"),
        ["RusK 本体と、同梱の Mod を削除します。\nBepInEx と、ほかの人が作った Mod は削除しません。"] = (
            "RusK and its mods will be removed.\nBepInEx and mods made by others are not removed.",
            "将删除 RusK 本体和附带的 Mod。\n不会删除 BepInEx 和其他人制作的 Mod。"),
        ["設定とデータも削除する (RusK\\configs, RusK\\data)"] = ("Also remove settings and data (RusK\\configs, RusK\\data)", "同时删除设置和数据 (RusK\\configs, RusK\\data)"),
        ["music フォルダも削除する (入れた曲も消えます)"] = ("Also remove the music folder (your songs are deleted too)", "同时删除 music 文件夹 (放入的歌曲也会被删除)"),

        // 実行
        ["インストール中"] = ("Installing", "正在安装"),
        ["アンインストール中"] = ("Uninstalling", "正在卸载"),
        ["やり直す"] = ("Retry", "重试"),
        ["エラー: フォルダに書き込めませんでした。セットアップを右クリック →「管理者として実行」で試してください。"] = (
            "Error: could not write to the folder. Right-click the setup and choose \"Run as administrator\".",
            "错误: 无法写入文件夹。请右键安装程序 →「以管理员身份运行」。"),
        ["エラー: {0}"] = ("Error: {0}", "错误: {0}"),
        ["ゲームが起動中です。ゲームを終了してから続けてください。"] = ("The game is running. Please close it before continuing.", "游戏正在运行。请关闭游戏后继续。"),
        ["ゲームが起動中です。ゲームを終了してからやり直してください。"] = ("The game is running. Please close it and try again.", "游戏正在运行。请关闭游戏后重试。"),
        ["BepInEx が入っていません。BepInEx の zip を選んでください。"] = ("BepInEx is not installed. Please choose the BepInEx zip.", "未安装 BepInEx。请选择 BepInEx 的 zip。"),
        ["BepInEx を展開しています..."] = ("Extracting BepInEx...", "正在解压 BepInEx..."),
        ["BepInEx を展開しましたが、IL2CPP 版の BepInEx が見つかりません。\n「BepInEx-Unity.IL2CPP-win-x64」の zip か確認してください。"] = (
            "BepInEx was extracted, but the IL2CPP version of BepInEx was not found.\nMake sure it is the \"BepInEx-Unity.IL2CPP-win-x64\" zip.",
            "已解压 BepInEx, 但找不到 IL2CPP 版的 BepInEx。\n请确认是「BepInEx-Unity.IL2CPP-win-x64」的 zip。"),
        ["  BepInEx を導入しました"] = ("  BepInEx installed", "  已安装 BepInEx"),
        ["BepInEx: 導入済み"] = ("BepInEx: already installed", "BepInEx: 已安装"),
        ["選んだ zip に winhttp.dll がありません。BepInEx の zip ではないようです。"] = (
            "The chosen zip has no winhttp.dll. It doesn't look like a BepInEx zip.",
            "所选 zip 中没有 winhttp.dll, 似乎不是 BepInEx 的 zip。"),
        ["  {0} ファイルを展開しました"] = ("  Extracted {0} files", "  已解压 {0} 个文件"),
        ["RusK 本体をインストールしています..."] = ("Installing the RusK core...", "正在安装 RusK 本体..."),
        ["リリースの一覧を取得しています..."] = ("Getting the list of releases...", "正在获取发布列表..."),
        ["{0} をダウンロードしています ({1})..."] = ("Downloading {0} ({1})...", "正在下载 {0} ({1})..."),
        ["{0}: {1} / {2}"] = ("{0}: {1} / {2}", "{0}: {1} / {2}"),
        ["  {0} ({1}) を配置しました"] = ("  Installed {0} ({1})", "  已放置 {0} ({1})"),
        ["{0} がリリースに見つかりません"] = ("{0} was not found in the releases", "在发布中找不到 {0}"),
        ["  {0} には {1} が必要なので、一緒に入れます"] = ("  {0} requires {1}, so it will be installed too", "  {0} 需要 {1}, 将一并安装"),
        ["{0} をダウンロードできませんでした: {1}"] = ("Could not download {0}: {1}", "无法下载 {0}: {1}"),
        ["  - {0} (選択されていないので削除)"] = ("  - {0} (removed because it wasn't selected)", "  - {0} (未选择, 已删除)"),
        ["  music フォルダを作成しました"] = ("  Created the music folder", "  已创建 music 文件夹"),
        ["  models フォルダを作成しました (ここに .vrm を置きます)"] = ("  Created the models folder (put .vrm files here)", "  已创建 models 文件夹 (在此放入 .vrm)"),
        ["  props フォルダを作成しました (ここに .glb を置きます)"] = ("  Created the props folder (put .glb files here)", "  已创建 props 文件夹 (在此放入 .glb)"),
        ["インストールが完了しました"] = ("Installation complete", "安装完成"),
        ["アンインストールが完了しました (BepInEx は残しています)"] = ("Uninstall complete (BepInEx was kept)", "卸载完成 (保留了 BepInEx)"),
        ["セットアップに payload.zip が入っていません (ビルドの問題)"] = ("The setup has no payload.zip (build problem)", "安装程序中没有 payload.zip (构建问题)"),

        // 完了
        ["アンインストールしました"] = ("Uninstalled", "已卸载"),
        ["インストールしました"] = ("Installed", "已安装"),
        ["RusK を削除しました。\nBepInEx はそのまま残っています。"] = ("RusK has been removed.\nBepInEx is still installed.", "已删除 RusK。\nBepInEx 仍然保留。"),
        ["RusK をインストールしました。\n\n" +
         "・ゲームを起動すると、画面右下に「RusK v{0}」と表示されます\n" +
         "・Insert キーでメニューを開きます (表示の言語は Visual > Language)\n" +
         "・初回起動は BepInEx の準備で時間がかかることがあります\n" +
         "・曲は RusK\\music に入れてください\n" +
         "・使い方は RusK\\README.txt を見てください"] = (
            "RusK has been installed.\n\n" +
            "- When you start the game, \"RusK v{0}\" appears at the bottom right\n" +
            "- Press Insert to open the menu (display language: Visual > Language)\n" +
            "- The first launch can take a while while BepInEx prepares\n" +
            "- Put songs in RusK\\music\n" +
            "- See RusK\\README.txt for how to use it",
            "已安装 RusK。\n\n" +
            "・启动游戏后, 屏幕右下角会显示「RusK v{0}」\n" +
            "・按 Insert 键打开菜单 (显示语言: Visual > Language)\n" +
            "・首次启动时 BepInEx 需要准备, 可能需要一些时间\n" +
            "・歌曲请放入 RusK\\music\n" +
            "・使用方法请查看 RusK\\README.txt"),
        ["ゲームを起動する (Steam)"] = ("Start the game (Steam)", "启动游戏 (Steam)"),
        ["インストール先のフォルダを開く"] = ("Open the install folder", "打开安装文件夹"),
    };
}
