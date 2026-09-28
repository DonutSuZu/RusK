using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RusK.Installer;

/// <summary>
/// セットアップウィザード。ページ:
///   0 ようこそ → 1 インストール先 → 2 コンポーネント (またはアンインストールの設定) → 3 実行 → 4 完了
/// </summary>
internal sealed class SetupForm : Form
{
    // 配色 (RusK のメニューに合わせたダーク)
    private static readonly Color Bg = Color.FromArgb(22, 24, 31);
    private static readonly Color Side = Color.FromArgb(16, 17, 22);
    private static readonly Color Field = Color.FromArgb(32, 35, 44);
    private static readonly Color TextColor = Color.FromArgb(230, 233, 240);
    private static readonly Color Dim = Color.FromArgb(138, 144, 160);
    private static readonly Color Accent = Color.FromArgb(92, 158, 255);
    private static readonly Color Good = Color.FromArgb(102, 217, 128);
    private static readonly Color Bad = Color.FromArgb(242, 102, 102);
    private static readonly Color Warn = Color.FromArgb(242, 191, 76);

    private const string BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be";

    private static readonly string[] StepNames = { "ようこそ", "インストール先", "コンポーネント", "インストール", "完了" };

    private readonly Font _font = new("Yu Gothic UI", 9.75f);
    private readonly Font _bold = new("Yu Gothic UI", 9.75f, FontStyle.Bold);
    private readonly Font _title = new("Yu Gothic UI", 15f, FontStyle.Bold);

    private readonly List<Label> _stepLabels = new();
    private readonly Panel[] _pages = new Panel[5];
    private readonly Label _header = new();
    private readonly Label _subHeader = new();
    private readonly Button _back = new();
    private readonly Button _next = new();
    private readonly Button _cancel = new();

    // ページ 1
    private readonly TextBox _path = new();
    private readonly Label _gameStatus = new();
    private readonly Label _bepStatus = new();
    private readonly Label _ruskStatus = new();
    private readonly Panel _bepPanel = new();
    private readonly TextBox _bepZip = new();
    private readonly Panel _modePanel = new();
    private readonly RadioButton _modeInstall = new();
    private readonly RadioButton _modeUninstall = new();

    // ページ 2
    private readonly Panel _componentPanel = new();
    private readonly Dictionary<string, CheckBox> _componentChecks = new();
    private readonly CheckBox _musicFolder = new();
    private readonly Panel _uninstallPanel = new();
    private readonly CheckBox _removeData = new();
    private readonly CheckBox _removeMusic = new();

    // ページ 3, 4
    private readonly ProgressBar _progress = new();
    private readonly TextBox _log = new();
    private readonly Label _result = new();
    private readonly CheckBox _launch = new();
    private readonly CheckBox _openFolder = new();

    private int _page;
    private bool _running;
    private bool _succeeded;

    private bool Uninstalling => _modeUninstall.Checked && _modePanel.Visible;

    public SetupForm()
    {
        Text = $"RusK セットアップ v{InstallEngine.PayloadVersion}";
        Font = _font;
        BackColor = Bg;
        ForeColor = TextColor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 520);

        BuildChrome();
        BuildWelcome();
        BuildLocation();
        BuildComponents();
        BuildProgress();
        BuildFinish();

        // 起動時にゲームを自動検出しておく
        var found = GameLocator.FindGame();
        if (found != null) _path.Text = found;
        RefreshStatus();

