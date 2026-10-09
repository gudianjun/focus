using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Focus;

/// <summary>右下角的托盘图标和右键菜单。</summary>
internal sealed class TrayIcon : IDisposable
{
    static readonly Icon IdleIcon = CreateIcon(Color.FromArgb(0x5B, 0x67, 0x7A), paused: false);
    static readonly Icon RunningIcon = CreateIcon(Color.FromArgb(0x2F, 0x6F, 0xEB), paused: false);
    static readonly Icon PausedIcon = CreateIcon(Color.FromArgb(0xE0, 0x8E, 0x0B), paused: true);

    readonly NotifyIcon _icon;
    readonly MainWindow _main;
    readonly ToolStripMenuItem _toggleItem;

    public TrayIcon(MainWindow main)
    {
        _main = main;

        _toggleItem = new ToolStripMenuItem("开始专注", null, (_, _) => _main.StartOrTogglePause());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("显示", null, (_, _) => _main.ShowFront()) { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripMenuItem("设置...", null, (_, _) => App.Current.ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => App.Current.ExitApp()));
        menu.Opening += (_, _) => UpdateMenu();

        _icon = new NotifyIcon
        {
            Icon = IdleIcon,
            Text = "专注模式",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _main.ShowFront();
        };

        _main.StatusChanged += (_, _) => UpdateStatus();
    }

    void UpdateMenu()
    {
        _toggleItem.Text = _main.State switch
        {
            FocusState.Running => "暂停",
            FocusState.Paused => "继续",
            _ => "开始专注",
        };
    }

    void UpdateStatus()
    {
        var icon = _main.State switch
        {
            FocusState.Running => RunningIcon,
            FocusState.Paused => PausedIcon,
            _ => IdleIcon,
        };
        if (_icon.Icon != icon)
            _icon.Icon = icon;

        var text = _main.State switch
        {
            FocusState.Running => $"专注中 {_main.RemainingText}",
            FocusState.Paused => $"已暂停 {_main.RemainingText}",
            _ => "专注模式",
        };
        if (_main.State != FocusState.Setup && _main.TaskName.Length > 0)
            text += $"\n{_main.TaskName}";
        if (text.Length > 63)
            text = text[..62] + "…";
        if (_icon.Text != text)
            _icon.Text = text;
    }

    public void ShowNotification(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    static Icon CreateIcon(Color color, bool paused)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, 30, 30);

            if (paused)
            {
                g.FillRectangle(Brushes.White, 10, 9, 4, 14);
                g.FillRectangle(Brushes.White, 18, 9, 4, 14);
            }
            else
            {
                // 时钟指针
                using var pen = new Pen(Color.White, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, 16, 16, 16, 7);
                g.DrawLine(pen, 16, 16, 22, 20);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
