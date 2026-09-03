using System.Text;
using Stagecoach.Infrastructure;

namespace Stagecoach.App;

/// <summary>
/// Last-resort diagnostics. Stagecoach previously had no crash record at all, so a failure on an
/// operator's machine left nothing behind to read. Writes redacted exception detail to
/// <c>%LOCALAPPDATA%\Stagecoach\logs</c>, and never throws from the failure path itself.
/// </summary>
internal static class CrashLog
{
    private static readonly Lock Gate = new();

    public static string LogPath => Path.Combine(StagecoachPaths.LogsDirectory, "stagecoach-errors.log");

    public static void Record(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(StagecoachPaths.LogsDirectory);
            var builder = new StringBuilder()
                .Append('[').Append(DateTimeOffset.Now.ToString("u")).Append("] ").AppendLine(context)
                .AppendLine(Describe(exception))
                .AppendLine(new string('-', 72));

            lock (Gate)
            {
                // Keep the log bounded so it can never fill a disk.
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath, builder.ToString());
            }
        }
        catch (Exception)
        {
            // Diagnostics must never take the application down.
        }
    }

    private static string Describe(Exception exception)
    {
        var builder = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            builder.Append(current.GetType().FullName).Append(": ").AppendLine(Redact(current.Message));
            if (current.StackTrace is { } stack) builder.AppendLine(Redact(stack));
        }

        return builder.ToString();
    }

    // Stack traces and messages should never carry a secret, but this is the file an operator is
    // most likely to paste into a chat window, so strip anything token-shaped anyway.
    private static string Redact(string value) => string.Join(
        Environment.NewLine,
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
                !line.Contains("accessToken", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("password", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Authorization:", StringComparison.OrdinalIgnoreCase)));
}
