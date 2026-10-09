using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Focus;

public partial class App : Application
{
    const string MutexName = @"Local\Focus.SingleInstance";
    const string ShowEventName = @"Local\Focus.ShowWindow";

    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    MainWindow? _main;
    TrayIcon? _tray;
    SettingsWindow? _settingsWindow;

    public static new App Current => (App)Application.Current;
    public AppSettings Settings { get; private set; } = new();
    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 只允许一个实例：已经在运行时通知它显示窗口，然后退出
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch (Exception) { }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(WaitForShowRequests) { IsBackground = true, Name = "ShowRequests" }.Start();

        base.OnStartup(e);
        System.Windows.Forms.Application.EnableVisualStyles();

        Settings = AppSettings.Load();
        if (Settings.AutoStart)
            AutoStart.Set(true); // 程序被移动过时更新路径

        _main = new MainWindow();
        _tray = new TrayIcon(_main);
        _main.Initialize();
        if (!_main.ApplyHotKey())
            ReportHotKeyFailure();

        if (!e.Args.Contains(AutoStart.TrayArgument, StringComparer.OrdinalIgnoreCase))
            _main.ShowFront();
    }

    void WaitForShowRequests()
    {
        while (_showEvent!.WaitOne())
            Dispatcher.BeginInvoke(() => _main?.ShowFront());
    }

    public void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(Settings);
        if (_main is { IsVisible: true })
            _settingsWindow.Owner = _main;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;

        // 设置期间暂停全局快捷键，否则按下当前组合键时输入框收不到
        _main?.SuspendHotKey();
        if (_settingsWindow.ShowDialog() == true)
        {
            ApplySettings();
        }
        else if (_main != null && !_main.ApplyHotKey())
        {
            ReportHotKeyFailure();
        }
    }

    void ApplySettings()
    {
        Settings.Save();
        AutoStart.Set(Settings.AutoStart);
        if (_main != null)
        {
            _main.ApplyAppearance();
            if (!_main.ApplyHotKey())
                ReportHotKeyFailure();
        }
    }

    void ReportHotKeyFailure()
    {
        if (!string.IsNullOrWhiteSpace(Settings.HotKey))
            _tray?.ShowNotification("快捷键不可用", $"快捷键 {Settings.HotKey} 已被其他程序占用，请在设置中更换。");
    }

    public void ShowNotification(string title, string text) => _tray?.ShowNotification(title, text);

    public void ExitApp()
    {
        if (_main != null && !_main.ConfirmExit())
            return;

        IsExiting = true;
        _tray?.Dispose();
        _tray = null;
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_mutex != null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
