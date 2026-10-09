using System;
using Microsoft.Win32;

namespace Focus;

/// <summary>通过 HKCU\...\Run 注册表项控制开机自启。</summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "FocusMode";
    public const string TrayArgument = "--tray";

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception)
        {
            // 没有权限时忽略
        }
    }
}
