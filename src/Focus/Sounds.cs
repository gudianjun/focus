using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;

namespace Focus;

/// <summary>结束提示音，使用 %WINDIR%\Media 下的系统声音。</summary>
public static class Sounds
{
    static readonly string MediaDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    static SoundPlayer? _player;

    public static IReadOnlyList<string> List()
    {
        try
        {
            return Directory.EnumerateFiles(MediaDirectory, "*.wav")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Play(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;

        var path = Path.Combine(MediaDirectory, fileName);
        try
        {
            if (File.Exists(path))
            {
                _player?.Stop();
                _player = new SoundPlayer(path);
                _player.Play();
                return;
            }
        }
        catch (Exception)
        {
            // 播放失败时改用系统提示音
        }
        SystemSounds.Asterisk.Play();
    }
}
