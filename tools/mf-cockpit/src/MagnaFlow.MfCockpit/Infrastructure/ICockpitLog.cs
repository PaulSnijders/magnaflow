namespace MagnaFlow.MfCockpit.Infrastructure;

/// <summary>The cockpit's own diary (docs/prompts/0001-cmd-cockpit-spawn-errors.md "Cockpit log
/// file"): one line per notable event — startup, config notices, every spawn failure — so "why is
/// it running the default?" is answerable from disk even when a broken project config means
/// nothing else got logged. Best-effort by design: a logging failure must never break the request
/// it was logging about.</summary>
public interface ICockpitLog
{
    string Path { get; }

    void Append(string message);
}

/// <summary>Appends to %APPDATA%\MagnaFlow\mf-cockpit.log (~/.config/magnaflow elsewhere) — same
/// location convention as the machine config (CockpitConfig.DefaultUserConfigDirectory), so it's
/// findable regardless of which project config failed to load. Bounded to ~1 MB: once exceeded, the
/// file is dropped and started fresh rather than growing without limit (KISS — no rotation, no
/// logging framework).</summary>
public sealed class FileCockpitLog : ICockpitLog
{
    public const long MaxBytes = 1024 * 1024;

    public string Path { get; }

    /// <summary>directory overrides the default location — tests use this (and the
    /// MF_COCKPIT_LOG_DIR env var, mirroring MF_COCKPIT_CONFIG) to avoid writing into the real
    /// user's AppData.</summary>
    public FileCockpitLog(string? directory = null)
    {
        var dir = directory
            ?? Environment.GetEnvironmentVariable("MF_COCKPIT_LOG_DIR")
            ?? Config.CockpitConfig.DefaultUserConfigDirectory();
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "mf-cockpit.log");
    }

    public void Append(string message)
    {
        try
        {
            if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                File.Delete(Path);
            File.AppendAllText(Path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort — logging must never break the request it's logging about.
        }
    }
}
