using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using RusK.Manager;

namespace RusK.PackCreator;

/// <summary>
/// キャラの Mod Pack を作る画面。上から「パック・見た目・武器・動き・声・絵」の欄を埋め、
/// 「ゲームに入れる」で RusK\characters\〈フォルダ名〉 にファイルをコピーして character.json を作る。「zip で書き出す」で配る形にする
/// </summary>
internal sealed class PackForm : Form
{
    private static readonly Color Bg = Color.FromArgb(22, 24, 31);
    private static readonly Color Side = Color.FromArgb(16, 17, 22);
    private static readonly Color Field = Color.FromArgb(28, 31, 40);
    private static readonly Color TextColor = Color.FromArgb(230, 233, 240);
    private static readonly Color Dim = Color.FromArgb(138, 144, 160);
    private static readonly Color Accent = Color.FromArgb(92, 158, 255);
    private static readonly Color Good = Color.FromArgb(102, 217, 128);
    private static readonly Color Bad = Color.FromArgb(242, 102, 102);
    private static readonly Color Warn = Color.FromArgb(242, 191, 76);

    private readonly Font _font = new("Yu Gothic UI", 10f);
    private readonly Font _bold = new("Yu Gothic UI", 11f, FontStyle.Bold);
    private readonly Font _small = new("Yu Gothic UI", 9f);

    public bool RestartForLanguage { get; private set; }

    private string _game;
    private Pack _pack = new();
    private string _openedFolder;    // 開いた Pack のフォルダ (新しく作るときは null)

    private Label _gameLabel, _status;
    private TextBox _key, _nameJa, _nameEn, _nameZh, _model, _weapon;
    private NumericUpDown _id, _equip, _scale;
    private readonly NumericUpDown[] _pos = new NumericUpDown[3], _rot = new NumericUpDown[3];
    private ComboBox _base;
    private DataGridView _motions;
    private Label _voiceInfo, _imageInfo, _motionInfo;
    private DataGridView _voiceGrid, _imageGrid;
    private CheckBox _imageFit;
    private readonly Dictionary<string, string> _imageFiles = new(StringComparer.OrdinalIgnoreCase);
    private CheckBox _voiceChinese;
    private readonly Dictionary<string, string> _voiceFiles = new(StringComparer.OrdinalIgnoreCase);
    private long _voiceBase;

