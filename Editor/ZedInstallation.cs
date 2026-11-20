using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Unity.CodeEditor;
using UnityEngine;

namespace SGame.Zed.Editor
{
    /// <summary>
    /// A single Zed installation (the <c>zed</c> / <c>zed.exe</c> CLI or the macOS <c>cli</c> shim).
    /// </summary>
    internal sealed class ZedInstallation
    {
        private static readonly Regex k_VersionPattern = new Regex(@"(\d+)\.(\d+)\.(\d+)(?:[-+][0-9A-Za-z.\-]+)?", RegexOptions.Compiled);
        private static readonly Dictionary<string, string> k_Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> k_PendingVersionQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object k_VersionLock = new object();

        public ZedInstallation(string executablePath)
        {
            ExecutablePath = PathUtility.GetAbsolutePath(executablePath);
            Name = ResolveName(ExecutablePath);
        }

        public string ExecutablePath { get; }

        public string Name { get; }

        public CodeEditor.Installation ToCodeEditorInstallation()
        {
            return new CodeEditor.Installation
            {
                Name = Name,
                Path = ExecutablePath,
            };
        }

        /// <summary>
        /// Opens <paramref name="path"/> (a script) at the given position, or the project folder when
        /// <paramref name="path"/> is empty. Zed resolves <c>path:line:column</c> suffixes natively.
        /// </summary>
        public bool Open(string path, int line, int column, string solutionPath)
        {
            if (!File.Exists(ExecutablePath))
                return false;

            var projectDirectory = GetProjectDirectory(solutionPath);
            var workspaceArgument = PathUtility.Quote(projectDirectory);

            if (string.IsNullOrEmpty(path))
            {
                // "Open C# Project": just bring up the project folder.
                ZedProcess.LaunchAsync(ExecutablePath, projectDirectory, null, workspaceArgument, projectDirectory);
                return true;
            }

            var fileArgument = BuildFileArgument(path, line, column);
            var openWorkspaceFirst = ZedPreferences.OpenWorkspaceFirst ? workspaceArgument : null;

            ZedProcess.LaunchAsync(ExecutablePath, projectDirectory, openWorkspaceFirst, fileArgument, path);
            return true;
        }

        /// <summary>Builds the <c>"path:line:column"</c> argument Zed expects.</summary>
        internal static string BuildFileArgument(string file, int line, int column)
        {
            if (string.IsNullOrEmpty(file))
                return string.Empty;

            var lineNumber = Math.Max(1, line);
            var columnNumber = Math.Max(1, column);

            if (lineNumber <= 1 && columnNumber <= 1)
                return PathUtility.Quote(file);

            if (columnNumber <= 1)
                return PathUtility.Quote($"{file}:{lineNumber}");

            return PathUtility.Quote($"{file}:{lineNumber}:{columnNumber}");
        }

        internal static string GetProjectDirectory(string solutionPath)
        {
            if (!string.IsNullOrEmpty(solutionPath))
            {
                var directory = Path.GetDirectoryName(solutionPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    return PathUtility.GetAbsolutePath(directory);
            }

            return PathUtility.GetAbsolutePath(Path.GetDirectoryName(Application.dataPath) ?? ".");
        }

        /// <summary>
        /// Returns a cached version string (e.g. "0.186.4") or <c>null</c> while the background query
        /// is still running. Never blocks the caller.
        /// </summary>
        public string GetVersionString()
        {
            lock (k_VersionLock)
            {
                if (k_Versions.TryGetValue(ExecutablePath, out var cached))
                    return cached;

                if (k_PendingVersionQueries.Contains(ExecutablePath))
                    return null;

                k_PendingVersionQueries.Add(ExecutablePath);
            }

            var executable = ExecutablePath;
            var thread = new Thread(() =>
            {
                var version = "unknown";
                try
                {
                    if (ZedProcess.TryRun(executable, "--version", null, 5000, out var output, out _))
                    {
                        var match = k_VersionPattern.Match(output);
                        if (match.Success)
                            version = $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
                    }
                }
                catch (Exception)
                {
                    // Ignore: the version is only used for display.
                }

                lock (k_VersionLock)
                {
                    k_Versions[executable] = version;
                    k_PendingVersionQueries.Remove(executable);
                }
            })
            {
                IsBackground = true,
                Name = "Zed.QueryVersion",
            };
            thread.Start();

            return null;
        }

        private static string ResolveName(string executablePath)
        {
            var path = PathUtility.ToUnixPath(executablePath);

            if (path.IndexOf("nightly", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Zed Nightly";
            if (path.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Zed Preview";
            if (path.IndexOf("/dev/", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith("Zed Dev.app/Contents/MacOS/cli", StringComparison.OrdinalIgnoreCase))
                return "Zed Dev";

            return "Zed";
        }
    }
}
