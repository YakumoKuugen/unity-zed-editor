using System;
using System.Collections.Generic;
using System.IO;

namespace SGame.Zed.Editor
{
    /// <summary>
    /// Locates Zed installations. Results are cached until <see cref="Refresh"/> is called.
    /// </summary>
    internal static class ZedDiscovery
    {
        private static readonly List<ZedInstallation> k_Installations = new List<ZedInstallation>();
        private static bool k_Discovered;

        /// <summary>All installations found on this machine, ordered by discovery priority.</summary>
        public static IReadOnlyList<ZedInstallation> GetInstallations()
        {
            if (!k_Discovered)
                Refresh();

            return k_Installations;
        }

        /// <summary>First installation found, or <c>null</c> when Zed is not installed.</summary>
        public static ZedInstallation GetDefaultInstallation()
        {
            var installations = GetInstallations();
            return installations.Count > 0 ? installations[0] : null;
        }

        public static void Refresh()
        {
            k_Installations.Clear();
            k_Discovered = true;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in EnumerateCandidates())
            {
                var executable = ResolveExecutablePath(candidate);
                if (string.IsNullOrEmpty(executable) || !seen.Add(executable))
                    continue;

                k_Installations.Add(new ZedInstallation(executable));
            }
        }

        /// <summary>
        /// Resolves an editor path coming from Unity's preferences (a file, a folder, a macOS bundle
        /// or a bare executable name looked up on <c>PATH</c>).
        /// </summary>
        public static bool TryGetInstallation(string path, out ZedInstallation installation)
        {
            installation = null;

            var executable = ResolveExecutablePath(path);
            if (string.IsNullOrEmpty(executable))
                return false;

            // Reuse cached instances so version queries are not duplicated.
            foreach (var known in GetInstallations())
            {
                if (string.Equals(known.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase))
                {
                    installation = known;
                    return true;
                }
            }

            installation = new ZedInstallation(executable);
            return true;
        }

        /// <summary>
        /// Unity calls <c>IExternalCodeEditor.Initialize</c> on every registered editor with the
        /// currently selected executable, so anything that is not Zed must be rejected here.
        /// </summary>
        public static bool IsZedExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var trimmed = path.Trim().Trim('"');
            var fileName = Path.GetFileNameWithoutExtension(trimmed);
            if (string.IsNullOrEmpty(fileName))
                return false;

