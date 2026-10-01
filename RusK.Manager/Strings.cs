using System.Collections.Generic;
using System.Globalization;

namespace RusK.Manager;

/// <summary>
/// Mod Manager の表示の言語 (日本語・英語・中国語)。日本語の文をキーにして、英語と中国語の訳を引く。
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
        // 上の帯・メニュー
        ["ゲームのフォルダを選ぶ..."] = ("Choose the game folder...", "选择游戏文件夹..."),
        ["ゲームのフォルダを開く"] = ("Open the game folder", "打开游戏文件夹"),
        ["Mod のフォルダを開く"] = ("Open the mods folder", "打开 Mod 文件夹"),
        ["最新の情報を取得し直す"] = ("Check for updates again", "重新获取最新信息"),
        ["GitHub のページを開く"] = ("Open the GitHub page", "打开 GitHub 页面"),
        ["RusK をアンインストール..."] = ("Uninstall RusK...", "卸载 RusK..."),
        ["＋ DLL を追加"] = ("+ Add DLL", "+ 添加 DLL"),
        ["ゲームのフォルダが見つかりません"] = ("Game folder not found", "找不到游戏文件夹"),
        ["ゲーム: {0}"] = ("Game: {0}", "游戏: {0}"),

        // 一覧
        ["入っている版"] = ("Installed", "已安装版本"),
        ["最新"] = ("Latest", "最新"),
        ["状態"] = ("Status", "状态"),
        ["Mod の DLL をこのウィンドウにドラッグ＆ドロップすると追加できます"] = (
            "Drag and drop a mod DLL onto this window to add it", "把 Mod 的 DLL 拖放到此窗口即可添加"),
        ["入手できます"] = ("Available", "可获取"),
        ["RusK の Mod ではないかも"] = ("May not be a RusK mod", "可能不是 RusK 的 Mod"),
        ["必要な Mod がありません"] = ("Missing a required mod", "缺少所需的 Mod"),
        ["更新があります"] = ("Update available", "有更新"),
        ["無効"] = ("Disabled", "已禁用"),
        ["有効"] = ("Enabled", "已启用"),

        // 下の帯
        ["ゲームを起動"] = ("Play", "启动游戏"),
        ["すべて更新"] = ("Update all", "全部更新"),
        ["最新の情報を取得しています..."] = ("Checking for updates...", "正在获取最新信息..."),
        ["最新の情報を取得しました"] = ("Up to date with the latest information", "已获取最新信息"),
        ["最新の情報を取得できませんでした"] = ("Couldn't get the latest information", "无法获取最新信息"),
        ["処理中は閉じられません。"] = ("You can't close the app while it is working.", "处理中无法关闭。"),

        // お知らせの帯
        ["新しい RusK Mod Manager v{0} があります"] = ("A new RusK Mod Manager v{0} is available", "有新的 RusK Mod Manager v{0}"),
        ["更新して再起動"] = ("Update and restart", "更新并重启"),
        ["VED:Recure のフォルダが見つかりません。ゲームのフォルダ (ved.exe がある場所) を選んでください"] = (
            "The VED:Recure folder wasn't found. Choose the game folder (where ved.exe is)",
            "找不到 VED:Recure 的文件夹。请选择游戏文件夹 (ved.exe 所在位置)"),
        ["フォルダを選ぶ"] = ("Choose folder", "选择文件夹"),
        ["RusK 本体が入っていません (BepInEx が無ければ一緒に入れます)"] = (
            "RusK isn't installed (BepInEx is installed too if it's missing)",
            "尚未安装 RusK 本体 (如果没有 BepInEx 会一并安装)"),
        ["RusK を入れる"] = ("Install RusK", "安装 RusK"),
        ["RusK 本体 v{0} があります (今は v{1})"] = ("RusK v{0} is available (installed: v{1})", "有 RusK 本体 v{0} (当前为 v{1})"),
        ["更新"] = ("Update", "更新"),
        ["最新の情報を取得できませんでした: {0}"] = ("Couldn't get the latest information: {0}", "无法获取最新信息: {0}"),
        ["もう一度"] = ("Retry", "重试"),
        ["ゲームが更新されています (v{0})。RusK が対応を確認したのは {1} です。動かない Mod があればゲーム内の Check で分かります"] = (
            "The game has been updated (v{0}). RusK was tested with {1}. The in-game Check shows any mods that no longer work",
            "游戏已更新 (v{0})。RusK 确认支持的版本为 {1}。如有无法运行的 Mod, 可在游戏内的 Check 中查看"),

        // 右の詳細
        ["RusK 本体: 入っていません"] = ("RusK: not installed", "RusK 本体: 未安装"),
        ["RusK 本体: v{0}"] = ("RusK: v{0}", "RusK 本体: v{0}"),
        [" (v{0} があります)"] = (" (v{0} available)", " (有 v{0})"),
        [" (最新)"] = (" (latest)", " (最新)"),
        ["BepInEx: 入っていません"] = ("BepInEx: not installed", "BepInEx: 未安装"),
        ["Mod: 有効 {0} / 無効 {1}"] = ("Mods: {0} enabled / {1} disabled", "Mod: 已启用 {0} / 已禁用 {1}"),
        ["対応ゲーム: {0}"] = ("Supported game: {0}", "支持的游戏: {0}"),
        ["お知らせ"] = ("News", "公告"),
        ["お知らせはありません"] = ("No news", "暂无公告"),
        ["左の一覧から Mod を選ぶと、説明と操作が出ます"] = (
            "Select a mod in the list to see its description and actions", "在左侧列表中选择 Mod, 即可查看说明和操作"),
        ["作者: {0}"] = ("Author: {0}", "作者: {0}"),
        ["入っている版: v{0}"] = ("Installed: v{0}", "已安装版本: v{0}"),
        ["最新: v{0}"] = ("Latest: v{0}", "最新: v{0}"),
        ["動作確認したゲーム: {0}"] = ("Tested with game: {0}", "已验证的游戏版本: {0}"),
        ["必要な Mod: {0}"] = ("Requires: {0}", "所需 Mod: {0}"),
        ["この Mod を使う Mod: {0}"] = ("Used by: {0}", "使用此 Mod 的 Mod: {0}"),
        ["この DLL には RusK の Mod の情報 ([RuskMod]) がありません。RusK では読み込まれないかもしれません"] = (
            "This DLL has no RusK mod information ([RuskMod]). RusK may not load it",
            "此 DLL 没有 RusK Mod 的信息 ([RuskMod])。RusK 可能无法加载"),
        ["手で追加した Mod です (更新は自分で DLL を入れ直してください)"] = (
            "Added by hand (to update it, add the new DLL yourself)", "手动添加的 Mod (更新时请自行重新放入 DLL)"),
        ["インストール"] = ("Install", "安装"),
        ["v{0} に更新"] = ("Update to v{0}", "更新到 v{0}"),
        ["無効にする"] = ("Disable", "禁用"),
        ["有効にする"] = ("Enable", "启用"),
        ["削除"] = ("Remove", "删除"),
        ["フォルダで表示"] = ("Show in folder", "在文件夹中显示"),
        ["リリースのページを開く"] = ("Open the release page", "打开发布页面"),

        // 操作
        ["先にゲームのフォルダを選んでください"] = ("Choose the game folder first", "请先选择游戏文件夹"),
        ["{0} に必要な {1} が見つかりません"] = ("{1}, required by {0}, wasn't found", "找不到 {0} 所需的 {1}"),
        ["RusK 本体をダウンロードできません。インターネットの接続を確かめてください"] = (
            "Can't download RusK. Check your internet connection", "无法下载 RusK 本体。请检查网络连接"),
        ["{0} を入れています..."] = ("Installing {0}...", "正在安装 {0}..."),
        ["{0} には {1} が必要なので、一緒に入れます"] = ("{0} requires {1}, so it will be installed too", "{0} 需要 {1}, 将一并安装"),
        ["{0} v{1} を入れました"] = ("Installed {0} v{1}", "已安装 {0} v{1}"),
        ["更新しています..."] = ("Updating...", "正在更新..."),
        ["{0} を更新しています ({1})..."] = ("Updating {0} ({1})...", "正在更新 {0} ({1})..."),
        ["{0} 個を更新しました"] = ("Updated {0} item(s)", "已更新 {0} 项"),
        ["RusK を入れています..."] = ("Installing RusK...", "正在安装 RusK..."),
        ["{0} は {1} が使っています。一緒に無効にしますか?"] = ("{0} uses {1}. Disable them too?", "{0} 正在使用 {1}。要一并禁用吗?"),
        ["{0} を有効にしました"] = ("Enabled {0}", "已启用 {0}"),
        ["{0} を無効にしました"] = ("Disabled {0}", "已禁用 {0}"),
        ["{0} を削除しますか?\n{1} も使えなくなるので、無効にします"] = (
            "Remove {0}?\n{1} will stop working, so it will be disabled", "要删除 {0} 吗?\n{1} 将无法使用, 因此会被禁用"),
        ["{0} を削除しますか? (設定は残ります)"] = ("Remove {0}? (Its settings are kept)", "要删除 {0} 吗? (设置会保留)"),
        ["{0} を削除しました"] = ("Removed {0}", "已删除 {0}"),
        ["RusK Mod Manager を更新しています..."] = ("Updating RusK Mod Manager...", "正在更新 RusK Mod Manager..."),
        ["RusK 本体とすべての Mod を削除しますか?\n(BepInEx・VRM などの素材は残ります)"] = (
            "Remove RusK and all mods?\n(BepInEx and assets such as VRM files are kept)",
            "要删除 RusK 本体和所有 Mod 吗?\n(BepInEx 和 VRM 等素材会保留)"),
        ["設定 (RusK\\configs・RusK\\data) も削除しますか?"] = (
            "Also remove the settings (RusK\\configs, RusK\\data)?", "也要删除设置 (RusK\\configs、RusK\\data) 吗?"),
        ["アンインストールしています..."] = ("Uninstalling...", "正在卸载..."),
        ["アンインストールが完了しました (BepInEx は残しています)"] = ("Uninstall complete (BepInEx was kept)", "卸载完成 (保留了 BepInEx)"),
        ["ゲームはもう起動しています"] = ("The game is already running", "游戏已在运行"),
        ["ゲームを起動しています... (Mod の変更はゲームを終了してから)"] = (
            "Starting the game... (close the game before changing mods)", "正在启动游戏... (更改 Mod 前请先关闭游戏)"),
        ["VED:Recure のフォルダ (ved.exe がある場所) を選んでください"] = (
            "Choose the VED:Recure folder (where ved.exe is)", "请选择 VED:Recure 的文件夹 (ved.exe 所在位置)"),
        ["このフォルダには ved.exe と GameAssembly.dll がありません"] = (
            "This folder doesn't contain ved.exe and GameAssembly.dll", "此文件夹中没有 ved.exe 和 GameAssembly.dll"),
        ["{0} には RusK の Mod の情報がありません。それでも追加しますか?"] = (
            "{0} has no RusK mod information. Add it anyway?", "{0} 没有 RusK Mod 的信息。仍要添加吗?"),
        ["{0} を追加しました"] = ("Added {0}", "已添加 {0}"),

        // はじめに
        ["RusK へようこそ"] = ("Welcome to RusK", "欢迎使用 RusK"),
        ["RusK 本体と、使う Mod をまとめてインストールします。"] = (
            "RusK and the mods you choose will be installed together.", "将一并安装 RusK 本体和所选的 Mod。"),
        ["BepInEx (Mod を動かす土台) も一緒に入れます。"] = (
            "BepInEx (the base that runs mods) will be installed too.", "也会一并安装 BepInEx (运行 Mod 的基础)。"),
        ["Mod はあとから一覧で入れたり外したりできます。"] = (
            "You can add or remove mods from the list later.", "之后也可以在列表中添加或移除 Mod。"),
        ["全部選ぶ / 全部外す"] = ("Select all / none", "全选 / 全不选"),
        ["あとで"] = ("Later", "稍后"),
        ["インストールが完了しました。「ゲームを起動」で遊べます"] = (
            "Installation complete. Press \"Play\" to start", "安装完成。点击\"启动游戏\"即可开始"),

        // ダウンロード・展開
        ["BepInEx {0} をダウンロードしています..."] = ("Downloading BepInEx {0}...", "正在下载 BepInEx {0}..."),
        ["BepInEx を展開しています..."] = ("Extracting BepInEx...", "正在解压 BepInEx..."),
        ["BepInEx を導入しました"] = ("Installed BepInEx", "已安装 BepInEx"),
        ["BepInEx を展開しましたが、IL2CPP 版の BepInEx が見つかりません。"] = (
            "BepInEx was extracted, but the IL2CPP version of BepInEx wasn't found.", "已解压 BepInEx, 但找不到 IL2CPP 版的 BepInEx。"),
        ["zip に winhttp.dll がありません。BepInEx の zip ではないようです。"] = (
            "The zip has no winhttp.dll. It doesn't look like a BepInEx zip.", "zip 中没有 winhttp.dll。这似乎不是 BepInEx 的 zip。"),
        ["  {0} ファイルを展開しました"] = ("  Extracted {0} files", "  已解压 {0} 个文件"),
        ["{0} をダウンロードしています ({1})..."] = ("Downloading {0} ({1})...", "正在下载 {0} ({1})..."),
        ["{0} をダウンロードできませんでした: {1}"] = ("Could not download {0}: {1}", "无法下载 {0}: {1}"),
        ["{0} がリリースに見つかりません"] = ("{0} was not found in the releases", "在发布中找不到 {0}"),
        ["RusK v{0} を入れました"] = ("Installed RusK v{0}", "已安装 RusK v{0}"),
        ["ダウンロードしたファイルが壊れています (SHA256 が一致しません)"] = (
            "The downloaded file is corrupted (SHA256 mismatch)", "下载的文件已损坏 (SHA256 不一致)"),
        ["ゲームが起動中です。ゲームを終了してからやり直してください。"] = (
            "The game is running. Please close it and try again.", "游戏正在运行。请关闭游戏后重试。"),
    };
}
