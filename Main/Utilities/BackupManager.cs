using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SaveVaultApp.Utilities;

/// <summary>
/// Low-level, UI-agnostic file operations behind Save Vault's backup engine.
///
/// These helpers are deliberately resilient: a single locked or unreadable
/// file must never abort an entire backup, and we must never delete a live
/// save until a replacement is safely in place. The methods report what
/// failed so callers can warn the user instead of silently producing an
/// incomplete backup.
/// </summary>
public static class BackupManager
{
    /// <summary>Result of a directory copy: how many files were copied and which ones failed.</summary>
    public readonly record struct CopyResult(int FilesCopied, List<string> Errors)
    {
        /// <summary>True when every file copied without error.</summary>
        public bool IsComplete => Errors.Count == 0;
    }

    /// <summary>
    /// Recursively copies <paramref name="sourceDir"/> into <paramref name="destDir"/>.
    /// Per-file failures are collected rather than thrown, so a locked file
    /// (common while a game is running) yields a best-effort backup plus a list
    /// of what could not be copied.
    /// </summary>
    public static CopyResult CopyDirectory(string sourceDir, string destDir)
    {
        var errors = new List<string>();
        int copied = CopyDirectoryInternal(sourceDir, destDir, errors);
        return new CopyResult(copied, errors);
    }

    private static int CopyDirectoryInternal(string sourceDir, string destDir, List<string> errors)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            return 0;

        int copied = 0;

        // Create the destination up-front so empty sub-directories are preserved too.
        try
        {
            Directory.CreateDirectory(destDir);
        }
        catch (Exception ex)
        {
            errors.Add($"{destDir}: {ex.Message}");
            return copied;
        }

        foreach (DirectoryInfo subdir in SafeGetDirectories(dir, errors))
        {
            copied += CopyDirectoryInternal(subdir.FullName, Path.Combine(destDir, subdir.Name), errors);
        }

        foreach (FileInfo file in SafeGetFiles(dir, errors))
        {
            string targetPath = Path.Combine(destDir, file.Name);
            try
            {
                file.CopyTo(targetPath, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                errors.Add($"{file.FullName}: {ex.Message}");
                Debug.WriteLine($"BackupManager: failed to copy '{file.FullName}': {ex.Message}");
            }
        }

        return copied;
    }

    private static IEnumerable<DirectoryInfo> SafeGetDirectories(DirectoryInfo dir, List<string> errors)
    {
        try { return dir.GetDirectories(); }
        catch (Exception ex)
        {
            errors.Add($"{dir.FullName}: {ex.Message}");
            return Array.Empty<DirectoryInfo>();
        }
    }

    private static IEnumerable<FileInfo> SafeGetFiles(DirectoryInfo dir, List<string> errors)
    {
        try { return dir.GetFiles(); }
        catch (Exception ex)
        {
            errors.Add($"{dir.FullName}: {ex.Message}");
            return Array.Empty<FileInfo>();
        }
    }

    /// <summary>Best-effort sum of every file size under <paramref name="path"/>. Unreadable entries are skipped.</summary>
    public static long GetDirectorySize(string path)
    {
        long total = 0;
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
                return 0;

            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { total += file.Length; }
                catch { /* skip files we cannot stat */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BackupManager: error sizing '{path}': {ex.Message}");
        }
        return total;
    }

    /// <summary>True when <paramref name="path"/> contains at least one file (searched recursively).</summary>
    public static bool DirectoryHasFiles(string path)
    {
        try
        {
            return Directory.Exists(path) &&
                   Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns false only when we can positively determine the destination drive
    /// lacks room for the source content plus a safety margin. When free space
    /// cannot be determined (network paths, permission errors, …) this returns
    /// true so a detection failure never blocks a backup.
    /// </summary>
    public static bool HasEnoughFreeSpace(string sourceDir, string destDir, out long requiredBytes)
    {
        // 50 MB of head-room above the raw payload for filesystem overhead.
        const long Headroom = 50L * 1024 * 1024;
        requiredBytes = GetDirectorySize(sourceDir);

        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(destDir));
            if (string.IsNullOrEmpty(root))
                return true;

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return true;

            return drive.AvailableFreeSpace >= requiredBytes + Headroom;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BackupManager: error checking free space for '{destDir}': {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// Deletes the files and sub-directories inside <paramref name="path"/> while keeping
    /// the directory itself. When <paramref name="throwOnError"/> is true the first failure
    /// is rethrown (used where we must know the wipe was clean); otherwise failures are
    /// collected and returned.
    /// </summary>
    public static List<string> ClearDirectoryContents(string path, bool throwOnError)
    {
        var errors = new List<string>();
        var dirInfo = new DirectoryInfo(path);
        if (!dirInfo.Exists)
            return errors;

        foreach (FileInfo file in dirInfo.GetFiles())
        {
            try
            {
                file.Attributes = FileAttributes.Normal; // clear read-only so Delete can't fail on it
                file.Delete();
            }
            catch (Exception ex)
            {
                if (throwOnError) throw;
                errors.Add($"{file.FullName}: {ex.Message}");
            }
        }

        foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
        {
            try
            {
                subDir.Delete(recursive: true);
            }
            catch (Exception ex)
            {
                if (throwOnError) throw;
                errors.Add($"{subDir.FullName}: {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>Formats a byte count as a human-readable string (e.g. "12.4 MB").</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        // Invariant culture so sizes read consistently ("1.5 MB") inside the
        // English status text regardless of the user's regional settings.
        return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", size, units[unit]);
    }
}
