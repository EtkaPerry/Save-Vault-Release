using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Shared, best-effort helper for identifying an application from its executable path:
    /// deciding whether the exe is obviously a non-app helper process, and resolving a
    /// human-friendly display name from the Steam manifest / exe metadata / the filename.
    ///
    /// This consolidates logic that was previously duplicated (and inconsistent) across
    /// the filesystem, registry, and view-model scan paths so every path filters and names
    /// the same way.
    /// </summary>
    public static class AppIdentity
    {
        /// <summary>
        /// Exact (extension-less, lower-invariant) executable names that are never user-facing
        /// applications: browser/runtime helper processes, crash handlers, redistributables and
        /// runtime hosts. Kept deliberately small and exact so real games are never matched.
        /// </summary>
        private static readonly HashSet<string> ObviousJunkNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "msedgewebview2",
            "steamwebhelper",
            "ui32",
            "ui64",
            "crashpad_handler",
            "crashhandler",
            "crashreporter",
            "unitycrashhandler32",
            "unitycrashhandler64",
            "unitycrashhandler",
            "werfault",
            "wermgr",
            "vc_redist",
            "vcredist",
            "dxsetup",
            "dxwebsetup",
            "dotnet",
            "dotnetfx",
            "ngen",
            "mscorsvw",
            "dwm",
            "conhost",
            "perfwatson2"
        };

        /// <summary>
        /// Unambiguous filename suffixes (matched on the lower-invariant filename, with extension)
        /// that identify helper/crash/elevation processes. These are specific enough not to hit
        /// real game executables (unlike broad substrings such as "update"/"launcher"/"helper").
        /// </summary>
        private static readonly string[] ObviousJunkSuffixes =
        {
            "webhelper.exe",
            "webview2.exe",
            "crashhandler.exe",
            "crashpad.exe",
            "crashreport.exe",
            "crashreporter.exe",
            "-gpu.exe",
            "_gpu.exe",
            "plugin-container.exe",
            "notification-helper.exe",
            "notification_helper.exe",
            "elevation_service.exe"
        };

        /// <summary>
        /// Returns true only for executables that are clearly NOT user-facing applications
        /// (browser/runtime helpers, crash handlers, redistributables, runtime hosts).
        /// Conservative by design: when in doubt it returns false so the exe still shows up,
        /// matching the permissive "just drop obvious junk" policy.
        /// </summary>
        public static bool IsObviousJunk(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            string fileName;
            string nameNoExt;
            try
            {
                fileName = Path.GetFileName(exePath).ToLowerInvariant();
                nameNoExt = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
            }
            catch
            {
                return false;
            }

            if (ObviousJunkNames.Contains(nameNoExt))
                return true;

            foreach (var suffix in ObviousJunkSuffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Best-effort human-friendly display name for an executable. Never throws and never
        /// returns an empty string. Resolution order (highest confidence first):
        /// 1. Steam appmanifest name (matched via the install dir in the path).
        /// 2. Exe ProductName metadata (if present and not generic).
        /// 3. Exe FileDescription metadata (if present and not generic).
        /// 4. A prettified version of the filename.
        /// </summary>
        /// <param name="exePath">Full path to the executable.</param>
        /// <param name="fallback">
        /// Optional caller-supplied name (e.g. derived from a directory). Used in preference to
        /// the prettified filename when steps 1-3 yield nothing and it looks like a real name.
        /// </param>
        public static string ResolveDisplayName(string exePath, string? fallback = null)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();

            // 1. Steam manifest (highest confidence).
            string? steamName = TryGetSteamName(exePath);
            if (!string.IsNullOrWhiteSpace(steamName))
                return steamName.Trim();

            // 2 & 3. Executable metadata.
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                string bareName = Path.GetFileNameWithoutExtension(exePath);

                string? product = CleanMetadataValue(info.ProductName, bareName);
                if (product != null)
                    return product;

                string? description = CleanMetadataValue(info.FileDescription, bareName);
                if (description != null)
                    return description;
            }
            catch
            {
                // Metadata is best-effort; fall through to the filename.
            }

            // 4. Fallback / prettified filename.
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                string trimmed = fallback.Trim();
                // Prefer a directory-derived fallback when it looks like a real name (contains a
                // space or is mixed/longer than a bare token), otherwise prettify the filename.
                if (trimmed.Contains(' ') || trimmed.Length > 2)
                    return trimmed;
            }

            return PrettifyFileName(Path.GetFileNameWithoutExtension(exePath));
        }

        /// <summary>
        /// Validates a FileVersionInfo metadata value, returning a trimmed value or null when it
        /// is empty, a known-generic placeholder, a pure version string, or just the exe filename.
        /// </summary>
        private static string? CleanMetadataValue(string? value, string bareExeName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string trimmed = value.Trim();
            string lower = trimmed.ToLowerInvariant();

            if (lower == "application" || lower == "app")
                return null;

            // Reject when it just echoes the executable filename.
            if (lower == bareExeName.ToLowerInvariant())
                return null;

            // Reject pure version strings (e.g. "1.0.0.0").
            if (Regex.IsMatch(trimmed, @"^[vV]?\d+(\.\d+)*$"))
                return null;

            return trimmed;
        }

        /// <summary>
        /// If the path lives under a Steam library (a <c>steamapps\common\&lt;installdir&gt;</c>
        /// segment), reads the sibling <c>appmanifest_*.acf</c> files and returns the
        /// <c>"name"</c> whose <c>"installdir"</c> matches the path's install folder. Returns
        /// null if not a Steam path or no match is found. All IO is wrapped in try/catch.
        /// </summary>
        private static string? TryGetSteamName(string exePath)
        {
            try
            {
                // Split into path segments and locate "steamapps" followed by "common".
                string[] parts = exePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                int steamAppsIndex = -1;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (string.Equals(parts[i], "steamapps", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(parts[i + 1], "common", StringComparison.OrdinalIgnoreCase))
                    {
                        steamAppsIndex = i;
                        break;
                    }
                }

                // Need the install-dir folder that follows "...\steamapps\common\".
                if (steamAppsIndex < 0 || steamAppsIndex + 2 >= parts.Length)
                    return null;

                string installDirFromPath = parts[steamAppsIndex + 2];

                // Reconstruct the absolute steamapps directory from the original path so we read
                // the correct library folder (handles multiple Steam libraries).
                int commonPos = exePath.IndexOf(
                    "steamapps" + Path.DirectorySeparatorChar + "common",
                    StringComparison.OrdinalIgnoreCase);
                if (commonPos < 0)
                {
                    commonPos = exePath.IndexOf(
                        "steamapps" + Path.AltDirectorySeparatorChar + "common",
                        StringComparison.OrdinalIgnoreCase);
                }
                if (commonPos < 0)
                    return null;

                // Slice off everything from "common" onward to get the steamapps directory.
                string steamAppsDir = exePath.Substring(0, commonPos + "steamapps".Length);

                if (!Directory.Exists(steamAppsDir))
                    return null;

                foreach (var manifest in Directory.GetFiles(steamAppsDir, "appmanifest_*.acf"))
                {
                    try
                    {
                        string content = File.ReadAllText(manifest);

                        var installDirMatch = Regex.Match(content, "\"installdir\"\\s+\"([^\"]+)\"",
                            RegexOptions.IgnoreCase);
                        if (!installDirMatch.Success)
                            continue;

                        if (!string.Equals(installDirMatch.Groups[1].Value, installDirFromPath,
                                StringComparison.OrdinalIgnoreCase))
                            continue;

                        var nameMatch = Regex.Match(content, "\"name\"\\s+\"([^\"]+)\"",
                            RegexOptions.IgnoreCase);
                        if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
                            return nameMatch.Groups[1].Value.Trim();
                    }
                    catch
                    {
                        // Skip unreadable/malformed manifest and try the next one.
                    }
                }
            }
            catch
            {
                // Any IO/parse failure: treat as "no Steam name".
            }

            return null;
        }

        /// <summary>
        /// Turns a raw filename token into a readable display name: separators become spaces,
        /// camelCase and letter-to-digit boundaries get spaced, multiple spaces collapse, and
        /// all-lowercase words are Title-cased (acronyms/mixed case are left alone). Casing uses
        /// the invariant culture to avoid locale issues (e.g. the Turkish dotted-I problem).
        /// </summary>
        private static string PrettifyFileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw ?? string.Empty;

            // Replace common separators with spaces.
            string working = raw.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');

            // Insert spaces at camelCase and letter->digit boundaries.
            var sb = new StringBuilder(working.Length + 8);
            for (int i = 0; i < working.Length; i++)
            {
                char c = working[i];
                if (i > 0)
                {
                    char prev = working[i - 1];
                    bool lowerToUpper = char.IsLower(prev) && char.IsUpper(c);
                    bool letterToDigit = char.IsLetter(prev) && char.IsDigit(c);
                    if (lowerToUpper || letterToDigit)
                        sb.Append(' ');
                }
                sb.Append(c);
            }

            // Collapse runs of whitespace and trim.
            string spaced = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (spaced.Length == 0)
                return raw.Trim();

            // Title-case words that are entirely lowercase; leave acronyms/mixed case untouched.
            string[] words = spaced.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length == 0)
                    continue;

                bool allLower = true;
                foreach (char ch in word)
                {
                    if (char.IsLetter(ch) && !char.IsLower(ch))
                    {
                        allLower = false;
                        break;
                    }
                }

                if (allLower)
                {
                    words[i] = char.ToUpperInvariant(word[0]) + word.Substring(1);
                }
            }

            return string.Join(' ', words);
        }
    }
}
