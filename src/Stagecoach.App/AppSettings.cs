using System.Text.Json;

namespace Stagecoach.App;

public enum CloseBehavior { Exit, NotificationArea }
public enum AppTheme { System, Light, Dark }
public enum AppAccent { Rust, Blue, Green, Purple }

public sealed record AppSettings(
    AppTheme Theme = AppTheme.System,
    AppAccent Accent = AppAccent.Rust,
    bool MinimizeToNotificationArea = true,
    CloseBehavior CloseBehavior = CloseBehavior.NotificationArea,
    bool BackgroundSyncEnabled = true,
    int BackgroundSyncMinutes = 30,
    bool StartMinimized = false);

public sealed class AppSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new AppSettings();
        await using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024) throw new JsonException("Stagecoach settings exceed the safe size limit.");
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken) ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
    }
}
