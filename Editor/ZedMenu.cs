using Unity.CodeEditor;
using UnityEditor;

namespace SGame.Zed.Editor
{
    /// <summary>Menu entries under <c>Tools/Zed</c>.</summary>
    internal static class ZedMenu
    {
        private const string k_Root = "Tools/Zed/";
        private const string k_OpenProject = k_Root + "Open Project in Zed";

        [MenuItem(k_OpenProject, false, 1)]
        private static void OpenProject()
        {
            ZedEditor.OpenProjectInZed();
        }

        [MenuItem(k_OpenProject, true, 1)]
        private static bool OpenProjectValidate()
        {
            return ZedDiscovery.TryGetInstallation(CodeEditor.CurrentEditorInstallation, out _)
                   || ZedDiscovery.GetDefaultInstallation() != null;
        }

        [MenuItem(k_Root + "Regenerate Project Files", false, 20)]
        private static void RegenerateProjectFiles()
        {
            ZedEditor.RegenerateProjectFiles();
        }

        [MenuItem(k_Root + "Write .zed/settings.json", false, 40)]
        private static void WriteProjectSettings()
        {
            ZedEditor.WriteProjectSettings(overwrite: false);
        }

        [MenuItem(k_Root + "Overwrite .zed/settings.json", false, 41)]
        private static void OverwriteProjectSettings()
        {
            ZedEditor.WriteProjectSettings(overwrite: true);
        }

        [MenuItem(k_Root + "External Tools Preferences...", false, 60)]
        private static void OpenPreferences()
        {
            ZedEditor.OpenExternalToolsPreferences();
        }
    }
}
