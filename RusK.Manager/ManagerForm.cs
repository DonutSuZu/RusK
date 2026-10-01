using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RusK.Manager;

/// <summary>
/// RusK Mod Manager の画面。起動するたびに GitHub から最新の情報 (Mod の一覧・お知らせ・リリース) を取得して、
/// RusK 本体・Mod・この Mod Manager 自身の更新、Mod の追加 (ドラッグ＆ドロップ)・有効 / 無効・削除、ゲームの起動をする
/// </summary>
internal sealed class ManagerForm : Form
{
    // 配色 (RusK のメニューに合わせたダーク)
    private static readonly Color Bg = Color.FromArgb(22, 24, 31);
    private static readonly Color Side = Color.FromArgb(16, 17, 22);
    private static readonly Color Field = Color.FromArgb(28, 31, 40);
    private static readonly Color Hover = Color.FromArgb(40, 44, 56);
    private static readonly Color TextColor = Color.FromArgb(230, 233, 240);
    private static readonly Color Dim = Color.FromArgb(138, 144, 160);
    private static readonly Color Accent = Color.FromArgb(92, 158, 255);
    private static readonly Color AccentDim = Color.FromArgb(40, 64, 104);
    private static readonly Color Good = Color.FromArgb(102, 217, 128);
    private static readonly Color Bad = Color.FromArgb(242, 102, 102);
    private static readonly Color Warn = Color.FromArgb(242, 191, 76);

    private static string FontName => Strings.Current switch { "zh" => "Microsoft YaHei UI", "en" => "Segoe UI", _ => "Yu Gothic UI" };
    private readonly Font _font = new(FontName, 9.75f);
    private readonly Font _bold = new(FontName, 9.75f, FontStyle.Bold);
    private readonly Font _title = new(FontName, 15f, FontStyle.Bold);
    private readonly Font _big = new(FontName, 12f, FontStyle.Bold);
    private readonly Font _small = new(FontName, 8.75f);

    /// <summary>表示の言語を変えたので、この言語でウィンドウを作り直す (Program が見る)</summary>
    public bool RestartForLanguage { get; private set; }

    private string _gameDir;
    private Catalog _catalog = Catalog.Builtin();
    private ReleaseClient _client = new();
    private bool _releasesOk;
    private string _onlineError;
    private bool _fetching = true;
    private ReleaseAsset _managerUpdate;
    private List<ModEntry> _entries = new();
    private ModEntry _selected;
    private bool _busy;

    private readonly ListView _list = new();
    private readonly FlowLayoutPanel _banners = new();
    private readonly FlowLayoutPanel _detail = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _launch = new();
    private readonly Button _updateAll = new();
    private readonly Label _gameLabel = new();
    private readonly List<Control> _actions = new(); // 処理中は押せなくするボタン

    public ManagerForm()
    {
        Text = "RusK Mod Manager v" + Program.Version;
        Font = _font;
        BackColor = Bg;
        ForeColor = TextColor;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1020, 660);
        MinimumSize = new Size(880, 560);
        AllowDrop = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // Dock は後から足したものが外側になるので、真ん中 → 右 → 下 → 上の順に足す
        BuildList();
        BuildDetail();
        BuildBottom();
        _banners.Dock = DockStyle.Top;
        _banners.AutoSize = true;
        _banners.FlowDirection = FlowDirection.TopDown;
        _banners.WrapContents = false;
        _banners.Padding = new Padding(16, 8, 16, 0);
        Controls.Add(_banners);
        BuildHeader();

        Resize += (_, _) => { foreach (Control b in _banners.Controls) b.Width = ClientSize.Width - 32; };
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var dir = Settings.GameDir;
        _gameDir = dir != null && GameLocator.IsGameFolder(dir) ? dir : GameLocator.FindGame();
        if (_gameDir != null) Settings.GameDir = _gameDir;

