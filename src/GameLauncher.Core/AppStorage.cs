using System.Text.Json;

namespace GameLauncher.Core;

public enum LaunchMode
{
    StartGame,
    IdentifyRunning,
}

/// <summary>Non-sensitive preferences kept on this computer only. The video URL is intentionally not stored.</summary>
public sealed record UserSettings
{
    public string EntryPath { get; init; } = string.Empty;
    public string GameExePath { get; init; } = string.Empty;
    public LaunchMode Mode { get; init; } = LaunchMode.StartGame;

    /// <summary>
    /// R75: pause the 凹分 retries after this many failures in a row and keep the last attempt's recording; 0 = never.
    /// Read by the check process (step 6).
    /// </summary>
    public int PauseAfterFailures { get; init; } = DefaultPauseAfterFailures;

    public const int DefaultPauseAfterFailures = 10;
    public const int MaxPauseAfterFailures = 999;

    /// <summary>R76: pending steps allowed in one battle before it is restarted; 0 = restart at the first.</summary>
    public int AllowedPendingPerBattle { get; init; }

    public const int MaxAllowedPendingPerBattle = 99;

    /// <summary>
    /// R76: pause when one step of the flow has been pending this many times, across battles (each step counts on
    /// its own; a success clears all); 0 = never.
    /// </summary>
    public int PauseAfterPending { get; init; } = DefaultPauseAfterPending;

    public const int DefaultPauseAfterPending = 15;
    public const int MaxPauseAfterPending = 999;
}

public sealed class AppStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const long MaxLogBytes = 512 * 1024;

    public AppStorage(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarRailAutoBattle");
    }

    public string RootDirectory { get; }

    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public string LogPath => Path.Combine(RootDirectory, "logs", "launcher.log");

    public UserSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new UserSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged settings file just means defaults.
        }

        return new UserSettings();
    }

    public void SaveSettings(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; failing to save them must not block launching.
        }
    }

    /// <summary>Appends one diagnostic line. Callers pass file names, not full paths or URLs.</summary>
    public void Log(string stage, string detail)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
            {
                File.Move(LogPath, LogPath + ".old", overwrite: true);
            }

            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}\t{stage}\t{detail.ReplaceLineEndings(" ")}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The UI still shows the error even if the log cannot be written.
        }
    }
}
