using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SGame.Zed.Editor
{
    /// <summary>
    /// Writes a Unity friendly <c>.zed/settings.json</c>. Zed scans every folder of the worktree
    /// unless excluded, and a Unity project contains tens of thousands of files under
    /// <c>Library</c> and <c>Temp</c> - excluding them makes Zed usable on large projects.
    /// </summary>
    internal static class ZedProjectSettings
    {
        public const string k_FolderName = ".zed";
        public const string k_FileName = "settings.json";

        public static string GetFolder(string projectDirectory)
        {
            return Path.Combine(projectDirectory, k_FolderName);
        }

        public static string GetPath(string projectDirectory)
        {
            return Path.Combine(GetFolder(projectDirectory), k_FileName);
        }

        public static bool Exists(string projectDirectory)
        {
            return File.Exists(GetPath(projectDirectory));
        }

        /// <summary>
        /// Creates <c>.zed/settings.json</c>. Existing files are left untouched unless
        /// <paramref name="overwrite"/> is <c>true</c>.
        /// </summary>
        public static bool Write(string projectDirectory, bool overwrite, bool logResult = true)
        {
            var path = GetPath(projectDirectory);

            if (File.Exists(path) && !overwrite)
            {
                if (logResult)
                    Debug.Log($"[Zed] '{path}' already exists, keeping the existing settings.");

                return false;
            }

            try
            {
                Directory.CreateDirectory(GetFolder(projectDirectory));
                File.WriteAllText(path, BuildSettings(projectDirectory), new UTF8Encoding(false));

                if (logResult)
                    Debug.Log($"[Zed] Wrote '{path}'.");

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Zed] Could not write '{path}': {exception.Message}");
                return false;
            }
        }

        internal static string BuildSettings(string projectDirectory)
        {
            // Keep this in sync with Zed's settings schema: unknown keys are reported as errors in the
            // editor, so only stable, long lived settings are emitted here.
            const string settings = @"{
  ""file_scan_exclusions"": [
    ""**/.git"",
    ""**/.hg"",
    ""**/.svn"",
    ""**/.vs"",
    ""**/.idea"",
    ""**/Library"",
    ""**/Temp"",
    ""**/Logs"",
    ""**/obj"",
    ""**/UserSettings"",
    ""**/MemoryCaptures"",
    ""**/Recordings"",
    ""**/Build"",
    ""**/Builds"",
    ""**/*.meta"",
    ""**/*.unitypackage"",
    ""**/*.csproj"",
    ""**/*.sln"",
    ""**/*.slnx""
  ],
  ""languages"": {
    ""C#"": {
      ""tab_size"": 4,
      ""hard_tabs"": false
    }
  }
}
";

            return settings;
        }
    }
}
