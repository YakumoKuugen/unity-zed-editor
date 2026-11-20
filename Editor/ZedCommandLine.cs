using SGame.Zed.Editor.ProjectGeneration;
using UnityEditor;
using UnityEngine;

namespace SGame.Zed.Editor
{
    /// <summary>
    /// Entry points usable from the command line, for example:
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath "D:\MyGame" -executeMethod SGame.Zed.Editor.ZedCommandLine.GenerateProjectFiles
    /// </code>
    /// </summary>
    public static class ZedCommandLine
    {
        /// <summary>Regenerates the <c>.sln</c> and every <c>.csproj</c> of the current project.</summary>
        public static void GenerateProjectFiles()
        {
            var generator = new ZedProjectGenerator();
            generator.Sync(force: true);
            Debug.Log($"[Zed] Solution generated: {generator.SolutionFile()}");
        }

        /// <summary>
        /// Writes the solution and only the projects that are missing, keeping the <c>.csproj</c> files
        /// produced by another generator untouched.
        /// </summary>
        public static void EnsureProjectFiles()
        {
            var generator = new ZedProjectGenerator();
            generator.Sync(force: false);
            Debug.Log($"[Zed] Solution ensured: {generator.SolutionFile()}");
        }

        /// <summary>Creates <c>.zed/settings.json</c> when it does not exist yet.</summary>
        public static void WriteProjectSettings()
        {
            ZedProjectSettings.Write(ZedProjectGenerator.GetDefaultProjectDirectory(), overwrite: false);
        }

        /// <summary>Overwrites <c>.zed/settings.json</c> with the Unity friendly defaults.</summary>
        public static void OverwriteProjectSettings()
        {
            ZedProjectSettings.Write(ZedProjectGenerator.GetDefaultProjectDirectory(), overwrite: true);
        }
    }
}
