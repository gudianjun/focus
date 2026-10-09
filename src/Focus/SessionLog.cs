using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Focus;

/// <summary>一次专注的记录，供以后的统计功能使用。</summary>
public sealed record SessionRecord(
    DateTime StartedAt,
    int PlannedMinutes,
    double ActualMinutes,
    string Task,
    bool Completed,
    bool? TaskDone);

/// <summary>把专注记录逐行追加到 %AppData%\Focus\history.jsonl。</summary>
public static class SessionLog
{
    static string FilePath => Path.Combine(AppSettings.DataDirectory, "history.jsonl");

    static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Append(SessionRecord record)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.AppendAllText(FilePath, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
        }
        catch (Exception)
        {
            // 记录失败不影响使用
        }
    }
}
