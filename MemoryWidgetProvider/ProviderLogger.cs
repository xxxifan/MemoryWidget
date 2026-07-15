using System.Diagnostics;
using System.Text;
using Windows.Storage;

namespace MemoryWidgetProvider;

internal static class ProviderLogger
{
    private static readonly object SyncRoot = new();

    [Conditional("DEBUG")]
    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{ex}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (SyncRoot)
            {
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [")
                    .Append(level)
                    .Append("] ")
                    .Append(message)
                    .AppendLine()
                    .ToString();
                foreach (var path in GetLogPaths())
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
        }
        catch
        {
            // 日志写入失败时忽略，避免影响 provider 正常逻辑。
        }
    }

    private static IEnumerable<string> GetLogPaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "memory-widget.log");

        string? localStatePath = null;
        try
        {
            localStatePath = ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            // 未打包或宿主上下文不允许时忽略。
        }

        if (!string.IsNullOrWhiteSpace(localStatePath))
        {
            yield return Path.Combine(localStatePath, "memory-widget.log");
        }
    }
}
