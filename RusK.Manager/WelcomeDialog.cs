using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RusK.Manager;

/// <summary>
/// 初めて (RusK が入っていないとき) だけ出す「はじめに」の画面。
/// 以前のセットアップと同じように、入れる Mod をチェックで選んで (最初は全部オン)、まとめてインストールする
/// </summary>
internal sealed class WelcomeDialog : Form
{
    private static readonly Color Bg = Color.FromArgb(22, 24, 31);
    private static readonly Color Field = Color.FromArgb(28, 31, 40);
    private static readonly Color TextColor = Color.FromArgb(230, 233, 240);
    private static readonly Color Dim = Color.FromArgb(138, 144, 160);
    private static readonly Color Accent = Color.FromArgb(92, 158, 255);

    private readonly Dictionary<CheckBox, ModEntry> _checks = new();

    /// <summary>選ばれた Mod (インストールが押されたとき)</summary>
    public List<ModEntry> Selected => _checks.Where(kv => kv.Key.Checked).Select(kv => kv.Value).ToList();

    public WelcomeDialog(IReadOnlyList<ModEntry> mods, Font font, Font bold, Font title, Font small, bool needBepInEx)
    {
        Text = "RusK Mod Manager";
        Font = font;
        BackColor = Bg;
        ForeColor = TextColor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 560);

        Controls.Add(new Label { Text = Strings.T("RusK へようこそ"), Font = title, AutoSize = true, Location = new Point(24, 20) });
        Controls.Add(new Label
        {
            Text = Strings.T("RusK 本体と、使う Mod をまとめてインストールします。") + "\n" +
                   (needBepInEx ? Strings.T("BepInEx (Mod を動かす土台) も一緒に入れます。") + "\n" : "") +
                   Strings.T("Mod はあとから一覧で入れたり外したりできます。"),
            ForeColor = Dim, Location = new Point(26, 60), Size = new Size(550, 66),
        });

        var list = new FlowLayoutPanel
        {
            Location = new Point(24, 130), Size = new Size(552, 360), AutoScroll = true, BackColor = Field,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 8, 12, 8),
        };
        Controls.Add(list);
        foreach (var m in mods)
        {
            var check = new CheckBox
            {
                Text = m.Name + (m.Latest != null ? "  v" + m.Latest.Version : ""), Font = bold, Checked = true,
                AutoSize = true, ForeColor = TextColor, Margin = new Padding(0, 6, 0, 0), UseMnemonic = false,
            };
            _checks[check] = m;
            list.Controls.Add(check);
            list.Controls.Add(new Label
            {
                Text = m.Description, Font = small, ForeColor = Dim, AutoSize = true, UseMnemonic = false,
                MaximumSize = new Size(500, 0), Margin = new Padding(20, 0, 0, 4),
            });
        }
        // 必要な Mod (Party) を外したら、それを使う Mod も外す / 使う Mod を選んだら、必要な Mod も選ぶ
        foreach (var kv in _checks)
        {
            var check = kv.Key;
            var mod = kv.Value;
            check.CheckedChanged += (_, _) =>
            {
                foreach (var other in _checks)
                {
                    if (check.Checked && mod.Requires.Contains(other.Value.Id)) other.Key.Checked = true;
                    if (!check.Checked && other.Value.Requires.Contains(mod.Id)) other.Key.Checked = false;
                }
            };
        }

        var all = new LinkLabel { Text = Strings.T("全部選ぶ / 全部外す"), AutoSize = true, Location = new Point(24, 500), LinkColor = Accent };
        all.LinkClicked += (_, _) =>
        {
            bool on = _checks.Keys.Any(c => !c.Checked);
            foreach (var c in _checks.Keys) c.Checked = on;
        };
        Controls.Add(all);

        var install = new Button { Text = Strings.T("インストール"), Size = new Size(150, 38), Location = new Point(426, 508), DialogResult = DialogResult.OK };
        var later = new Button { Text = Strings.T("あとで"), Size = new Size(110, 38), Location = new Point(308, 508), DialogResult = DialogResult.Cancel };
        foreach (var (b, back) in new[] { (install, Accent), (later, Field) })
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = back == Accent ? Color.White : TextColor;
            b.Font = bold;
            b.Cursor = Cursors.Hand;
            Controls.Add(b);
        }
        AcceptButton = install;
        CancelButton = later;
    }
}
