using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Focus;

public enum FocusState { Setup, Running, Paused }

/// <summary>
/// 设定画面、倒计时画面和小计时条共用的窗口。
/// 始终显示在主显示器工作区的正上方居中，不可移动。
/// </summary>
public partial class MainWindow : Window
{
    static readonly int[] PresetMinutes = [10, 20, 30, 45, 60];
    const int MaxMinutes = 60;
    const int HotKeyId = 0xF0C5;
    const double TopOffset = 4;

    readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly DispatcherTimer _compactTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    IntPtr _handle;
    bool _repositioning;
    bool _compact;
    bool _completing;
    FocusState _state = FocusState.Setup;
    DateTime _endUtc;
    TimeSpan _pausedRemaining;
    TimeSpan _total;
    DateTime _startedAt;

    /// <summary>状态或剩余时间变化时触发（供托盘图标更新）。</summary>
    public event EventHandler? StatusChanged;

    public FocusState State => _state;
    public string TaskName { get; private set; } = "";
    public string RemainingText => FormatTime(Remaining);

    TimeSpan Remaining => _state switch
    {
        FocusState.Running => Max(_endUtc - DateTime.UtcNow, TimeSpan.Zero),
        FocusState.Paused => _pausedRemaining,
        _ => TimeSpan.Zero,
    };

    static AppSettings Settings => App.Current.Settings;

    public MainWindow()
    {
        InitializeComponent();

        foreach (var minutes in PresetMinutes)
            DurationBox.Items.Add(minutes.ToString());
        DurationBox.Text = Settings.LastMinutes.ToString();

        _tickTimer.Tick += (_, _) => OnTick();
        _compactTimer.Tick += (_, _) =>
        {
            _compactTimer.Stop();
            if (_state != FocusState.Setup && Settings.AutoCompact && !IsMouseOver)
                SetCompact(true);
        };
        _topmostTimer.Tick += (_, _) => ForceTopmost();

        SizeChanged += (_, _) => Reposition();
        ContentRendered += (_, _) => Reposition();
        LocationChanged += (_, _) => Reposition();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        MouseEnter += (_, _) => OnMouseEnterWindow();
        MouseLeave += (_, _) => OnMouseLeaveWindow();
    }

