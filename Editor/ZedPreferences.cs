using SGame.Zed.Editor.ProjectGeneration;
using UnityEditor;

namespace SGame.Zed.Editor
{
    /// <summary>Editor preferences owned by the Zed integration.</summary>
    internal static class ZedPreferences
    {
        private const string k_GenerationFlags = "SGame.Zed.Editor.ProjectGenerationFlags";
        private const string k_WriteProjectSettings = "SGame.Zed.Editor.WriteZedProjectSettings";
        private const string k_OpenWorkspaceFirst = "SGame.Zed.Editor.OpenWorkspaceFirst";

        public static ProjectGenerationFlag GenerationFlags
        {
            get
            {
                var stored = EditorPrefs.GetInt(k_GenerationFlags, (int)ProjectGenerationFlag.Default);
                return (ProjectGenerationFlag)stored;
            }
            set { EditorPrefs.SetInt(k_GenerationFlags, (int)value); }
        }

        public static bool HasFlag(ProjectGenerationFlag flag)
        {
            return (GenerationFlags & flag) == flag;
        }

        public static void ToggleFlag(ProjectGenerationFlag flag)
        {
            GenerationFlags ^= flag;
        }

        /// <summary>
        /// When enabled, a <c>.zed/settings.json</c> is created next to <c>Assets</c> (only when it
        /// does not exist yet) so that Zed skips <c>Library</c>, <c>Temp</c>, <c>obj</c>, ...
        /// </summary>
        public static bool AutoWriteProjectSettings
        {
            get { return EditorPrefs.GetBool(k_WriteProjectSettings, true); }
            set { EditorPrefs.SetBool(k_WriteProjectSettings, value); }
        }

        /// <summary>
        /// Zed derives the workspace (and therefore the language server root) from the first path
        /// it is given. Opening the project folder before a script keeps IntelliSense working on a
        /// cold start, at the cost of one extra CLI invocation.
        /// </summary>
        public static bool OpenWorkspaceFirst
        {
            get { return EditorPrefs.GetBool(k_OpenWorkspaceFirst, true); }
            set { EditorPrefs.SetBool(k_OpenWorkspaceFirst, value); }
        }
    }
}
