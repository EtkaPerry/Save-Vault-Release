using System;

namespace SaveVaultApp.Services;

/// <summary>
/// Bridges the extension Lua API (a process-wide singleton in <see cref="LuaEngine"/>) to the live
/// <c>MainWindowViewModel</c> (a large, non-singleton object). The view-model registers providers
/// and actions here when it is constructed; <see cref="LuaEngine"/> calls them to expose games and
/// backups to extensions without the Services layer taking a hard dependency on the ViewModels layer.
///
/// Providers that return collections hand back JSON strings (extensions parse them with the injected
/// <c>json</c> Lua helper). Every member is safe to call before the view-model has registered
/// anything — it returns an empty/false default instead of throwing.
/// </summary>
public class ExtensionHostService
{
    private static readonly Lazy<ExtensionHostService> _instance = new(() => new ExtensionHostService());
    public static ExtensionHostService Instance => _instance.Value;

    private ExtensionHostService() { }

    /// <summary>Returns a JSON array of the installed games/apps.</summary>
    public Func<string>? GamesProvider { get; set; }

    /// <summary>Maps an app/game name to its save path (empty string when unknown).</summary>
    public Func<string, string>? SavePathProvider { get; set; }

    /// <summary>Returns a JSON array of backups for the named app/game.</summary>
    public Func<string, string>? BackupsProvider { get; set; }

    /// <summary>Triggers an immediate backup for the named app/game; returns whether it ran.</summary>
    public Func<string, bool>? BackupTrigger { get; set; }

    /// <summary>Restores the named app/game from the given backup path; returns whether it ran.</summary>
    public Func<string, string, bool>? RestoreTrigger { get; set; }

    public string GetGamesJson()
    {
        try { return GamesProvider?.Invoke() ?? "[]"; }
        catch (Exception ex) { LoggingService.Instance.Error($"getGames failed: {ex.Message}"); return "[]"; }
    }

    public string GetSavePath(string appName)
    {
        try { return SavePathProvider?.Invoke(appName) ?? ""; }
        catch (Exception ex) { LoggingService.Instance.Error($"getSavePath failed: {ex.Message}"); return ""; }
    }

    public string GetBackupsJson(string appName)
    {
        try { return BackupsProvider?.Invoke(appName) ?? "[]"; }
        catch (Exception ex) { LoggingService.Instance.Error($"getBackups failed: {ex.Message}"); return "[]"; }
    }

    public bool TriggerBackup(string appName)
    {
        try { return BackupTrigger?.Invoke(appName) ?? false; }
        catch (Exception ex) { LoggingService.Instance.Error($"createBackupNow failed: {ex.Message}"); return false; }
    }

    public bool TriggerRestore(string appName, string backupPath)
    {
        try { return RestoreTrigger?.Invoke(appName, backupPath) ?? false; }
        catch (Exception ex) { LoggingService.Instance.Error($"restoreBackup failed: {ex.Message}"); return false; }
    }
}