        Rescan();
        Shown += (_, _) => FetchOnline();
    }

    // ------------------------------------------------------------------ 画面

    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Side };
        Controls.Add(header);

        var logo = LoadLogo();
        if (logo != null)
            header.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(18, 14, 44, 44) });
        header.Controls.Add(new Label { Text = "RusK Mod Manager", Font = _title, AutoSize = true, Location = new Point(72, 12), ForeColor = TextColor });
        _gameLabel.AutoSize = true;
        _gameLabel.Location = new Point(75, 44);
        _gameLabel.ForeColor = Dim;
        _gameLabel.Font = _small;
        header.Controls.Add(_gameLabel);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(0, 20, 14, 0), BackColor = Side,
        };
        header.Controls.Add(right);

        var more = MakeButton("⋯", Field, (_, _) => { });
        more.Width = 40;
        var menu = new ContextMenuStrip { BackColor = Field, ForeColor = TextColor, ShowImageMargin = false, Font = _font };
        menu.Items.Add(Strings.T("ゲームのフォルダを選ぶ..."), null, (_, _) => PickGameFolder());
        menu.Items.Add(Strings.T("ゲームのフォルダを開く"), null, (_, _) => OpenFolder(_gameDir));
        menu.Items.Add(Strings.T("Mod のフォルダを開く"), null, (_, _) => OpenFolder(_gameDir == null ? null : ModLibrary.ModsDir(_gameDir)));
        menu.Items.Add(Strings.T("最新の情報を取得し直す"), null, (_, _) => FetchOnline());
        menu.Items.Add(Strings.T("GitHub のページを開く"), null, (_, _) => OpenUrl("https://github.com/" + ReleaseClient.Repo));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.T("RusK をアンインストール..."), null, (_, _) => Uninstall());
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));
        right.Controls.Add(more);

        var add = MakeButton(Strings.T("＋ DLL を追加"), Field, (_, _) => PickDll());
        _actions.Add(add);
        right.Controls.Add(add);

        var language = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, BackColor = Field, ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 6, 10, 3),
        };
        language.Items.AddRange(Strings.Names);
        language.SelectedIndex = Math.Max(0, Strings.CurrentIndex);
        language.SelectedIndexChanged += (_, _) =>
        {
            var code = Strings.Codes[language.SelectedIndex];
            if (code == Strings.Current || _busy) return;
            Strings.Current = code;
            Settings.Language = code;
            RestartForLanguage = true;
            Close();
        };
        right.Controls.Add(language);
    }

    private void BuildList()
    {
        var area = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 8, 0) };
        Controls.Add(area);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Field;
        _list.ForeColor = TextColor;
        _list.OwnerDraw = true;
        _list.AllowDrop = true;
        // 行の高さは SmallImageList の高さで決まる
        _list.SmallImageList = new ImageList { ImageSize = new Size(1, 34) };
        _list.Columns.Add("Mod", 230);
        _list.Columns.Add(Strings.T("入っている版"), 100);
        _list.Columns.Add(Strings.T("最新"), 90);
        _list.Columns.Add(Strings.T("状態"), 150);
        _list.DrawColumnHeader += (_, e) =>
        {
            using (var b = new SolidBrush(Side)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, _small, Rectangle.Inflate(e.Bounds, -8, 0), Dim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        _list.DrawItem += (_, _) => { };
        _list.DrawSubItem += DrawCell;
        _list.SelectedIndexChanged += (_, _) =>
        {
            _selected = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModEntry : null;
            ShowDetail();
        };
        // 空いている所をクリックしたら選択を外して、概要とお知らせに戻す
        _list.MouseDown += (_, e) => { if (_list.HitTest(e.Location).Item == null) _list.SelectedItems.Clear(); };
        _list.DoubleClick += (_, _) => { if (_selected != null) PrimaryAction(_selected)?.Invoke(); };
        _list.Resize += (_, _) => FitColumns();
        _list.DragEnter += OnDragEnter;
        _list.DragDrop += OnDragDrop;
        area.Controls.Add(_list);

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 30, ForeColor = Dim, Font = _small, TextAlign = ContentAlignment.MiddleLeft,
            Text = Strings.T("Mod の DLL をこのウィンドウにドラッグ＆ドロップすると追加できます"),
        };
        area.Controls.Add(hint);
    }

    private void FitColumns()
    {
        int rest = _list.ClientSize.Width - _list.Columns[1].Width - _list.Columns[2].Width - _list.Columns[3].Width;
        _list.Columns[0].Width = Math.Max(160, rest);
    }

    private void DrawCell(object sender, DrawListViewSubItemEventArgs e)
    {
        var entry = e.Item.Tag as ModEntry;
        var back = e.Item.Selected ? AccentDim : (e.ItemIndex % 2 == 0 ? Field : Color.FromArgb(31, 34, 44));
        using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
        if (entry == null) return;
        var r = Rectangle.Inflate(e.Bounds, -8, 0);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        switch (e.ColumnIndex)
        {
            case 0:
                TextRenderer.DrawText(e.Graphics, entry.Name, _bold, r, entry.Installed ? TextColor : Dim, flags);
                break;
            case 1:
                TextRenderer.DrawText(e.Graphics, entry.Installed ? "v" + entry.InstalledVersion : "—", _font, r, Dim, flags);
                break;
            case 2:
                TextRenderer.DrawText(e.Graphics, entry.Latest != null ? "v" + entry.Latest.Version : "—", _font, r, Dim, flags);
                break;
            case 3:
                var (text, color) = StatusOf(entry);
                TextRenderer.DrawText(e.Graphics, text, _font, r, color, flags);
                break;
        }
    }

    private (string, Color) StatusOf(ModEntry e)
    {
        if (!e.Installed) return (Strings.T("入手できます"), Accent);
        if (!e.IsRuskMod) return (Strings.T("RusK の Mod ではないかも"), Bad);
        if (MissingRequires(e).Any()) return (Strings.T("必要な Mod がありません"), Bad);
        if (e.UpdateAvailable) return (Strings.T("更新があります"), Warn);
        if (e.State == ModState.Disabled) return (Strings.T("無効"), Dim);
        return (Strings.T("有効"), Good);
    }

    private void BuildDetail()
    {
        var holder = new Panel { Dock = DockStyle.Right, Width = 380, Padding = new Padding(8, 12, 16, 0) };
        Controls.Add(holder);
        _detail.Dock = DockStyle.Fill;
        _detail.FlowDirection = FlowDirection.TopDown;
        _detail.WrapContents = false;
        _detail.AutoScroll = true;
        _detail.BackColor = Field;
        _detail.Padding = new Padding(18, 16, 18, 16);
        holder.Controls.Add(_detail);
        _detail.Resize += (_, _) => { foreach (Control c in _detail.Controls) c.Width = DetailWidth; };
    }

    private int DetailWidth => Math.Max(200, _detail.ClientSize.Width - _detail.Padding.Horizontal - 4);

    private void BuildBottom()
    {
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Side, Padding = new Padding(16, 0, 16, 0) };
        Controls.Add(bottom);

        _launch.Text = "▶  " + Strings.T("ゲームを起動");
        StyleButton(_launch, Accent);
        _launch.Font = _big;
        _launch.ForeColor = Color.White;
        _launch.Size = new Size(200, 48);
        _launch.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _launch.Location = new Point(bottom.Width - 216, 14);
        _launch.Click += (_, _) => LaunchGame();
        bottom.Controls.Add(_launch);

        _updateAll.Text = Strings.T("すべて更新");
        StyleButton(_updateAll, Field);
        _updateAll.Size = new Size(140, 48);
        _updateAll.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _updateAll.Location = new Point(bottom.Width - 216 - 152, 14);
        _updateAll.Click += (_, _) => UpdateAll();
        _actions.Add(_updateAll);
        bottom.Controls.Add(_updateAll);

        _status.AutoSize = false;
        _status.Location = new Point(16, 14);
        _status.Size = new Size(bottom.Width - 420, 24);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _status.ForeColor = Dim;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        bottom.Controls.Add(_status);

        _progress.Location = new Point(18, 44);
        _progress.Size = new Size(bottom.Width - 424, 10);
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _progress.Visible = false;
        bottom.Controls.Add(_progress);
    }

    // ------------------------------------------------------------------ 状態の更新

    /// <summary>最新の情報 (catalog.json とリリースの一覧) を取得する</summary>
    private void FetchOnline()
    {
        if (_busy) return;
        _fetching = true;
        SetStatus(Strings.T("最新の情報を取得しています..."));
        RefreshBanners();
        Task.Run(() =>
        {
            var client = new ReleaseClient();
            Catalog catalog = null;
            string error = null;
            bool releases = false;
            try
            {
                client.Fetch();
                releases = true;
            }
            catch (Exception e) { error = e.Message; }
            try
            {
                catalog = Catalog.Parse(client.GetText(Catalog.Url));
            }
            catch (Exception e) { error ??= e.Message; }
            return (client, catalog, releases, error);
        }).ContinueWith(t => OnUi(() =>
        {
            var (client, catalog, releases, error) = t.Result;
            _client = client;
            _releasesOk = releases;
            _onlineError = releases ? null : error;
            _catalog = catalog ?? Catalog.Builtin();
            _catalog.Online = releases;
            _managerUpdate = releases ? SelfUpdater.Check(client, _catalog) : null;
            _fetching = false;
            SetStatus(releases ? Strings.T("最新の情報を取得しました") : Strings.T("最新の情報を取得できませんでした"));
            Rescan();
        }));
    }

    private ReleaseAsset LatestOf(string asset) => _releasesOk ? _client.Latest(asset) : null;
    private ReleaseAsset CoreLatest => LatestOf(_catalog.CoreAsset);
    private string CoreInstalled => _gameDir == null ? null : GameLocator.RuskVersion(_gameDir);
    private bool CoreUpdate => CoreInstalled != null && CoreLatest != null &&
                               ReleaseClient.CompareVersions(CoreLatest.Version, CoreInstalled) > 0;

    /// <summary>ゲームフォルダを見直して、一覧・バナー・詳細を作り直す</summary>
    private void Rescan()
    {
        var keep = _selected?.FileName;
        try { _entries = ModLibrary.Scan(_gameDir, _catalog, LatestOf); }
        catch (Exception e) { _entries = new List<ModEntry>(); SetStatus(e.Message); }

        // 並び: 入っているもの (カタログの順 → 手で入れたもの) → 入手できるもの
        int Order(ModEntry e) => e.Catalog != null ? _catalog.Mods.IndexOf(e.Catalog) : 1000;
        _entries = _entries.OrderBy(e => e.Installed ? 0 : 1).ThenBy(Order).ThenBy(e => e.Name).ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _entries)
        {
            var item = new ListViewItem(new[] { e.Name, "", "", "" }) { Tag = e };
            _list.Items.Add(item);
            if (keep != null && string.Equals(e.FileName, keep, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        _list.EndUpdate();
        FitColumns();
        _selected = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModEntry : null;

        _gameLabel.Text = _gameDir == null
            ? Strings.T("ゲームのフォルダが見つかりません")
            : Strings.T("ゲーム: {0}", _gameDir) + (GameLocator.GameBuildVersion(_gameDir) is { } v ? $"  (v{v})" : "");
        RefreshBanners();
        ShowDetail();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _actions.RemoveAll(c => c.IsDisposed);
        foreach (var c in _actions) c.Enabled = !_busy;
        _launch.Enabled = !_busy && _gameDir != null;
        _updateAll.Enabled = !_busy && (CoreUpdate || _entries.Any(e => e.UpdateAvailable));
    }

    private void RefreshBanners()
    {
        _banners.SuspendLayout();
        foreach (Control c in _banners.Controls.Cast<Control>().ToList()) c.Dispose();
        _banners.Controls.Clear();

        if (_managerUpdate != null)
            AddBanner(Strings.T("新しい RusK Mod Manager v{0} があります", _managerUpdate.Version), Accent,
                Strings.T("更新して再起動"), UpdateSelf);
        if (_gameDir == null)
            AddBanner(Strings.T("VED:Recure のフォルダが見つかりません。ゲームのフォルダ (ved.exe がある場所) を選んでください"), Bad,
                Strings.T("フォルダを選ぶ"), PickGameFolder);
        else if (CoreInstalled == null)
            AddBanner(Strings.T("RusK 本体が入っていません (BepInEx が無ければ一緒に入れます)"), Warn,
                Strings.T("RusK を入れる"), () => InstallCore(null));
        else if (CoreUpdate)
            AddBanner(Strings.T("RusK 本体 v{0} があります (今は v{1})", CoreLatest.Version, CoreInstalled), Warn,
                Strings.T("更新"), () => InstallCore(null));
        if (!_fetching && !_releasesOk)
            AddBanner(Strings.T("最新の情報を取得できませんでした: {0}", _onlineError ?? "?"), Bad, Strings.T("もう一度"), FetchOnline);
        if (_gameDir != null && _releasesOk && !string.IsNullOrEmpty(_catalog.GameVersion) &&
            GameLocator.GameBuildVersion(_gameDir) is { } game && !_catalog.GameVersion.StartsWith(game))
            AddBanner(Strings.T("ゲームが更新されています (v{0})。RusK が対応を確認したのは {1} です。動かない Mod があればゲーム内の Check で分かります", game, _catalog.GameVersion),
                Dim, null, null);

        _banners.ResumeLayout();
    }

    private void AddBanner(string text, Color color, string button, Action action)
    {
        var p = new Panel { Height = 40, Width = ClientSize.Width - 32, BackColor = Field, Margin = new Padding(0, 0, 0, 6) };
        p.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = color });
        var label = new Label
        {
            Text = text, AutoEllipsis = true, ForeColor = TextColor, TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(14, 0), Size = new Size(p.Width - (button != null ? 190 : 24), 40),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        p.Controls.Add(label);
        if (button != null)
        {
            var b = MakeButton(button, color == Dim ? Hover : color, (_, _) => action());
            if (color != Field && color != Dim) b.ForeColor = Color.FromArgb(16, 17, 22);
            b.Size = new Size(160, 30);
            b.Location = new Point(p.Width - 168, 5);
            b.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _actions.Add(b);
            b.Enabled = !_busy;
            p.Controls.Add(b);
        }
        _banners.Controls.Add(p);
    }

    // ------------------------------------------------------------------ 詳細

    private void ShowDetail()
    {
        _detail.SuspendLayout();
        foreach (Control c in _detail.Controls.Cast<Control>().ToList())
        {
            _actions.Remove(c);
            c.Dispose();
        }
        _detail.Controls.Clear();
        if (_selected == null) ShowOverview();
        else ShowMod(_selected);
        foreach (Control c in _detail.Controls) c.Width = DetailWidth;
        _detail.ResumeLayout();
        UpdateButtons();
    }

    private void ShowOverview()
    {
        AddText("RusK", _big, TextColor);
        if (_gameDir != null)
        {
            var core = CoreInstalled;
            AddText(core == null ? Strings.T("RusK 本体: 入っていません") : Strings.T("RusK 本体: v{0}", core) +
                    (CoreLatest != null ? (CoreUpdate ? Strings.T(" (v{0} があります)", CoreLatest.Version) : Strings.T(" (最新)")) : ""),
                _font, core == null ? Warn : CoreUpdate ? Warn : Good);
            var bep = GameLocator.HasBepInEx(_gameDir) ? GameLocator.BepInExVersion(_gameDir) : null;
            AddText(bep == null ? Strings.T("BepInEx: 入っていません") : "BepInEx: " + bep, _font, bep == null ? Warn : Dim);
            int on = _entries.Count(e => e.State == ModState.Enabled), off = _entries.Count(e => e.State == ModState.Disabled);
            AddText(Strings.T("Mod: 有効 {0} / 無効 {1}", on, off), _font, Dim);
        }
        if (!string.IsNullOrEmpty(_catalog.GameVersion))
            AddText(Strings.T("対応ゲーム: {0}", _catalog.GameVersion), _font, Dim);
        AddGap(10);

        AddText(Strings.T("お知らせ"), _big, TextColor);
        if (_catalog.News.Count == 0) AddText(Strings.T("お知らせはありません"), _font, Dim);
        foreach (var n in _catalog.News.Take(8))
        {
            AddText(n.Date, _small, Accent);
            AddText(n.LocalText, _font, TextColor);
            AddGap(4);
        }
        AddGap(6);
        AddText(Strings.T("左の一覧から Mod を選ぶと、説明と操作が出ます"), _small, Dim);
    }

    private void ShowMod(ModEntry e)
    {
        AddText(e.Name, _big, TextColor);
        var sub = new List<string> { e.FileName };
        if (!string.IsNullOrEmpty(e.Info?.Author) && e.Info.Author != "you") sub.Add(Strings.T("作者: {0}", e.Info.Author));
        AddText(string.Join("  ·  ", sub), _small, Dim);
        var (status, color) = StatusOf(e);
        AddText(status, _bold, color);
        AddGap(4);

        if (e.Installed) AddText(Strings.T("入っている版: v{0}", e.InstalledVersion), _font, TextColor);
        if (e.Latest != null) AddText(Strings.T("最新: v{0}", e.Latest.Version), _font, e.UpdateAvailable ? Warn : Dim);
        if (!string.IsNullOrEmpty(e.Info?.GameVersion)) AddText(Strings.T("動作確認したゲーム: {0}", e.Info.GameVersion), _font, Dim);
        if (e.Requires.Length > 0)
        {
            var missing = MissingRequires(e).ToList();
            AddText(Strings.T("必要な Mod: {0}", string.Join(", ", e.Requires.Select(NameOf))), _font, missing.Count > 0 ? Bad : Dim);
        }
        var users = Dependents(e, enabledOnly: false).ToList();
        if (users.Count > 0) AddText(Strings.T("この Mod を使う Mod: {0}", string.Join(", ", users.Select(u => u.Name))), _font, Dim);
        AddGap(8);

        var desc = e.Description;
        if (!string.IsNullOrEmpty(desc)) AddText(desc, _font, TextColor);
        if (e.Installed && !e.IsRuskMod)
            AddText(Strings.T("この DLL には RusK の Mod の情報 ([RuskMod]) がありません。RusK では読み込まれないかもしれません"), _font, Bad);
        if (e.Installed && e.Catalog == null && e.IsRuskMod)
            AddText(Strings.T("手で追加した Mod です (更新は自分で DLL を入れ直してください)"), _small, Dim);
        AddGap(12);

        // 操作
        if (PrimaryAction(e) is { } primary)
            AddAction(PrimaryLabel(e), Accent, primary, Color.White);
        if (e.Installed)
        {
            if (e.State == ModState.Enabled) AddAction(Strings.T("無効にする"), Hover, () => SetEnabled(e, false));
            else AddAction(Strings.T("有効にする"), Hover, () => SetEnabled(e, true));
            AddAction(Strings.T("削除"), Hover, () => Remove(e), Bad);
            AddAction(Strings.T("フォルダで表示"), Field, () =>
                Process.Start("explorer.exe", "/select,\"" + e.LocalPath + "\""), Dim, busySafe: true);
        }
        if (e.Latest?.ReleaseUrl != null)
            AddAction(Strings.T("リリースのページを開く"), Field, () => OpenUrl(e.Latest.ReleaseUrl), Dim, busySafe: true);
    }

    /// <summary>いちばん使いそうな操作 (入れる / 更新 / 有効にする)。ダブルクリックでも同じ</summary>
    private Action PrimaryAction(ModEntry e)
    {
        if (!e.Installed && e.Latest != null) return () => Install(e);
        if (e.UpdateAvailable) return () => Install(e);
        if (e.State == ModState.Disabled) return () => SetEnabled(e, true);
        return null;
    }

    private string PrimaryLabel(ModEntry e) =>
        !e.Installed ? Strings.T("インストール") :
        e.UpdateAvailable ? Strings.T("v{0} に更新", e.Latest.Version) : Strings.T("有効にする");

    private void AddText(string text, Font font, Color color)
    {
        _detail.Controls.Add(new Label
        {
            Text = text, Font = font, ForeColor = color, AutoSize = true, UseMnemonic = false,
            MaximumSize = new Size(DetailWidth, 0), Width = DetailWidth, Margin = new Padding(0, 2, 0, 2),
        });
    }

    private void AddGap(int h) => _detail.Controls.Add(new Panel { Height = h, Width = DetailWidth, Margin = Padding.Empty });

    private void AddAction(string text, Color back, Action action, Color? fore = null, bool busySafe = false)
    {
        var b = MakeButton(text, back, (_, _) => action());
        b.Height = 36;
        b.Width = DetailWidth;
        b.Margin = new Padding(0, 0, 0, 8);
        if (fore != null) b.ForeColor = fore.Value;
        if (!busySafe) _actions.Add(b);
        _detail.Controls.Add(b);
    }

    // ------------------------------------------------------------------ 依存

    private string NameOf(string id) =>
        _entries.FirstOrDefault(x => x.Id == id)?.Name ?? _catalog.Find(id)?.Name ?? id;

    private IEnumerable<string> MissingRequires(ModEntry e) =>
        !e.Installed ? Enumerable.Empty<string>()
            : e.Requires.Where(id => !_entries.Any(x => x.Id == id && x.State == ModState.Enabled));

    private IEnumerable<ModEntry> Dependents(ModEntry e, bool enabledOnly) =>
        _entries.Where(x => x.Installed && (!enabledOnly || x.State == ModState.Enabled) && x.Requires.Contains(e.Id));

    /// <summary>e を使うのに必要な Mod (まだ入っていない / 無効のもの) を、必要な順に</summary>
    private List<ModEntry> NeededFor(ModEntry e)
    {
        var result = new List<ModEntry>();
        void Visit(ModEntry m, int depth)
        {
            if (depth > 8) return;
            foreach (var id in m.Requires)
            {
                var r = _entries.FirstOrDefault(x => x.Id == id);
                if (r == null || r.State == ModState.Enabled || result.Contains(r)) continue;
                Visit(r, depth + 1);
                result.Add(r);
            }
        }
        Visit(e, 0);
        return result;
    }

    // ------------------------------------------------------------------ 操作

    private bool CheckGameDir()
    {
        if (_gameDir != null) return true;
        Message(Strings.T("先にゲームのフォルダを選んでください"), MessageBoxIcon.Warning);
        return false;
    }

    private void Install(ModEntry e)
    {
        if (!CheckGameDir()) return;
        var needed = NeededFor(e);
        var missing = needed.Where(n => !n.Installed && n.Latest == null).ToList();
        if (missing.Count > 0)
        {
            Message(Strings.T("{0} に必要な {1} が見つかりません", e.Name, string.Join(", ", missing.Select(m => m.Name))), MessageBoxIcon.Warning);
            return;
        }
        bool needCore = CoreInstalled == null;
        if (needCore && CoreLatest == null)
        {
            Message(Strings.T("RusK 本体をダウンロードできません。インターネットの接続を確かめてください"), MessageBoxIcon.Warning);
            return;
        }
        Run(Strings.T("{0} を入れています...", e.Name), progress =>
        {
            if (needCore) InstallEngine.InstallCore(_gameDir, _client, CoreLatest, SetStatusAsync, progress);
            foreach (var n in needed)
            {
                if (n.Installed) ModLibrary.SetEnabled(_gameDir, n, true);
                else
                {
                    SetStatusAsync(Strings.T("{0} には {1} が必要なので、一緒に入れます", e.Name, n.Name));
                    ModLibrary.Download(_gameDir, n, _client, progress);
                }
            }
            ModLibrary.Download(_gameDir, e, _client, progress);
            SetStatusAsync(Strings.T("{0} v{1} を入れました", e.Name, e.Latest.Version));
        });
    }

    private void UpdateAll()
    {
        if (!CheckGameDir()) return;
        var mods = _entries.Where(e => e.UpdateAvailable).ToList();
        bool core = CoreUpdate;
        var coreAsset = CoreLatest;
        Run(Strings.T("更新しています..."), progress =>
        {
            if (core) InstallEngine.InstallCore(_gameDir, _client, coreAsset, SetStatusAsync, progress);
            foreach (var e in mods)
            {
                SetStatusAsync(Strings.T("{0} を更新しています ({1})...", e.Name, "v" + e.Latest.Version));
                ModLibrary.Download(_gameDir, e, _client, progress);
            }
            SetStatusAsync(Strings.T("{0} 個を更新しました", mods.Count + (core ? 1 : 0)));
        });
    }

    private void InstallCore(Action after)
    {
        if (!CheckGameDir()) return;
        var asset = CoreLatest;
        if (asset == null)
        {
            Message(Strings.T("RusK 本体をダウンロードできません。インターネットの接続を確かめてください"), MessageBoxIcon.Warning);
            return;
        }
        Run(Strings.T("RusK を入れています..."), progress => InstallEngine.InstallCore(_gameDir, _client, asset, SetStatusAsync, progress), after);
    }

    private void SetEnabled(ModEntry e, bool enabled)
    {
        if (!CheckGameDir()) return;
        var also = new List<ModEntry>();
        if (enabled) also = NeededFor(e).Where(n => n.Installed).ToList();
        else
        {
            also = Dependents(e, enabledOnly: true).ToList();
            if (also.Count > 0 && Ask(Strings.T("{0} は {1} が使っています。一緒に無効にしますか?", string.Join(", ", also.Select(a => a.Name)), e.Name)) != DialogResult.Yes)
                return;
        }
        Run(null, _ =>
        {
            foreach (var a in also) ModLibrary.SetEnabled(_gameDir, a, enabled);
            ModLibrary.SetEnabled(_gameDir, e, enabled);
            SetStatusAsync(enabled ? Strings.T("{0} を有効にしました", e.Name) : Strings.T("{0} を無効にしました", e.Name));
        });
    }

    private void Remove(ModEntry e)
    {
        if (!CheckGameDir()) return;
        var users = Dependents(e, enabledOnly: true).ToList();
        var question = users.Count > 0
            ? Strings.T("{0} を削除しますか?\n{1} も使えなくなるので、無効にします", e.Name, string.Join(", ", users.Select(u => u.Name)))
            : Strings.T("{0} を削除しますか? (設定は残ります)", e.Name);
        if (Ask(question) != DialogResult.Yes) return;
        Run(null, _ =>
        {
            foreach (var u in users) ModLibrary.SetEnabled(_gameDir, u, false);
            ModLibrary.Remove(_gameDir, e);
            SetStatusAsync(Strings.T("{0} を削除しました", e.Name));
        });
        _selected = null;
    }

    private void UpdateSelf()
    {
        var asset = _managerUpdate;
        if (asset == null) return;
        Run(Strings.T("RusK Mod Manager を更新しています..."), progress => SelfUpdater.Apply(_client, asset, progress), () =>
        {
            if (!File.Exists(Application.ExecutablePath + ".old")) return; // 失敗した
            Close();
        });
    }

    private void Uninstall()
    {
        if (!CheckGameDir() || _busy) return;
        if (Ask(Strings.T("RusK 本体とすべての Mod を削除しますか?\n(BepInEx・VRM などの素材は残ります)")) != DialogResult.Yes) return;
        bool data = Ask(Strings.T("設定 (RusK\\configs・RusK\\data) も削除しますか?")) == DialogResult.Yes;
        Run(Strings.T("アンインストールしています..."), _ =>
            InstallEngine.Uninstall(new UninstallOptions { GameDir = _gameDir, RemoveData = data }, SetStatusAsync));
    }

    private void LaunchGame()
    {
        if (!CheckGameDir()) return;
        if (GameLocator.GameRunning(_gameDir))
        {
            SetStatus(Strings.T("ゲームはもう起動しています"));
            return;
        }
        try
        {
            // Steam のゲームなので Steam から起動する (Steam が無いときは exe を直接)
            Process.Start(new ProcessStartInfo("steam://rungameid/" + GameLocator.SteamAppId) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(_gameDir, GameLocator.GameExe)) { WorkingDirectory = _gameDir, UseShellExecute = true });
            }
            catch (Exception e)
            {
                Message(e.Message, MessageBoxIcon.Error);
                return;
            }
        }
        SetStatus(Strings.T("ゲームを起動しています... (Mod の変更はゲームを終了してから)"));
    }

    private void PickGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Strings.T("VED:Recure のフォルダ (ved.exe がある場所) を選んでください"),
            SelectedPath = _gameDir ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!GameLocator.IsGameFolder(dialog.SelectedPath))
        {
            Message(Strings.T("このフォルダには ved.exe と GameAssembly.dll がありません"), MessageBoxIcon.Warning);
            return;
        }
        _gameDir = dialog.SelectedPath;
        Settings.GameDir = _gameDir;
        Rescan();
    }

    // ------------------------------------------------------------------ DLL の追加

    private void PickDll()
    {
        using var dialog = new OpenFileDialog { Filter = "Mod (*.dll)|*.dll", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) Import(dialog.FileNames);
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effect = !_busy && e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                   files.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Import(files);
    }

    private void Import(IEnumerable<string> files)
    {
        if (!CheckGameDir() || _busy) return;
        var dlls = files.Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(f)).ToList();
        var plan = new List<(string path, ModDllInfo info)>();
        foreach (var f in dlls)
        {
            var info = ModInfoReader.Read(f);
            if (info == null &&
                Ask(Strings.T("{0} には RusK の Mod の情報がありません。それでも追加しますか?", Path.GetFileName(f))) != DialogResult.Yes)
                continue;
            plan.Add((f, info));
        }
        if (plan.Count == 0) return;
        string last = null;
        Run(null, _ =>
        {
            foreach (var (path, info) in plan)
            {
                last = ModLibrary.Import(_gameDir, path, info, _entries);
                SetStatusAsync(Strings.T("{0} を追加しました", info?.Name ?? Path.GetFileName(path)));
            }
        }, () =>
        {
            // 追加したものを選んでおく
            foreach (ListViewItem item in _list.Items)
                if (item.Tag is ModEntry m && string.Equals(m.FileName, last, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.EnsureVisible();
                }
        });
    }

    // ------------------------------------------------------------------ 共通

    /// <summary>重い処理を裏で動かす。終わったら一覧を作り直して after を呼ぶ</summary>
    private void Run(string status, Action<Action<int>> work, Action after = null)
    {
        if (_busy) return;
        _busy = true;
        if (status != null) SetStatus(status);
        _progress.Value = 0;
        _progress.Visible = true;
        UpdateButtons();
        Task.Run(() =>
        {
            try
            {
                work(p => OnUi(() => _progress.Value = Math.Max(0, Math.Min(100, p))));
                return null;
            }
            catch (Exception e) { return e; }
        }).ContinueWith(t => OnUi(() =>
        {
            _busy = false;
            _progress.Visible = false;
            if (t.Result != null)
            {
                SetStatus(t.Result.Message);
                Message(t.Result.Message, MessageBoxIcon.Error);
            }
            Rescan();
            if (t.Result == null) after?.Invoke();
        }));
    }

    private void OnUi(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
    }

    private void SetStatus(string text) => _status.Text = text;
    private void SetStatusAsync(string text) => OnUi(() => SetStatus(text));

    private void Message(string text, MessageBoxIcon icon) =>
        MessageBox.Show(this, text, "RusK Mod Manager", MessageBoxButtons.OK, icon);

    private DialogResult Ask(string text) =>
        MessageBox.Show(this, text, "RusK Mod Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private Button MakeButton(string text, Color back, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 32), Padding = new Padding(10, 0, 10, 0), Margin = new Padding(3, 3, 3, 3) };
        StyleButton(b, back);
        b.Click += click;
        return b;
    }

    private void StyleButton(Button b, Color back)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
        b.BackColor = back;
        b.ForeColor = TextColor;
        b.Cursor = Cursors.Hand;
        b.UseMnemonic = false;
    }

    private void OpenFolder(string dir)
    {
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", "\"" + dir + "\"");
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static Image LoadLogo()
    {
        try
        {
            using var s = typeof(ManagerForm).Assembly.GetManifestResourceStream("icon.png");
            return s == null ? null : new Bitmap(Image.FromStream(s));
        }
        catch { return null; }
    }

    // タイトルバーも暗くする (Windows 10 20H1 以降。古い Windows では何も起きない)
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4);
        }
        catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy && !RestartForLanguage)
        {
            e.Cancel = true;
            SetStatus(Strings.T("処理中は閉じられません。"));
            return;
        }
        base.OnFormClosing(e);
    }
}
