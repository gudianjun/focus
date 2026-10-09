using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Focus;

/// <summary>用户设置，保存在 %AppData%\Focus\settings.json。</summary>
public sealed class AppSettings
{
    public int LastMinutes { get; set; } = 30;
    public bool AutoStart { get; set; }
    public string HotKey { get; set; } = "Ctrl+Alt+F";
    /// <summary>%WINDIR%\Media 下的 wav 文件名；空字符串表示不播放。</summary>
    public string Sound { get; set; } = "Alarm01.wav";
    public double FocusOpacity { get; set; } = 0.9;
    public bool AutoCompact { get; set; } = true;

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Focus");

    static string FilePath => Path.Combine(DataDirectory, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文和 "+" 不转义，方便手动查看
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings != null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // 设置文件损坏时使用默认值
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // 保存失败不影响使用
        }
    }

    void Normalize()
    {
        LastMinutes = Math.Clamp(LastMinutes, 1, 60);
        FocusOpacity = Math.Clamp(FocusOpacity, 0.3, 1.0);
        HotKey ??= "";
        Sound ??= "";
    }
}