    /// <summary>创建窗口句柄并挂上消息处理（窗口隐藏时也要能接收快捷键）。</summary>
    public void Initialize()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
    }

    // ---------- 位置 ----------

    void Reposition()
    {
        if (_repositioning)
            return;

        double width = ActualWidth;
        if (width <= 0 && Content is FrameworkElement content)
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = content.DesiredSize.Width;
        }

        var workArea = SystemParameters.WorkArea;
        double left = Math.Round(workArea.Left + (workArea.Width - width) / 2);
        double top = workArea.Top + TopOffset;
        if (left == Left && top == Top)
            return;

        _repositioning = true;
        try
        {
            Left = left;
            Top = top;
        }
        finally
        {
            _repositioning = false;
        }
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Reposition);

    public void ShowFront()
    {
        Reposition();
        Show();
        Reposition();
        Activate();
        if (_state == FocusState.Setup)
        {
            TaskBox.Focus();
        }
        else
        {
            SetCompact(false);
            ScheduleCompact();
        }
    }

    // ---------- 消息处理 ----------

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_HOTKEY when wParam.ToInt32() == HotKeyId:
                OnHotKey();
                handled = true;
                break;
            case NativeMethods.WM_SYSCOMMAND when (wParam.ToInt32() & 0xFFF0) == NativeMethods.SC_MOVE:
                handled = true; // 禁止通过系统菜单移动
                break;
        }
        return IntPtr.Zero;
    }

    public void SuspendHotKey() => NativeMethods.UnregisterHotKey(_handle, HotKeyId);

    public bool ApplyHotKey()
    {
        NativeMethods.UnregisterHotKey(_handle, HotKeyId);
        if (string.IsNullOrWhiteSpace(Settings.HotKey))
            return true;
        if (!HotKey.TryParse(Settings.HotKey, out var modifiers, out var virtualKey))
            return false;
        return NativeMethods.RegisterHotKey(_handle, HotKeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
    }

    void OnHotKey()
    {
        if (_state == FocusState.Setup)
            ShowFront();
        StartOrTogglePause();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.Current.IsExiting)
        {
            // Alt+F4 等：设定画面收到托盘，专注中忽略
            e.Cancel = true;
            if (_state == FocusState.Setup)
                Hide();
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        NativeMethods.UnregisterHotKey(_handle, HotKeyId);
        base.OnClosing(e);
    }

    // ---------- 设定画面 ----------

    void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    void StartButton_Click(object sender, RoutedEventArgs e) => Start();

    void TaskBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        TaskPlaceholder.Visibility = TaskBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    bool TryGetMinutes(out int minutes)
    {
        var text = DurationBox.Text.Trim().Replace("分钟", "").Trim();
        return int.TryParse(text, out minutes) && minutes >= 1 && minutes <= MaxMinutes;
    }

    // ---------- 专注流程 ----------

    public void StartOrTogglePause()
    {
        if (_state == FocusState.Setup)
            Start();
        else
            TogglePause();
    }

    void Start()
    {
        if (!TryGetMinutes(out int minutes))
        {
            SetupError.Text = $"请输入 1～{MaxMinutes} 之间的分钟数。";
            SetupError.Visibility = Visibility.Visible;
            ShowFront();
            DurationBox.Focus();
            return;
        }

        SetupError.Visibility = Visibility.Collapsed;
        Settings.LastMinutes = minutes;
        Settings.Save();

        TaskName = TaskBox.Text.Trim();
        _total = TimeSpan.FromMinutes(minutes);
        _endUtc = DateTime.UtcNow + _total;
        _startedAt = DateTime.Now;
        _state = FocusState.Running;

        FocusTaskText.Text = TaskName;
        FocusTaskText.Visibility = TaskName.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompactTask.Text = TaskName;
        PauseButton.Content = "暂停";
        StatusText.Text = "专注中";

        SetupPanel.Visibility = Visibility.Collapsed;
        SetCompact(false);
        Topmost = true;
        ApplyAppearance();
        Show();
        ForceTopmost();

        _tickTimer.Start();
        _topmostTimer.Start();
        UpdateDisplay();
        ScheduleCompact();
    }

    void TogglePause()
    {
        if (_completing)
            return;
        if (_state == FocusState.Running)
        {
            _pausedRemaining = Remaining;
            _state = FocusState.Paused;
            PauseButton.Content = "继续";
            StatusText.Text = "已暂停";
        }
        else if (_state == FocusState.Paused)
        {
            _endUtc = DateTime.UtcNow + _pausedRemaining;
            _state = FocusState.Running;
            PauseButton.Content = "暂停";
            StatusText.Text = "专注中";
        }
        UpdateDisplay();
    }

    void PauseButton_Click(object sender, RoutedEventArgs e) => TogglePause();

    void AbandonButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmAbandon("确定要放弃本次专注吗？"))
            EndSession();
    }

    /// <summary>退出程序前调用。专注中需要确认，确认后记为放弃。</summary>
    public bool ConfirmExit()
    {
        if (_state == FocusState.Setup)
            return true;
        if (_completing)
            return false;
        ShowFront();
        return ConfirmAbandon("正在专注中，确定要退出吗？");
    }

    bool ConfirmAbandon(string message)
    {
        _compactTimer.Stop();
        var result = MessageBox.Show(this, message, "专注模式", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            if (!IsMouseOver)
                ScheduleCompact();
            return false;
        }
        Log(completed: false, taskDone: null);
        return true;
    }

    void OnTick()
    {
        if (_state == FocusState.Running && Remaining <= TimeSpan.Zero)
            Complete();
        else
            UpdateDisplay();
    }

    void Complete()
    {
        _tickTimer.Stop();
        _compactTimer.Stop();
        _pausedRemaining = TimeSpan.Zero;
        _state = FocusState.Paused; // 冻结在 00:00，等待用户确认
        _completing = true;
        SetCompact(false);
        Opacity = 1;
        TimeText.Text = FormatTime(TimeSpan.Zero);
        Progress.Value = 1;
        StatusText.Text = "完成！";
        FocusButtons.IsEnabled = false;

        Sounds.Play(Settings.Sound);
        App.Current.ShowNotification("专注完成", $"{(int)_total.TotalMinutes} 分钟的专注完成了，休息一下吧。");

        bool? taskDone = null;
        if (TaskName.Length > 0)
        {
            var answer = MessageBox.Show(this, $"「{TaskName}」完成了吗？", "专注完成",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            taskDone = answer == MessageBoxResult.Yes;
        }

        FocusButtons.IsEnabled = true;
        _completing = false;
        Log(completed: true, taskDone);
        EndSession();
    }

    void EndSession()
    {
        _tickTimer.Stop();
        _compactTimer.Stop();
        _topmostTimer.Stop();
        _state = FocusState.Setup;

        Topmost = false;
        Opacity = 1;
        _compact = false;
        FocusPanel.Visibility = Visibility.Collapsed;
        CompactPanel.Visibility = Visibility.Collapsed;
        SetupPanel.Visibility = Visibility.Visible;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    void Log(bool completed, bool? taskDone)
    {
        var actual = _total - Remaining;
        SessionLog.Append(new SessionRecord(
            _startedAt, (int)_total.TotalMinutes, Math.Round(actual.TotalMinutes, 2), TaskName, completed, taskDone));
    }

    // ---------- 显示 ----------

    void UpdateDisplay()
    {
        var remaining = Remaining;
        var text = FormatTime(remaining);
        TimeText.Text = text;
        CompactTime.Text = _state == FocusState.Paused ? "⏸ " + text : text;
        Progress.Value = _total > TimeSpan.Zero ? 1 - remaining / _total : 0;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    void SetCompact(bool compact)
    {
        if (_state == FocusState.Setup)
            return;
        _compact = compact;
        FocusPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    void ScheduleCompact()
    {
        _compactTimer.Stop();
        if (_state != FocusState.Setup && Settings.AutoCompact)
            _compactTimer.Start();
    }

    /// <summary>设置变更后更新专注中的外观。</summary>
    public void ApplyAppearance()
    {
        if (_state == FocusState.Setup)
            return;
        Opacity = IsMouseOver ? 1 : Settings.FocusOpacity;
        if (!Settings.AutoCompact)
        {
            _compactTimer.Stop();
            SetCompact(false);
        }
        else if (!_compact && !IsMouseOver)
        {
            ScheduleCompact();
        }
    }

    void OnMouseEnterWindow()
    {
        if (_state == FocusState.Setup)
            return;
        _compactTimer.Stop();
        Opacity = 1;
        if (_compact)
            SetCompact(false);
    }

    void OnMouseLeaveWindow()
    {
        if (_state == FocusState.Setup)
            return;
        Opacity = Settings.FocusOpacity;
        ScheduleCompact();
    }

    /// <summary>全屏程序或其他置顶窗口可能把本窗口压下去，定期重新置顶。</summary>
    void ForceTopmost()
    {
        if (_handle != IntPtr.Zero && _state != FocusState.Setup)
        {
            NativeMethods.SetWindowPos(_handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    static string FormatTime(TimeSpan time)
    {
        int totalSeconds = (int)Math.Ceiling(time.TotalSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