    public PackForm(string openFolder = null)
    {
        Text = $"RusK Mod Pack Creator  v{Program.Version}";
        BackColor = Bg;
        ForeColor = TextColor;
        Font = _font;
        Size = new Size(1000, 900);
        MinimumSize = new Size(860, 600);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

        var dir = Settings.GameDir;
        _game = dir != null && GameLocator.IsGameFolder(dir) ? dir : GameLocator.FindGame();
        if (_game != null) Settings.GameDir = _game;

        BuildUi();
        NewPack();
        if (openFolder != null) OpenFolder(openFolder);
        // Pack のフォルダをウィンドウにドラッグ＆ドロップしても開ける
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) OpenFolder(f[0]); };
    }

    // ------------------------------------------------------------------ 画面

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Side, Padding = new Padding(16, 10, 16, 0) };
        header.Controls.Add(new Label { Text = "RusK Mod Pack Creator", Font = new Font("Yu Gothic UI", 15f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 8) });
        _gameLabel = new Label { AutoSize = true, ForeColor = Dim, Font = _small, Location = new Point(18, 38) };
        header.Controls.Add(_gameLabel);
        var lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, BackColor = Field, ForeColor = TextColor, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        lang.Items.AddRange(Strings.Names);
        lang.SelectedIndex = Math.Max(0, Strings.CurrentIndex);
        lang.SelectedIndexChanged += (_, _) =>
        {
            Strings.Current = Strings.Codes[lang.SelectedIndex];
            Settings.Language = Strings.Current;
            RestartForLanguage = true;
            Close();
        };
        var gameBtn = MakeButton(Strings.T("ゲームのフォルダを選ぶ..."), ChooseGame);
        gameBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(lang);
        header.Controls.Add(gameBtn);
        header.Resize += (_, _) =>
        {
            lang.Location = new Point(header.Width - lang.Width - 16, 18);
            gameBtn.Location = new Point(lang.Left - gameBtn.Width - 10, 14);
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Side, Padding = new Padding(16, 12, 16, 12) };
        var build = MakeButton(Strings.T("ゲームに入れる"), BuildPack, true);
        var zip = MakeButton(Strings.T("zip で書き出す"), ExportZip);
        var open = MakeButton(Strings.T("Pack を開く..."), OpenPack);
        var neu = MakeButton(Strings.T("新しく作る"), NewPack);
        _status = new Label { AutoSize = false, ForeColor = Dim, Font = _small, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Side };
        flow.Controls.AddRange(new Control[] { build, zip, open, neu });
        bottom.Controls.Add(_status);
        bottom.Controls.Add(flow);

        var body = new StayFlow
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(16, 12, 16, 12), BackColor = Bg,
        };
        body.Resize += (_, _) => { foreach (Control c in body.Controls) c.MinimumSize = new Size(Math.Max(200, body.ClientSize.Width - 40), 0); };

        body.Controls.Add(PackSection());
        body.Controls.Add(ModelSection());
        body.Controls.Add(WeaponSection());
        body.Controls.Add(MotionSection());
        body.Controls.Add(VoiceSection());
        body.Controls.Add(ImageSection());

        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(header);
        UpdateGameLabel();
    }

    private Control Section(string title, string hint, out TableLayoutPanel grid)
    {
        // 欄そのものを、中身に合わせて伸びる縦の並びにする (幅は本文の幅に合わせて最低限の幅を決める)
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Field, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 12),
        };
        stack.Controls.Add(new Label { Text = title, Font = _bold, AutoSize = true, ForeColor = Accent, Margin = new Padding(0, 0, 0, 2) });
        if (hint != null) stack.Controls.Add(new Label { Text = hint, Font = _small, ForeColor = Dim, AutoSize = true, MaximumSize = new Size(880, 0), Margin = new Padding(0, 0, 0, 6) });
        grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Field, Margin = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        stack.Controls.Add(grid);
        return stack;
    }

    private void Row(TableLayoutPanel grid, string label, Control control)
    {
        grid.RowCount++;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Dim, Margin = new Padding(0, 7, 8, 0) });
        grid.Controls.Add(control);
    }

    private TextBox Box(int width = 300) =>
        new() { Width = width, BackColor = Bg, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 3) };

    private NumericUpDown Num(decimal min, decimal max, int decimals = 0, decimal step = 1) =>
        new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step, Width = 90, BackColor = Bg, ForeColor = TextColor, Margin = new Padding(0, 3, 6, 3) };

    private Button MakeButton(string text, Action click, bool primary = false)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(6, 0, 0, 0),
            BackColor = primary ? Accent : Field, ForeColor = primary ? Color.White : TextColor,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(60, 66, 82);
        b.Click += (_, _) =>
        {
            try { click(); }
            catch (Exception e) { SetStatus(e.Message, Bad); }
        };
        return b;
    }

    /// <summary>テキスト欄 + 「選ぶ...」 (+ 「外す」)</summary>
    private Control FilePicker(TextBox box, Func<string> pick, Action changed = null)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        box.Width = 520;
        box.ReadOnly = true;
        row.Controls.Add(box);
        row.Controls.Add(MakeButton(Strings.T("選ぶ..."), () => { var p = pick(); if (p != null) { box.Text = p; changed?.Invoke(); } }));
        row.Controls.Add(MakeButton(Strings.T("外す"), () => { box.Text = ""; changed?.Invoke(); }));
        return row;
    }

    private string PickFile(string filter)
    {
        using var d = new OpenFileDialog { Filter = filter };
        return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
    }

    private string PickFolder(string description)
    {
        using var d = new FolderBrowserDialog { Description = description };
        return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
    }

    // ---- 欄

    private Control PackSection()
    {
        var p = Section(Strings.T("1. パック"), Strings.T("新しいキャラの枠です。動作・能力値・攻撃の判定は「土台のキャラ」を複製します。"), out var g);
        _key = Box(240);
        Row(g, Strings.T("フォルダ名 (英数字)"), _key);
        _id = Num(9000, 99999);
        Row(g, Strings.T("キャラ番号"), _id);
        _base = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, BackColor = Bg, ForeColor = TextColor, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 3, 0, 3) };
        foreach (var b in BaseChara.All) _base.Items.Add(b);
        _base.SelectedIndexChanged += (_, _) => { OnBaseChanged(); };
        Row(g, Strings.T("土台のキャラ"), _base);
        _nameJa = Box(240); _nameEn = Box(240); _nameZh = Box(240);
        Row(g, Strings.T("名前 (日本語)"), _nameJa);
        Row(g, Strings.T("名前 (英語)"), _nameEn);
        Row(g, Strings.T("名前 (中国語)"), _nameZh);
        return p;
    }

    private Control ModelSection()
    {
        var p = Section(Strings.T("2. 見た目"), Strings.T("VRM か PMX (MMD)。PMX はテクスチャの入ったフォルダごとコピーします。無ければ土台のキャラの見た目のままです。"), out var g);
        _model = Box();
        Row(g, Strings.T("モデル"), FilePicker(_model, () => PickFile("VRM / PMX|*.vrm;*.pmx")));
        return p;
    }

    private Control WeaponSection()
    {
        var p = Section(Strings.T("3. 武器"), Strings.T("glb か PMX。新しいキャラが「置き換える装備」を持つときだけ置き換えます (土台のキャラの武器は変わりません)。位置・回転・大きさは、ゲームで見ながら合わせてください。"), out var g);
        _weapon = Box();
        Row(g, Strings.T("武器"), FilePicker(_weapon, () => PickFile("glb / PMX|*.glb;*.pmx")));
        _equip = Num(0, 99999);
        Row(g, Strings.T("置き換える装備の番号"), _equip);
        _scale = Num(0.01m, 100, 2, 0.05m);
        Row(g, Strings.T("大きさ"), _scale);
        Row(g, Strings.T("回転 (度) X / Y / Z"), Triple(_rot, -360, 360, 0, 15));
        Row(g, Strings.T("位置 X / Y / Z"), Triple(_pos, -10, 10, 3, 0.01m));
        return p;
    }

    private Control Triple(NumericUpDown[] boxes, decimal min, decimal max, int dec, decimal step)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        for (int i = 0; i < 3; i++) { boxes[i] = Num(min, max, dec, step); row.Controls.Add(boxes[i]); }
        return row;
    }

    private Control MotionSection()
    {
        var p = Section(Strings.T("4. 動き"),
            Strings.T("動作ごとに、自作の動き (Blender で作った glb) を割り当てます。「当たる瞬間」は自分の動きで剣が当たる秒です (攻撃の動作だけ)。右の「土台」は、土台のキャラの動作の長さと攻撃判定の位置です。"), out var g);
        _motions = new DataGridView
        {
            Width = 900, Height = 240, BackgroundColor = Bg, ForeColor = TextColor, GridColor = Color.FromArgb(50, 54, 66), BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Margin = new Padding(0, 4, 0, 4),
        };
        _motions.ColumnHeadersDefaultCellStyle.BackColor = Side;
        _motions.ColumnHeadersDefaultCellStyle.ForeColor = Dim;
        _motions.DefaultCellStyle.BackColor = Bg;
        _motions.DefaultCellStyle.ForeColor = TextColor;
        _motions.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 64, 104);
        _motions.Columns.Add(new DataGridViewComboBoxColumn { Name = "action", HeaderText = Strings.T("動作"), FillWeight = 26, FlatStyle = FlatStyle.Flat });
        _motions.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = Strings.T("動きのファイル (ダブルクリックで選ぶ)"), FillWeight = 40, ReadOnly = true });
        _motions.Columns.Add(new DataGridViewTextBoxColumn { Name = "anim", HeaderText = Strings.T("アニメーションの名前 (省略可)"), FillWeight = 20 });
        _motions.Columns.Add(new DataGridViewTextBoxColumn { Name = "hit", HeaderText = Strings.T("当たる瞬間 (秒)"), FillWeight = 12 });
        _motions.Columns.Add(new DataGridViewTextBoxColumn { Name = "info", HeaderText = Strings.T("土台"), FillWeight = 20, ReadOnly = true });
        _motions.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _motions.Columns[e.ColumnIndex].Name != "file") return;
            var f = PickFile("glb|*.glb");
            if (f != null) _motions.Rows[e.RowIndex].Cells["file"].Value = f;
        };
        _motions.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && _motions.Columns[e.ColumnIndex].Name == "action") UpdateMotionInfo(_motions.Rows[e.RowIndex]); };
        _motions.CurrentCellDirtyStateChanged += (_, _) => { if (_motions.IsCurrentCellDirty) _motions.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _motions.DataError += (_, e) => e.ThrowException = false;
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        buttons.Controls.Add(MakeButton(Strings.T("＋ 動作を足す"), () => AddMotionRow(null)));
        buttons.Controls.Add(MakeButton(Strings.T("＋ 通常攻撃 5 段を足す"), AddCombo));
        buttons.Controls.Add(MakeButton(Strings.T("選んだ行を消す"), () => { foreach (DataGridViewRow r in _motions.SelectedRows) _motions.Rows.Remove(r); }));
        _motionInfo = new Label { AutoSize = true, ForeColor = Dim, Font = _small, Margin = new Padding(12, 8, 0, 0) };
        buttons.Controls.Add(_motionInfo);
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        stack.Controls.Add(_motions);
        stack.Controls.Add(buttons);
        g.ColumnStyles[0].Width = 0;
        g.RowCount++;
        g.Controls.Add(new Label { Width = 0, Margin = new Padding(0) });
        g.Controls.Add(stack);
        return p;
    }

    private Control VoiceSection()
    {
        var p = Section(Strings.T("5. 声"),
            Strings.T("要る声ごとに、好きな音声ファイル (ogg / wav / mp3、名前は何でもよい) を選びます。「ゲームに入れる」で、ゲームが使う名前に変えてコピーします。新しいキャラが場にいるときだけ使われ、選ばなかった声は土台のキャラの声のままです。"), out var g);
        _voiceGrid = new DataGridView
        {
            Width = 900, Height = 300, BackgroundColor = Bg, ForeColor = TextColor, GridColor = Color.FromArgb(50, 54, 66), BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Margin = new Padding(0, 4, 0, 4), ReadOnly = true,
        };
        _voiceGrid.ColumnHeadersDefaultCellStyle.BackColor = Side;
        _voiceGrid.ColumnHeadersDefaultCellStyle.ForeColor = Dim;
        _voiceGrid.DefaultCellStyle.BackColor = Bg;
        _voiceGrid.DefaultCellStyle.ForeColor = TextColor;
        _voiceGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 64, 104);
        _voiceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "when", HeaderText = Strings.T("鳴るとき"), FillWeight = 22 });
        _voiceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = Strings.T("ゲームが使う名前"), FillWeight = 34 });
        _voiceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = Strings.T("選んだファイル (ダブルクリックで選ぶ)"), FillWeight = 44 });
        _voiceGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ChooseVoice(new[] { _voiceGrid.Rows[e.RowIndex] }); };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        buttons.Controls.Add(MakeButton(Strings.T("選んだ行にファイルを選ぶ..."), () => ChooseVoice(_voiceGrid.SelectedRows.Cast<DataGridViewRow>().ToArray())));
        buttons.Controls.Add(MakeButton(Strings.T("選んだ行を外す"), () =>
        {
            foreach (DataGridViewRow r in _voiceGrid.SelectedRows) _voiceFiles.Remove((string)r.Cells["name"].Value);
            RefreshVoiceGrid();
        }));
        buttons.Controls.Add(MakeButton(Strings.T("フォルダからまとめて入れる..."), ImportVoiceFolder));
        _voiceChinese = new CheckBox { Text = Strings.T("中国語の声の名前 (_JP なし) でも置く"), AutoSize = true, Checked = true, ForeColor = TextColor, Margin = new Padding(12, 8, 0, 0) };
        buttons.Controls.Add(_voiceChinese);
        _voiceInfo = new Label { AutoSize = true, ForeColor = Dim, Font = _small, Margin = new Padding(0, 4, 0, 0) };
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        stack.Controls.Add(_voiceGrid);
        stack.Controls.Add(buttons);
        stack.Controls.Add(_voiceInfo);
        g.ColumnStyles[0].Width = 0;
        g.RowCount++;
        g.Controls.Add(new Label { Width = 0, Margin = new Padding(0) });
        g.Controls.Add(stack);
        return p;
    }

    private Control ImageSection()
    {
        var p = Section(Strings.T("6. 絵"),
            Strings.T("絵ごとに、好きな画像ファイル (PNG / JPG、名前は何でもよい) を選びます。「ゲームに入れる」で、ゲームが使う名前の PNG にしてコピーします。大きさが違っても、縦横の比が同じならゲームが合わせます。選ばなかった絵は土台のキャラの絵のままです。"), out var g);
        _imageGrid = new DataGridView
        {
            Width = 900, Height = 300, BackgroundColor = Bg, ForeColor = TextColor, GridColor = Color.FromArgb(50, 54, 66), BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Margin = new Padding(0, 4, 0, 4), ReadOnly = true,
        };
        _imageGrid.ColumnHeadersDefaultCellStyle.BackColor = Side;
        _imageGrid.ColumnHeadersDefaultCellStyle.ForeColor = Dim;
        _imageGrid.DefaultCellStyle.BackColor = Bg;
        _imageGrid.DefaultCellStyle.ForeColor = TextColor;
        _imageGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 64, 104);
        _imageGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "what", HeaderText = Strings.T("中身 (出る場所)"), FillWeight = 30 });
        _imageGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = Strings.T("ゲームが使う名前"), FillWeight = 18 });
        _imageGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = Strings.T("お手本の大きさ"), FillWeight = 12 });
        _imageGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = Strings.T("選んだファイル (ダブルクリックで選ぶ)"), FillWeight = 40 });
        _imageGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ChooseImage(new[] { _imageGrid.Rows[e.RowIndex] }); };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        buttons.Controls.Add(MakeButton(Strings.T("選んだ行にファイルを選ぶ..."), () => ChooseImage(_imageGrid.SelectedRows.Cast<DataGridViewRow>().ToArray())));
        buttons.Controls.Add(MakeButton(Strings.T("選んだ行を外す"), () =>
        {
            foreach (DataGridViewRow r in _imageGrid.SelectedRows) _imageFiles.Remove((string)r.Cells["name"].Value);
            RefreshImageGrid();
        }));
        buttons.Controls.Add(MakeButton(Strings.T("フォルダからまとめて入れる..."), ImportImageFolder));
        _imageFit = new CheckBox { Text = Strings.T("比が違う絵は透明の余白を足して合わせる"), AutoSize = true, Checked = true, ForeColor = TextColor, Margin = new Padding(12, 8, 0, 0) };
        _imageFit.CheckedChanged += (_, _) => RefreshImageGrid();
        buttons.Controls.Add(_imageFit);
        _imageInfo = new Label { AutoSize = true, ForeColor = Dim, Font = _small, Margin = new Padding(0, 4, 0, 0) };
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Field, Margin = new Padding(0) };
        stack.Controls.Add(_imageGrid);
        stack.Controls.Add(buttons);
        stack.Controls.Add(_imageInfo);
        g.ColumnStyles[0].Width = 0;
        g.RowCount++;
        g.Controls.Add(new Label { Width = 0, Margin = new Padding(0) });
        g.Controls.Add(stack);
        return p;
    }

    // ------------------------------------------------------------------ 動きの表

    private BaseChara CurrentBase => _base.SelectedItem as BaseChara ?? BaseChara.All.First();

    private void OnBaseChanged()
    {
        var b = CurrentBase;
        var col = (DataGridViewComboBoxColumn)_motions.Columns["action"];
        var keep = _motions.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["action"].Value as string).Where(a => a != null).ToList();
        col.Items.Clear();
        foreach (var m in b.Motions) col.Items.Add(m.name);
        foreach (var a in keep) if (!col.Items.Contains(a)) col.Items.Add(a); // 前の土台の動作も残す (警告は info に)
        foreach (DataGridViewRow r in _motions.Rows) UpdateMotionInfo(r);
        if (b.Weapon is long w && _equip.Value == 0) _equip.Value = w;
        _motionInfo.Text = Strings.T("土台のキャラの動作: {0} 個", b.Motions.Count);
        if (_voiceBase != 0 && _voiceBase != b.Id && _voiceFiles.Count > 0)
        {
            var moved = _voiceFiles.ToList();
            _voiceFiles.Clear();
            foreach (var kv in moved) _voiceFiles[kv.Key.Replace("_" + _voiceBase, "_" + b.Id)] = kv.Value;
        }
        _voiceBase = b.Id;
        RefreshVoiceGrid();
    }

    private void AddMotionRow(MotionRow m)
    {
        var col = (DataGridViewComboBoxColumn)_motions.Columns["action"];
        var action = m?.Action ?? CurrentBase.Motions.FirstOrDefault().name;
        if (action != null && !col.Items.Contains(action)) col.Items.Add(action);
        int i = _motions.Rows.Add(action, m?.File ?? "", m?.Anim ?? "", m?.Hit?.ToString("0.###", CultureInfo.InvariantCulture) ?? "", "");
        UpdateMotionInfo(_motions.Rows[i]);
    }

    private void AddCombo()
    {
        var have = new HashSet<string>(_motions.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["action"].Value as string));
        // 通常攻撃: 名前に Combo / NormalAttack が入り、攻撃判定のある最初の 5 つ (派生の動作は除く)
        var combo = CurrentBase.Motions.Where(m => m.hit != null && (m.name.Contains("Combo") || m.name.Contains("NormalAttack")) && !m.name.Contains("QTE") && !m.name.Contains("_Parry") && !m.name.Contains("Defence") && !m.name.Contains("Loop") && !m.name.Contains("Charge"))
            .Take(5);
        foreach (var m in combo)
            if (!have.Contains(m.name)) AddMotionRow(new MotionRow { Action = m.name });
    }

    private void UpdateMotionInfo(DataGridViewRow r)
    {
        var a = r.Cells["action"].Value as string;
        var m = CurrentBase.Motions.FirstOrDefault(x => x.name == a);
        r.Cells["info"].Value = m.name == null ? Strings.T("(土台に無い動作)")
            : $"{m.sec:0.00}s" + (m.hit is double h ? Strings.T(" / 当たる {0:0.00}", h) : "") + (m.loop ? Strings.T(" / ループ") : "");
    }

    // ------------------------------------------------------------------ 声・絵の確認

    /// <summary>声の名前の頭 → 鳴るとき (Vocie はゲームの綴りのまま)</summary>
    private static readonly (string prefix, string when)[] VoiceKinds =
    {
        ("LightAttackVoice", "通常攻撃のかけ声"), ("HardAttackVoice", "強い攻撃のかけ声"),
        ("LightHurtVocie", "小さく攻撃を受けた"), ("HardHurtVocie", "大きく攻撃を受けた"),
        ("HealthLowVocie", "体力が少ない"), ("DieVocie", "倒れた"),
        ("FightStartVoice", "戦闘の始まり"), ("FightWellVoice", "いい戦いをした"),
        ("BuffChooseVoice", "バフを選ぶ"), ("BuffEquipVoice", "バフを付けた"),
        ("EquipWearVocie", "装備を付けた"), ("ChooseCharVocie", "出撃前にキャラを選んだ"),
        ("ShowPoseVocie", "キャラ画面のポーズ"), ("SceneChat", "拠点の会話"),
    };

    private static string WhenOf(string name)
    {
        foreach (var (prefix, when) in VoiceKinds)
            if (name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)) return Strings.T(when);
        return "";
    }

    /// <summary>表を作り直す (土台のキャラの声の名前 + 選んだファイル)</summary>
    private void RefreshVoiceGrid()
    {
        if (_voiceGrid == null) return;
        int first = _voiceGrid.FirstDisplayedScrollingRowIndex;
        _voiceGrid.Rows.Clear();
        foreach (var v in CurrentBase.Voices)
        {
            _voiceFiles.TryGetValue(v, out var f);
            int i = _voiceGrid.Rows.Add(WhenOf(v), v, f != null ? Path.GetFileName(f) : "");
            _voiceGrid.Rows[i].Cells["file"].ToolTipText = f ?? "";
            if (f != null) _voiceGrid.Rows[i].Cells["file"].Style.ForeColor = File.Exists(f) ? Good : Bad;
        }
        if (first >= 0 && first < _voiceGrid.RowCount) _voiceGrid.FirstDisplayedScrollingRowIndex = first;
        int have = CurrentBase.Voices.Count(v => _voiceFiles.ContainsKey(v));
        _voiceInfo.Text = Strings.T("選んだ声: {0} / {1} (戦闘・画面 38 個 + 拠点の会話。全部そろえなくても動きます)", have, CurrentBase.Voices.Count);
        _voiceInfo.ForeColor = have == 0 ? Dim : have == CurrentBase.Voices.Count ? Good : TextColor;
    }

    /// <summary>選んだ行にファイルを選ぶ。1 行ならそのファイル、複数行なら選んだファイルを順に割り当てる</summary>
    private void ChooseVoice(DataGridViewRow[] rows)
    {
        if (rows.Length == 0) { SetStatus(Strings.T("先に表で行を選んでください"), Warn); return; }
        rows = rows.OrderBy(r => r.Index).ToArray();
        using var d = new OpenFileDialog { Filter = "ogg / wav / mp3|*.ogg;*.wav;*.mp3", Multiselect = rows.Length > 1 };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var files = d.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int i = 0; i < rows.Length && i < files.Length; i++) _voiceFiles[(string)rows[i].Cells["name"].Value] = files[i];
        if (files.Length < rows.Length && files.Length == 1)
            foreach (var r in rows) _voiceFiles[(string)r.Cells["name"].Value] = files[0]; // 1 つのファイルを選んだ全部の行に
        RefreshVoiceGrid();
    }

    /// <summary>フォルダの中の、ゲームの声の名前のファイルをまとめて入れる</summary>
    private void ImportVoiceFolder()
    {
        var folder = PickFolder(Strings.T("ゲームの声の名前のファイルを入れたフォルダ"));
        if (folder == null) return;
        var map = Pack.MatchVoices(folder, CurrentBase);
        foreach (var kv in map) _voiceFiles[kv.Key] = kv.Value;
        RefreshVoiceGrid();
        SetStatus(Strings.T("フォルダから {0} 個の声を入れました", map.Count), map.Count > 0 ? Good : Warn);
    }

    /// <summary>今の Pack のフォルダ (お手本 images_template の大きさを読むため)</summary>
    private string PackDir => _game == null || _key.Text.Trim().Length == 0 ? null : Path.Combine(Pack.CharactersDir(_game), _key.Text.Trim());

    /// <summary>画像ファイルの大きさ (読めなければ null)</summary>
    private static Size? FileSize(string f)
    {
        try { using var img = Image.FromStream(new MemoryStream(File.ReadAllBytes(f)), false, false); return img.Size; }
        catch { return null; }
    }

    private void RefreshImageGrid()
    {
        if (_imageGrid == null) return;
        int first = _imageGrid.FirstDisplayedScrollingRowIndex;
        _imageGrid.Rows.Clear();
        var dir = PackDir;
        var info = Pack.ImageInfo.ToDictionary(x => x.kind, x => x);
        int stretched = 0;
        foreach (var k in Pack.ImageKinds)
        {
            var what = info.TryGetValue(k, out var x) ? Strings.T(x.what) + " (" + Strings.T(x.where) + ")" : Strings.T("飾り・アイコン (土台のままでも大丈夫)");
            var want = Pack.ImageSize(dir, k);
            _imageFiles.TryGetValue(k, out var f);
            var text = f != null ? Path.GetFileName(f) : "";
            var color = Good;
            if (f != null && !File.Exists(f)) color = Bad;
            else if (f != null && FileSize(f) is Size have)
            {
                text += $"  ({have.Width}×{have.Height})";
                if (want is Size w && Pack.AspectDiffers(have, w))
                {
                    text += _imageFit.Checked ? "  " + Strings.T("余白を足します") : "  " + Strings.T("比が違うので伸びます");
                    color = Warn;
                    stretched++;
                }
            }
            int i = _imageGrid.Rows.Add(what, k, want is Size s ? $"{s.Width}×{s.Height}" : "—", text);
            _imageGrid.Rows[i].Cells["file"].ToolTipText = f ?? "";
            if (f != null) _imageGrid.Rows[i].Cells["file"].Style.ForeColor = color;
            if (!info.ContainsKey(k)) _imageGrid.Rows[i].DefaultCellStyle.ForeColor = Dim;
        }
        if (first >= 0 && first < _imageGrid.RowCount) _imageGrid.FirstDisplayedScrollingRowIndex = first;
        int count = Pack.ImageKinds.Count(k => _imageFiles.ContainsKey(k));
        int chara = Pack.ImageInfo.Count(x => _imageFiles.ContainsKey(x.kind));
        _imageInfo.Text = Strings.T("選んだ絵: {0} / {1} (キャラの絵 {2} / 15。全部そろえなくても動きます)", count, Pack.ImageKinds.Length, chara)
            + (stretched > 0 ? "  " + Strings.T("比が違う絵: {0}", stretched) : "");
        _imageInfo.ForeColor = count == 0 ? Dim : chara == 15 ? Good : TextColor;
    }

    /// <summary>選んだ行にファイルを選ぶ。1 行ならそのファイル、複数行なら選んだファイルを順に割り当てる</summary>
    private void ChooseImage(DataGridViewRow[] rows)
    {
        if (rows.Length == 0) { SetStatus(Strings.T("先に表で行を選んでください"), Warn); return; }
        rows = rows.OrderBy(r => r.Index).ToArray();
        using var d = new OpenFileDialog { Filter = "PNG / JPG|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = rows.Length > 1 };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var files = d.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int i = 0; i < rows.Length && i < files.Length; i++) _imageFiles[(string)rows[i].Cells["name"].Value] = files[i];
        if (files.Length < rows.Length && files.Length == 1)
            foreach (var r in rows) _imageFiles[(string)r.Cells["name"].Value] = files[0];
        RefreshImageGrid();
    }

    /// <summary>フォルダの中の、絵の名前のファイルをまとめて入れる</summary>
    private void ImportImageFolder()
    {
        var folder = PickFolder(Strings.T("絵の名前のファイルを入れたフォルダ"));
        if (folder == null) return;
        var map = Pack.MatchImages(folder);
        foreach (var kv in map) _imageFiles[kv.Key] = kv.Value;
        RefreshImageGrid();
        SetStatus(Strings.T("フォルダから {0} 枚の絵を入れました", map.Count), map.Count > 0 ? Good : Warn);
    }

    // ------------------------------------------------------------------ 画面 ⇔ Pack

    private void NewPack()
    {
        _openedFolder = null;
        _pack = new Pack();
        if (_game != null) _pack.Id = Pack.FreeId(_game);
        ToUi();
        SetStatus(Strings.T("新しい Pack です。上から順に埋めて「ゲームに入れる」を押してください。"), Dim);
    }

    private void OpenPack()
    {
        if (!RequireGame()) return;
        var start = Pack.CharactersDir(_game);
        using var d = new FolderBrowserDialog { Description = Strings.T("開く Pack のフォルダ (character.json があるフォルダ)"), SelectedPath = Directory.Exists(start) ? start : _game };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        OpenFolder(d.SelectedPath);
    }

    private void OpenFolder(string folder)
    {
        if (!File.Exists(Path.Combine(folder, "character.json"))) { SetStatus(Strings.T("このフォルダには character.json がありません"), Bad); return; }
        _pack = Pack.Open(folder);
        _openedFolder = folder;
        ToUi();
        SetStatus(Strings.T("開きました: {0}", folder), Good);
    }

    private void ToUi()
    {
        _key.Text = _pack.Key;
        _id.Value = Math.Max(_id.Minimum, Math.Min(_id.Maximum, _pack.Id));
        _base.SelectedItem = BaseChara.All.FirstOrDefault(b => b.Id == _pack.Base) ?? BaseChara.All.First();
        _nameJa.Text = _pack.NameJa; _nameEn.Text = _pack.NameEn; _nameZh.Text = _pack.NameZh;
        _model.Text = _pack.Model ?? "";
        _weapon.Text = _pack.Weapon ?? "";
        _equip.Value = Math.Max(_equip.Minimum, Math.Min(_equip.Maximum, _pack.WeaponEquip));
        _scale.Value = (decimal)Math.Max(0.01, Math.Min(100, _pack.WeaponScale));
        for (int i = 0; i < 3; i++)
        {
            _pos[i].Value = (decimal)Math.Max(-10, Math.Min(10, _pack.WeaponPos[i]));
            _rot[i].Value = (decimal)Math.Max(-360, Math.Min(360, _pack.WeaponRot[i]));
        }
        _motions.Rows.Clear();
        OnBaseChanged();
        foreach (var m in _pack.Motions) AddMotionRow(m);
        _voiceFiles.Clear();
        foreach (var kv in _pack.VoiceFiles) _voiceFiles[kv.Key] = kv.Value;
        _voiceBase = _pack.Base;
        _voiceChinese.Checked = _pack.VoiceChinese;
        RefreshVoiceGrid();
        _imageFiles.Clear();
        foreach (var kv in _pack.ImageFiles) _imageFiles[kv.Key] = kv.Value;
        RefreshImageGrid();
    }

    private Pack FromUi()
    {
        var p = new Pack
        {
            Key = _key.Text.Trim(), Id = (long)_id.Value, Base = CurrentBase.Id,
            NameJa = _nameJa.Text.Trim(), NameEn = _nameEn.Text.Trim(), NameZh = _nameZh.Text.Trim(),
            Model = _model.Text.Length > 0 ? _model.Text : null,
            Weapon = _weapon.Text.Length > 0 ? _weapon.Text : null,
            WeaponEquip = (long)_equip.Value, WeaponScale = (double)_scale.Value,
            WeaponPos = _pos.Select(x => (double)x.Value).ToArray(), WeaponRot = _rot.Select(x => (double)x.Value).ToArray(),
            ImageFit = _imageFit.Checked,
        };
        foreach (var kv in _voiceFiles)
            if (CurrentBase.Voices.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) p.VoiceFiles[kv.Key] = kv.Value;
        p.VoiceChinese = _voiceChinese.Checked;
        foreach (var kv in _imageFiles) p.ImageFiles[kv.Key] = kv.Value;
        foreach (DataGridViewRow r in _motions.Rows)
        {
            var action = r.Cells["action"].Value as string;
            var file = r.Cells["file"].Value as string;
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(file)) continue;
            double? hit = double.TryParse((r.Cells["hit"].Value as string ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : null;
            var anim = (r.Cells["anim"].Value as string ?? "").Trim();
            p.Motions.Add(new MotionRow { Action = action, File = file, Anim = anim.Length > 0 ? anim : null, Hit = hit });
        }
        return p;
    }

    // ------------------------------------------------------------------ 作る・書き出す

    private void BuildPack()
    {
        if (!RequireGame()) return;
        var p = FromUi();
        var problems = p.Problems(_game);
        if (problems.Count > 0) { SetStatus(problems[0], Bad); MessageBox.Show(this, string.Join(Environment.NewLine, problems), Strings.T("直してください")); return; }
        var dest = Path.Combine(Pack.CharactersDir(_game), p.Key);
        bool other = Directory.Exists(dest) && (_openedFolder == null || !string.Equals(Path.GetFullPath(_openedFolder), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase));
        if (other && MessageBox.Show(this, Strings.T("{0} はもうあります。上書きしますか?", dest), Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Cursor = Cursors.WaitCursor;
        try
        {
            dest = p.Build(_game);
            _pack = Pack.Open(dest);
            _openedFolder = dest;
            ToUi();
            SetStatus(Strings.T("ゲームに入れました: {0}  (ゲームを起動し直すと出ます)", dest), Good);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void ExportZip()
    {
        if (!RequireGame()) return;
        if (_openedFolder == null || !Directory.Exists(_openedFolder)) { SetStatus(Strings.T("先に「ゲームに入れる」で Pack を作ってください"), Warn); return; }
        using var d = new SaveFileDialog { Filter = "zip|*.zip", FileName = Path.GetFileName(_openedFolder) + ".zip" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var skipped = Pack.Zip(_openedFolder, d.FileName);
        SetStatus(Strings.T("書き出しました: {0}", d.FileName) + (skipped.Count > 0 ? Strings.T("  (ゲームから書き出した物は除きました: {0})", string.Join(", ", skipped)) : ""), Good);
    }

    // ------------------------------------------------------------------ ゲームのフォルダ

    private bool RequireGame()
    {
        if (_game != null) return true;
        SetStatus(Strings.T("先にゲームのフォルダを選んでください"), Warn);
        ChooseGame();
        return _game != null;
    }

    private void ChooseGame()
    {
        using var d = new FolderBrowserDialog { Description = Strings.T("VED:Recure のフォルダ (ved.exe がある場所) を選んでください") };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (!GameLocator.IsGameFolder(d.SelectedPath)) { SetStatus(Strings.T("このフォルダには ved.exe がありません"), Bad); return; }
        _game = d.SelectedPath;
        Settings.GameDir = _game;
        UpdateGameLabel();
    }

    private void UpdateGameLabel() =>
        _gameLabel.Text = _game == null ? Strings.T("ゲームのフォルダが見つかりません") : Strings.T("ゲーム: {0}", _game) + "   " + Strings.T("(対応: {0})", BaseChara.GameVersion);

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }
}

/// <summary>押した部品へ勝手にスクロールしない欄 (ボタンやチェックを押すと上に飛ぶのを防ぐ)</summary>
internal sealed class StayFlow : FlowLayoutPanel
{
    protected override Point ScrollToControl(Control activeControl) => AutoScrollPosition;
}