        ShowPage(0);
    }

    // ------------------------------------------------------------------ 共通の枠

    private void BuildChrome()
    {
        var side = new Panel { Dock = DockStyle.Left, Width = 190, BackColor = Side };
        Controls.Add(side);

        // ウィンドウのアイコン (exe に埋め込んだ icon.ico) と、ロゴ (icon.png)
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        var logo = LoadLogo();
        if (logo != null)
        {
            side.Controls.Add(new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Location = new Point(18, 28), Size = new Size(48, 44),
            });
        }

        side.Controls.Add(new Label
        {
            Text = "RusK", Font = new Font("Yu Gothic UI", 26f, FontStyle.Bold), ForeColor = Accent,
            Location = new Point(logo != null ? 66 : 22, 24), AutoSize = true,
        });
        side.Controls.Add(new Label
        {
            Text = $"VED:Recure Mod Loader\nv{InstallEngine.PayloadVersion}\n対応ゲーム:\n{InstallEngine.SupportedGameVersion}", ForeColor = Dim,
            Location = new Point(25, 78), AutoSize = true,
        });

        for (int i = 0; i < StepNames.Length; i++)
        {
            var l = new Label
            {
                Text = StepNames[i], Location = new Point(25, 160 + i * 34), Size = new Size(160, 26),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Dim,
            };
            _stepLabels.Add(l);
            side.Controls.Add(l);
        }

        _header.SetBounds(215, 22, 540, 32);
        _header.Font = _title;
        _subHeader.SetBounds(217, 56, 540, 22);
        _subHeader.ForeColor = Dim;
        Controls.Add(_header);
        Controls.Add(_subHeader);

        // 下のボタン
        var bar = new Panel { Location = new Point(190, 466), Size = new Size(590, 54), BackColor = Side };
        Controls.Add(bar);
        StyleButton(_back, "< 戻る", false);
        StyleButton(_next, "次へ >", true);
        StyleButton(_cancel, "キャンセル", false);
        _back.SetBounds(262, 12, 96, 30);
        _next.SetBounds(364, 12, 110, 30);
        _cancel.SetBounds(480, 12, 96, 30);
        _back.Click += (_, _) => ShowPage(_page - 1);
        _next.Click += (_, _) => OnNext();
        _cancel.Click += (_, _) => Close();
        bar.Controls.AddRange(new Control[] { _back, _next, _cancel });

        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i] = new Panel { Location = new Point(215, 90), Size = new Size(545, 366), BackColor = Bg, Visible = false };
            Controls.Add(_pages[i]);
        }

        FormClosing += (_, e) =>
        {
            if (_running)
            {
                e.Cancel = true;
                MessageBox.Show(this, "処理中は閉じられません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    // ------------------------------------------------------------------ 0 ようこそ

    private void BuildWelcome()
    {
        var p = _pages[0];
        p.Controls.Add(new Label
        {
            AutoSize = false, Size = new Size(540, 360), Location = new Point(0, 0),
            Text =
                "VED:Recure 用の Mod ローダー「RusK」をインストールします。\n\n" +
                "RusK でできること\n" +
                "  ・ゲーム内メニュー (Insert キー) で Mod の機能を ON/OFF\n" +
                "  ・Mod の読み込み / 取り外し / 再読み込み\n" +
                "  ・キー割り当て、アクショントリガー、設定プロファイル\n\n" +
                "必要なもの\n" +
                $"  ・VED:Recure (Steam 版)  … 対応バージョン {InstallEngine.SupportedGameVersion}\n" +
                "  ・BepInEx 6 (IL2CPP 版)  … 入っていなければ次の画面で案内します\n\n" +
                "注意\n" +
                "  ・インストール中はゲームを終了しておいてください\n" +
                "  ・セーブデータを書き換える機能は自己責任で使ってください\n" +
                "    (最初の変更前に自動でバックアップします)",
        });
    }

    // ------------------------------------------------------------------ 1 インストール先

    private void BuildLocation()
    {
        var p = _pages[1];
        p.Controls.Add(Caption("ゲームのフォルダ (ved.exe がある場所)", 0, 0));

        StyleTextBox(_path);
        _path.SetBounds(0, 26, 330, 28);
        _path.TextChanged += (_, _) => RefreshStatus();
        p.Controls.Add(_path);

        var browse = new Button();
        StyleButton(browse, "参照...", false);
        browse.SetBounds(338, 25, 96, 30);
        browse.Click += (_, _) => BrowseGame();
        p.Controls.Add(browse);

        var detect = new Button();
        StyleButton(detect, "自動検出", false);
        detect.SetBounds(440, 25, 96, 30);
        detect.Click += (_, _) =>
        {
            var found = GameLocator.FindGame();
            if (found != null) _path.Text = found;
            else MessageBox.Show(this, "Steam のライブラリに VED:Recure が見つかりませんでした。\n「参照...」から選んでください。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        p.Controls.Add(detect);

        _gameStatus.SetBounds(0, 70, 540, 22);
        _bepStatus.SetBounds(0, 94, 540, 22);
        _ruskStatus.SetBounds(0, 118, 540, 22);
        p.Controls.AddRange(new Control[] { _gameStatus, _bepStatus, _ruskStatus });

        // BepInEx が無いときだけ出す
        _bepPanel.SetBounds(0, 150, 540, 110);
        _bepPanel.BackColor = Field;
        _bepPanel.Controls.Add(new Label
        {
            Text = "BepInEx 6 (IL2CPP 版) が必要です。公式サイトから\n「BepInEx-Unity.IL2CPP-win-x64-...zip」をダウンロードして、下で選んでください。",
            Location = new Point(10, 8), Size = new Size(520, 40), ForeColor = TextColor,
        });
        var link = new LinkLabel
        {
            Text = "BepInEx のダウンロードページを開く", Location = new Point(10, 50), AutoSize = true,
            LinkColor = Accent, ActiveLinkColor = Accent,
        };
        link.LinkClicked += (_, _) => Open(BepInExUrl);
        _bepPanel.Controls.Add(link);
        StyleTextBox(_bepZip);
        _bepZip.SetBounds(10, 74, 410, 26);
        _bepZip.ReadOnly = true;
        _bepZip.TextChanged += (_, _) => UpdateButtons();
        _bepPanel.Controls.Add(_bepZip);
        var pickZip = new Button();
        StyleButton(pickZip, "zip を選択...", false);
        pickZip.SetBounds(426, 72, 106, 29);
        pickZip.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "BepInEx zip (*.zip)|*.zip", Title = "BepInEx の zip を選択" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _bepZip.Text = dlg.FileName;
        };
        _bepPanel.Controls.Add(pickZip);
        p.Controls.Add(_bepPanel);

        // RusK が入っているときだけ出す
        _modePanel.SetBounds(0, 270, 540, 80);
        _modePanel.Controls.Add(Caption("RusK はすでにインストールされています。どうしますか？", 0, 0));
        _modeInstall.Text = "更新・修復する (設定はそのまま)";
        _modeInstall.SetBounds(6, 26, 520, 24);
        _modeInstall.Checked = true;
        _modeUninstall.Text = "アンインストールする";
        _modeUninstall.SetBounds(6, 50, 520, 24);
        _modeInstall.CheckedChanged += (_, _) => UpdateButtons();
        _modePanel.Controls.AddRange(new Control[] { _modeInstall, _modeUninstall });
        p.Controls.Add(_modePanel);
    }

    private void BrowseGame()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "VED:Recure のフォルダ (ved.exe がある場所) を選んでください",
            SelectedPath = Directory.Exists(_path.Text) ? _path.Text : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
    }

    private void RefreshStatus()
    {
        var dir = _path.Text.Trim();
        bool game = GameLocator.IsGameFolder(dir);
        SetStatus(_gameStatus, game, game ? "ゲーム: 見つかりました" : "ゲーム: ved.exe がありません (フォルダを確認してください)");

        if (!game)
        {
            _bepStatus.Text = "";
            _ruskStatus.Text = "";
            _bepPanel.Visible = false;
            _modePanel.Visible = false;
            UpdateButtons();
            return;
        }

        bool bep = GameLocator.HasBepInEx(dir);
        SetStatus(_bepStatus, bep, bep
            ? $"BepInEx: 導入済み ({GameLocator.BepInExVersion(dir)})"
            : "BepInEx: 入っていません (下で zip を選ぶと一緒に導入します)", warnInsteadOfBad: true);
        _bepPanel.Visible = !bep;

        var rusk = GameLocator.RuskVersion(dir);
        if (rusk != null)
        {
            _ruskStatus.Text = $"●  RusK: v{rusk} がインストール済み → v{InstallEngine.PayloadVersion} で更新できます";
            _ruskStatus.ForeColor = Accent;
        }
        else
        {
            _ruskStatus.Text = "●  RusK: 未インストール";
            _ruskStatus.ForeColor = Dim;
        }
        _modePanel.Visible = rusk != null;
        _modePanel.Top = bep ? 150 : 270;
        if (rusk == null) _modeInstall.Checked = true;

        // 入っている Mod に合わせてチェックを付ける (更新のとき)
        if (rusk != null)
        {
            _componentChecks["music"].Checked = File.Exists(Path.Combine(dir, "RusK", "mods", "RuskMusic.dll"));
            _componentChecks["extreme"].Checked = File.Exists(Path.Combine(dir, "RusK", "mods", "RuskExtreme.dll"));
            // 前のバージョンに無かった部品は、更新のときも最初は ON にしておく
            _componentChecks["camera"].Checked = true;
            _componentChecks["ui"].Checked = true;
            _componentChecks["party"].Checked = true;
            _componentChecks["model"].Checked = true;
            _componentChecks["itemmodel"].Checked = true;
        }
        UpdateButtons();
    }

    // ------------------------------------------------------------------ 2 コンポーネント / アンインストール

    private void BuildComponents()
    {
        var p = _pages[2];

        _componentPanel.SetBounds(0, 0, 545, 366);
        _componentPanel.AutoScroll = true; // 部品が増えて収まらないときはスクロールする
        int y = 0;
        foreach (var c in InstallEngine.Components)
        {
            // 必須のものは無効化 (灰色で読みにくい) ではなく、クリックしても外れないようにする
            var cb = new CheckBox
            {
                Text = c.Name, Font = _bold, Checked = c.DefaultOn || c.Required, AutoCheck = !c.Required,
                Location = new Point(0, y), Size = new Size(515, 22),
            };
            _componentChecks[c.Id] = cb;
            _componentPanel.Controls.Add(cb);
            _componentPanel.Controls.Add(new Label
            {
                Text = c.Description, ForeColor = Dim, Location = new Point(20, y + 22), Size = new Size(495, 20),
            });
            y += 38;
        }
        _musicFolder.Text = "music フォルダを作る (RusK\\music と RusK\\music\\boss)";
        _musicFolder.Checked = true;
        _musicFolder.SetBounds(0, y + 6, 515, 24);
        _componentPanel.Controls.Add(_musicFolder);
        _componentPanel.Controls.Add(new Label
        {
            Text = "チェックを外した Mod は取り外します。設定 (RusK\\configs) はそのまま残します。",
            ForeColor = Dim, Location = new Point(0, y + 32), Size = new Size(515, 22),
        });
        _componentChecks["music"].CheckedChanged += (_, _) => _musicFolder.Enabled = _componentChecks["music"].Checked;
        p.Controls.Add(_componentPanel);

        _uninstallPanel.SetBounds(0, 0, 545, 366);
        _uninstallPanel.Controls.Add(new Label
        {
            Text = "RusK 本体と、同梱の Mod を削除します。\n" +
                   "BepInEx と、ほかの人が作った Mod は削除しません。",
            Location = new Point(0, 0), Size = new Size(540, 48),
        });
        _removeData.Text = "設定とデータも削除する (RusK\\configs, RusK\\data)";
        _removeData.SetBounds(0, 60, 540, 24);
        _removeMusic.Text = "music フォルダも削除する (入れた曲も消えます)";
        _removeMusic.SetBounds(0, 88, 540, 24);
        _removeMusic.ForeColor = Warn;
        _uninstallPanel.Controls.AddRange(new Control[] { _removeData, _removeMusic });
        p.Controls.Add(_uninstallPanel);
    }

    // ------------------------------------------------------------------ 3 実行

    private void BuildProgress()
    {
        var p = _pages[3];
        _progress.SetBounds(0, 0, 540, 16);
        p.Controls.Add(_progress);
        StyleTextBox(_log);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 9f);
        _log.SetBounds(0, 26, 540, 336);
        p.Controls.Add(_log);
    }

    private async void RunTask()
    {
        _running = true;
        _succeeded = false;
        _log.Clear();
        _progress.Value = 0;
        UpdateButtons();

        var dir = _path.Text.Trim();
        void Log(string s) => BeginInvoke(new Action(() => _log.AppendText(s + Environment.NewLine)));
        void Progress(int v) => BeginInvoke(new Action(() => _progress.Value = Math.Max(0, Math.Min(100, v))));

        try
        {
            if (Uninstalling)
            {
                var o = new UninstallOptions { GameDir = dir, RemoveData = _removeData.Checked, RemoveMusic = _removeMusic.Checked };
                await Task.Run(() => InstallEngine.Uninstall(o, Log, Progress));
            }
            else
            {
                var o = new InstallOptions
                {
                    GameDir = dir,
                    BepInExZip = string.IsNullOrWhiteSpace(_bepZip.Text) ? null : _bepZip.Text,
                    CreateMusicFolder = _musicFolder.Checked,
                };
                foreach (var kv in _componentChecks)
                    if (kv.Value.Checked) o.Components.Add(kv.Key);
                await Task.Run(() => InstallEngine.Install(o, Log, Progress));
            }
            _succeeded = true;
        }
        catch (UnauthorizedAccessException)
        {
            Log("エラー: フォルダに書き込めませんでした。セットアップを右クリック →「管理者として実行」で試してください。");
        }
        catch (Exception e)
        {
            Log("エラー: " + e.Message);
        }
        finally
        {
            _running = false;
        }

        if (_succeeded) ShowPage(4);
        else UpdateButtons();
    }

    // ------------------------------------------------------------------ 4 完了

    private void BuildFinish()
    {
        var p = _pages[4];
        _result.SetBounds(0, 0, 540, 170);
        p.Controls.Add(_result);
        _launch.Text = "ゲームを起動する (Steam)";
        _launch.SetBounds(0, 180, 540, 24);
        _launch.Checked = true;
        _openFolder.Text = "インストール先のフォルダを開く";
        _openFolder.SetBounds(0, 206, 540, 24);
        p.Controls.AddRange(new Control[] { _launch, _openFolder });
    }

    // ------------------------------------------------------------------ ページ移動

    private void ShowPage(int page)
    {
        if (page < 0) page = 0;
        _page = page;
        for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == page;
        for (int i = 0; i < _stepLabels.Count; i++)
        {
            _stepLabels[i].ForeColor = i == page ? TextColor : i < page ? Accent : Dim;
            _stepLabels[i].Font = i == page ? _bold : _font;
            _stepLabels[i].Text = (i == page ? "▶ " : i < page ? "✓ " : "    ") + StepNames[i];
        }

        switch (page)
        {
            case 0:
                SetHeader("ようこそ", "RusK のセットアップを始めます");
                break;
            case 1:
                SetHeader("インストール先", "VED:Recure のフォルダを選んでください");
                RefreshStatus();
                break;
            case 2:
                _componentPanel.Visible = !Uninstalling;
                _uninstallPanel.Visible = Uninstalling;
                SetHeader(Uninstalling ? "アンインストール" : "コンポーネント",
                    Uninstalling ? "削除する内容を確認してください" : "インストールするものを選んでください");
                break;
            case 3:
                SetHeader(Uninstalling ? "アンインストール中" : "インストール中", _path.Text.Trim());
                RunTask();
                break;
            case 4:
                SetHeader("完了", Uninstalling ? "アンインストールしました" : "インストールしました");
                _result.Text = Uninstalling
                    ? "RusK を削除しました。\nBepInEx はそのまま残っています。"
                    : "RusK をインストールしました。\n\n" +
                      "・ゲームを起動すると、画面右下に「RusK v" + InstallEngine.PayloadVersion + "」と表示されます\n" +
                      "・Insert キーでメニューを開きます\n" +
                      "・初回起動は BepInEx の準備で時間がかかることがあります\n" +
                      "・曲は RusK\\music に入れてください\n" +
                      "・使い方は RusK\\README.txt を見てください";
                _launch.Visible = !Uninstalling;
                break;
        }
        UpdateButtons();
    }

    private void OnNext()
    {
        if (_page == 3)
        {
            // 失敗したときの「やり直す」
            if (!_running && !_succeeded) RunTask();
            return;
        }

        if (_page == 4)
        {
            var dir = _path.Text.Trim();
            if (_launch.Visible && _launch.Checked) Open($"steam://rungameid/{GameLocator.SteamAppId}");
            if (_openFolder.Checked) Open(Directory.Exists(Path.Combine(dir, "RusK")) ? Path.Combine(dir, "RusK") : dir);
            Close();
            return;
        }

        if (_page == 2 && GameLocator.GameRunning(_path.Text.Trim()))
        {
            MessageBox.Show(this, "ゲームが起動中です。ゲームを終了してから続けてください。", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ShowPage(_page + 1);
    }

    private void UpdateButtons()
    {
        _back.Enabled = !_running && _page > 0 && _page != 4 && !(_page == 3 && _succeeded);
        _back.Visible = _page != 4;
        _cancel.Enabled = !_running;
        _cancel.Visible = _page != 4;

        switch (_page)
        {
            case 1:
                var dir = _path.Text.Trim();
                bool ok = GameLocator.IsGameFolder(dir) &&
                          (GameLocator.HasBepInEx(dir) || Uninstalling || File.Exists(_bepZip.Text));
                _next.Text = "次へ >";
                _next.Enabled = ok;
                break;
            case 2:
                _next.Text = Uninstalling ? "アンインストール" : "インストール";
                _next.Enabled = true;
                break;
            case 3:
                _next.Text = "やり直す";
                _next.Enabled = !_running && !_succeeded;
                _back.Text = "< 戻る";
                break;
            case 4:
                _next.Text = "完了";
                _next.Enabled = true;
                break;
            default:
                _next.Text = "次へ >";
                _next.Enabled = true;
                break;
        }
    }

    // ------------------------------------------------------------------ 見た目の小物

    private void SetHeader(string title, string sub)
    {
        _header.Text = title;
        _subHeader.Text = sub;
    }

    private Label Caption(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Dim };

    private static void SetStatus(Label label, bool ok, string text, bool warnInsteadOfBad = false)
    {
        label.Text = (ok ? "✓  " : warnInsteadOfBad ? "!  " : "✗  ") + text;
        label.ForeColor = ok ? Good : warnInsteadOfBad ? Warn : Bad;
    }

    private void StyleButton(Button b, string text, bool primary)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(60, 64, 78);
        b.BackColor = primary ? Color.FromArgb(40, 70, 120) : Field;
        b.ForeColor = TextColor;
        b.Cursor = Cursors.Hand;
    }

    private void StyleTextBox(TextBox t)
    {
        t.BackColor = Field;
        t.ForeColor = TextColor;
        t.BorderStyle = BorderStyle.FixedSingle;
    }

    private static Image LoadLogo()
    {
        try
        {
            var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("icon.png");
            return stream == null ? null : Image.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }
}
