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
        /// Lower-invariant path fragments that only ever belong to developer tools, runtimes,
        /// SDKs and OS components — never to a game. Any executable whose full path contains one
        /// of these is treated as junk. This is what removes the bundled Unix utilities that ship
        /// with Git for Windows (<c>\Git\usr\bin\rm.exe</c>, <c>pwd.exe</c>, <c>scp.exe</c> …),
        /// MSYS2/Cygwin trees, the .NET/PowerShell runtimes, and Windows SDKs that otherwise flood
        /// the application list with hundreds of non-game console programs.
        /// Compared using the platform separator so a folder must match as a whole segment.
        /// </summary>
        private static readonly string[] JunkPathFragments =
        {
            @"\git\usr\",
            @"\git\mingw32\",
            @"\git\mingw64\",
            @"\git\cmd\",
            @"\git\bin\",
            @"\msys64\",
            @"\msys32\",
            @"\msys2\",
            @"\cygwin\",
            @"\cygwin64\",
            @"\dotnet\sdk\",
            @"\dotnet\shared\",
            @"\dotnet\host\",
            @"\powershell\7\",
            @"\windows kits\",
            @"\microsoft sdks\",
            @"\microsoft visual studio\",
            @"\windows defender\",
            @"\nodejs\",
            @"\node_modules\",
            @"\llvm\bin\",
            @"\cmake\bin\",
            @"\python3",
            @"\windowspowershell\",
            @"\system32\",
            @"\syswow64\",
            @"\winsxs\",
            @"\driverstore\",
        };

        /// <summary>
        /// Lower-invariant filename substrings that mark installers, redistributables, anti-cheat
        /// shims and crash helpers. These never name an actual game executable, so a Contains match
        /// is safe. Kept separate from <see cref="ObviousJunkNames"/> (which is exact-match only).
        /// </summary>
        private static readonly string[] JunkNameSubstrings =
        {
            "unins",            // unins000.exe / uninstall.exe
            "uninstall",
            "installer",
            "vcredist",
            "vc_redist",
            "dxsetup",
            "dxwebsetup",
            "easyanticheat",
            "anticheatinstaller",
            "battleye",
            "beservice",
            "redistributable",
            "crashhandler",
            "crashreport",
            "crashpad",
            "overwolf",         // Overwolf overlay/companion components, not games
        };

        /// <summary>
        /// Lower-invariant path fragments that identify a recognised game library / store install
        /// root. Executables living under one of these are given the benefit of the doubt: the
        /// console-subsystem heuristic is skipped for them so that the rare console-mode game
        /// (roguelikes, some emulators) is still listed.
        /// </summary>
        private static readonly string[] GameLibraryFragments =
        {
            @"\steamapps\common\",
            @"\steamlibrary\",
            @"\epic games\",
            @"\gog galaxy\games\",
            @"\gog games\",
            @"\origin games\",
            @"\ea games\",
            @"\electronic arts\",
            @"\ubisoft game launcher\games\",
            @"\rockstar games\",
            @"\bethesda.net launcher\games\",
            @"\blizzard entertainment\",
            @"\xbox games\",
            @"\xboxgames\",
            @"\amazon games\",
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

            // Tool / runtime / OS trees that never contain games (Git's bundled Unix tools,
            // MSYS2, the .NET & PowerShell runtimes, Windows SDKs, …).
            if (IsJunkPath(exePath))
                return true;

            // Installer / redistributable / anti-cheat / crash-helper filenames.
            foreach (var fragment in JunkNameSubstrings)
            {
                if (fileName.Contains(fragment, StringComparison.Ordinal))
                    return true;
            }

            // Console (CUI) executables are almost always command-line utilities, not games.
            // Drop them — unless they sit inside a recognised game library, where the rare
            // console-mode game should still be listed.
            if (!IsLikelyGameLibraryPath(exePath) && IsConsoleSubsystem(exePath))
                return true;

            return false;
        }

        /// <summary>
        /// True when the executable lives under a developer-tool / runtime / OS path that never
        /// contains a game (see <see cref="JunkPathFragments"/>).
        /// </summary>
        private static bool IsJunkPath(string exePath)
        {
            string lower = exePath.ToLowerInvariant();
            foreach (var fragment in JunkPathFragments)
            {
                if (lower.Contains(fragment, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when the path sits under a recognised game library / store install root
        /// (see <see cref="GameLibraryFragments"/>). Accepts a full executable path or a directory.
        /// </summary>
        public static bool IsLikelyGameLibraryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string lower = path.ToLowerInvariant();
            foreach (var fragment in GameLibraryFragments)
            {
                if (lower.Contains(fragment, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Reads the PE optional header and returns true if the image's Subsystem is
        /// IMAGE_SUBSYSTEM_WINDOWS_CUI (a console application). Best-effort and exception-safe:
        /// any IO/parse problem returns false so a readable-but-odd file is never dropped on a
        /// false positive. Only a few bytes are read (DOS stub pointer + the Subsystem field).
        /// </summary>
        private static bool IsConsoleSubsystem(string exePath)
        {
            const int ImageSubsystemWindowsCui = 3;
            try
            {
                using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                // 'MZ' DOS signature.
                Span<byte> buffer = stackalloc byte[4];
                if (stream.Read(buffer.Slice(0, 2)) != 2 || buffer[0] != 'M' || buffer[1] != 'Z')
                    return false;

                // e_lfanew (offset to the PE header) lives at 0x3C.
                stream.Seek(0x3C, SeekOrigin.Begin);
                if (stream.Read(buffer) != 4)
                    return false;
                uint peHeaderOffset = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));

                // Verify the 'PE\0\0' signature.
                stream.Seek(peHeaderOffset, SeekOrigin.Begin);
                if (stream.Read(buffer) != 4 || buffer[0] != 'P' || buffer[1] != 'E' || buffer[2] != 0 || buffer[3] != 0)
                    return false;

                // Subsystem is a UInt16 at a fixed offset within the optional header, identical for
                // PE32 and PE32+: PE sig (4) + COFF header (20) + 68 = peHeaderOffset + 92.
                stream.Seek(peHeaderOffset + 92, SeekOrigin.Begin);
                if (stream.Read(buffer.Slice(0, 2)) != 2)
                    return false;
                int subsystem = buffer[0] | (buffer[1] << 8);

                return subsystem == ImageSubsystemWindowsCui;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a stable "install root" key for an executable, used to collapse the many
        /// executables that ship inside a single game/app folder down to one list entry. When the
        /// path runs through a known library (…\steamapps\common\&lt;Game&gt;\…, …\Epic Games\
        /// &lt;Game&gt;\…, etc.) the first folder under that library is returned; otherwise the
        /// executable's own directory is used. Always lower-invariant with no trailing separator.
        /// </summary>
        public static string GameRootFolder(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return string.Empty;

            try
            {
                string directory = Path.GetDirectoryName(exePath) ?? exePath;
                string normalized = directory.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                string lower = normalized.ToLowerInvariant();

                foreach (var fragment in GameLibraryFragments)
                {
                    int idx = lower.IndexOf(fragment, StringComparison.Ordinal);
                    if (idx < 0)
                        continue;

                    int rootStart = idx + fragment.Length;
                    if (rootStart >= normalized.Length)
                        continue;

                    int nextSep = normalized.IndexOf(Path.DirectorySeparatorChar, rootStart);
                    string root = nextSep < 0 ? normalized : normalized.Substring(0, nextSep);
                    return root.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
                }

                // Not under a known library: climb out of binary-only subfolders (…\Binaries\
                // Win64, …\bin\x64, …) so an engine exe buried a few levels down collapses with the
                // launcher next to it. The climb stops at the first "real" folder name, so sibling
                // products under a shared vendor folder are NOT merged.
                string current = normalized;
                for (int i = 0; i < 3; i++)
                {
                    string leaf = Path.GetFileName(current).ToLowerInvariant();
                    if (!BinarySubfolderNames.Contains(leaf))
                        break;

                    string? parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(parent))
                        break;
                    current = parent;
                }

                string candidate = current.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

                // Never collapse by a shared "catch-all" folder (Desktop, Downloads, Documents,
                // a Program Files root, a drive root): several unrelated games can live loose in
                // those, so fall back to the full executable path and keep each as its own entry.
                if (IsCatchAllFolder(candidate))
                    return exePath.ToLowerInvariant();

                return candidate;
            }
            catch
            {
                return exePath.ToLowerInvariant();
            }
        }

        private static readonly HashSet<string> CatchAllFolders = BuildCatchAllFolders();

        private static HashSet<string> BuildCatchAllFolders()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? p)
            {
                if (!string.IsNullOrEmpty(p))
                    set.Add(p.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant());
            }

            try
            {
                Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            }
            catch
            {
                // Best effort: any folder we fail to resolve simply isn't treated as catch-all.
            }

            return set;
        }

        private static bool IsCatchAllFolder(string normalizedLowerDir)
        {
            if (CatchAllFolders.Contains(normalizedLowerDir))
                return true;

            // A drive root such as "d:" (after trimming the trailing separator).
            return normalizedLowerDir.Length <= 3 && normalizedLowerDir.EndsWith(":", StringComparison.Ordinal);
        }

        /// <summary>
        /// Folder names that only ever hold build output / binaries, used by
        /// <see cref="GameRootFolder"/> to climb up to the real install folder. Deliberately
        /// excludes ambiguous names like "game"/"data" that could merge distinct products.
        /// </summary>
        private static readonly HashSet<string> BinarySubfolderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "binaries", "win64", "win32", "win", "x64", "x86", "x86_64", "release", "retail"
        };

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