            if (fileName.Equals("zed", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("zeditor", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("zed-cli", StringComparison.OrdinalIgnoreCase))
                return true;

            // macOS: /Applications/Zed.app/Contents/MacOS/cli
            if (trimmed.IndexOf(".app" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0
                && (fileName.Equals("cli", StringComparison.OrdinalIgnoreCase) || fileName.Equals("zed", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Custom builds: zed-preview.exe, zed-nightly, ...
            return fileName.StartsWith("zed", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            var environment = Environment.GetEnvironmentVariable("ZED_EXECUTABLE_PATH");
            if (!string.IsNullOrEmpty(environment))
                yield return environment;

#if UNITY_EDITOR_WIN
            foreach (var candidate in EnumerateWindowsCandidates())
                yield return candidate;
#elif UNITY_EDITOR_OSX
            foreach (var candidate in EnumerateMacCandidates())
                yield return candidate;
#else
            foreach (var candidate in EnumerateLinuxCandidates())
                yield return candidate;
#endif

            foreach (var directory in EnumeratePathDirectories())
            {
                foreach (var name in GetExecutableNames())
                    yield return Path.Combine(directory, name);
            }
        }

#if UNITY_EDITOR_WIN
        private static IEnumerable<string> EnumerateWindowsCandidates()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var directories = new[]
            {
                Combine(localAppData, "Programs", "Zed"),
                Combine(localAppData, "Programs", "Zed Preview"),
                Combine(localAppData, "Programs", "Zed Nightly"),
                Combine(localAppData, "Zed", "bin"),
                Combine(programFiles, "Zed"),
                Combine(programFilesX86, "Zed"),
                Combine(localAppData, "Microsoft", "WinGet", "Links"),
                Combine(userProfile, "scoop", "shims"),
                Combine(userProfile, ".cargo", "bin"),
            };

            foreach (var directory in directories)
            {
                if (!string.IsNullOrEmpty(directory))
                    yield return Path.Combine(directory, "zed.exe");
            }
        }
#endif

#if UNITY_EDITOR_OSX
        private static IEnumerable<string> EnumerateMacCandidates()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            yield return "/Applications/Zed.app";
            yield return "/Applications/Zed Preview.app";
            yield return "/Applications/Zed Nightly.app";
            yield return Path.Combine(home, "Applications", "Zed.app");
            yield return "/usr/local/bin/zed";
            yield return "/opt/homebrew/bin/zed";
            yield return Path.Combine(home, ".local", "bin", "zed");
        }
#endif

#if !UNITY_EDITOR_WIN && !UNITY_EDITOR_OSX
        private static IEnumerable<string> EnumerateLinuxCandidates()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            yield return "/usr/bin/zed";
            yield return "/usr/bin/zeditor";
            yield return "/usr/local/bin/zed";
            yield return "/snap/bin/zed";
            yield return "/var/lib/flatpak/exports/bin/dev.zed.Zed";
            yield return Path.Combine(home, ".local", "bin", "zed");
            yield return Path.Combine(home, ".local", "share", "flatpak", "exports", "bin", "dev.zed.Zed");
        }
#endif

        private static IEnumerable<string> EnumeratePathDirectories()
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable))
                yield break;

            foreach (var entry in pathVariable.Split(Path.PathSeparator))
            {
                var directory = entry?.Trim().Trim('"');
                if (string.IsNullOrEmpty(directory))
                    continue;

                yield return directory;
            }
        }

        private static IEnumerable<string> GetExecutableNames()
        {
#if UNITY_EDITOR_WIN
            yield return "zed.exe";
#elif UNITY_EDITOR_OSX
            yield return "zed";
            yield return "cli";
#else
            yield return "zed";
            yield return "zeditor";
#endif
        }

        private static string ResolveExecutablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim().Trim('"');
            if (path.Length == 0)
                return null;

            // Bare executable name ("zed", "zed.exe") -> PATH lookup.
            if (!Path.IsPathRooted(path) && path.IndexOfAny(new[] { '/', '\\' }) < 0)
            {
                var executableName = Path.HasExtension(path) ? path : AddPlatformExtension(path);
                foreach (var directory in EnumeratePathDirectories())
                {
                    var resolved = AcceptExecutable(Path.Combine(directory, executableName));
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
                }

                return null;
            }

            if (Directory.Exists(path))
            {
                // macOS bundle or an installation folder.
                foreach (var candidate in GetPathInsideDirectory(path))
                {
                    var resolved = AcceptExecutable(candidate);
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
                }

                return null;
            }

            return AcceptExecutable(path);
        }

        private static string AcceptExecutable(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
                return null;

            return IsZedExecutable(candidate) ? PathUtility.GetAbsolutePath(candidate) : null;
        }

        private static IEnumerable<string> GetPathInsideDirectory(string directory)
        {
            if (directory.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(directory, "Contents", "MacOS", "cli");
                yield return Path.Combine(directory, "Contents", "MacOS", "zed");
                yield break;
            }

#if UNITY_EDITOR_WIN
            yield return Path.Combine(directory, "zed.exe");
#elif UNITY_EDITOR_OSX
            yield return Path.Combine(directory, "cli");
            yield return Path.Combine(directory, "zed");
#else
            yield return Path.Combine(directory, "zed");
            yield return Path.Combine(directory, "zeditor");
#endif
        }

        private static string AddPlatformExtension(string name)
        {
#if UNITY_EDITOR_WIN
            return name + ".exe";
#else
            return name;
#endif
        }

        private static string Combine(string first, params string[] parts)
        {
            if (string.IsNullOrEmpty(first))
                return null;

            var combined = first;
            foreach (var part in parts)
                combined = Path.Combine(combined, part);

            return combined;
        }
    }
}
