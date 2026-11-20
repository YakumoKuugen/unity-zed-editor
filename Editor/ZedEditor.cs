using System;
using System.Collections.Generic;
using SGame.Zed.Editor.ProjectGeneration;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace SGame.Zed.Editor
{
    /// <summary>
    /// Unity code editor integration for Zed (https://zed.dev).
    /// Implements <see cref="IExternalCodeEditor"/> so Zed shows up in
    /// Edit &gt; Preferences &gt; External Tools and in the "Open C# Project" menu.
    /// </summary>
    [InitializeOnLoad]
    public sealed class ZedEditor : IExternalCodeEditor
    {
        private const string k_DisplayName = "Zed Editor";

        private static ZedProjectGenerator s_Generator;
        private static bool k_EnsureSolutionScheduled;

        // Created on demand: the static constructor also runs in asset import workers.
        private static ZedProjectGenerator Generator => s_Generator ?? (s_Generator = new ZedProjectGenerator());

        static ZedEditor()
        {
            try
            {
                if (!IsMainUnityEditorProcess())
                    return;

                CodeEditor.Register(new ZedEditor());
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Zed] Failed to register the code editor: {exception}");
            }
        }

        [InitializeOnLoadMethod]
        private static void EnsureSolutionOnLoad()
        {
            if (!IsMainUnityEditorProcess())
                return;

            EditorApplication.delayCall += () =>
            {
                if (CodeEditor.CurrentEditor is ZedEditor)
                    ScheduleSolutionGeneration();
            };
        }

        public CodeEditor.Installation[] Installations
        {
            get
            {
                var discovered = ZedDiscovery.GetInstallations();
                var installations = new CodeEditor.Installation[discovered.Count];
                for (var i = 0; i < discovered.Count; i++)
                    installations[i] = discovered[i].ToCodeEditorInstallation();

                return installations;
            }
        }

        public void Initialize(string editorInstallationPath)
        {
            // Unity calls Initialize on every registered editor with the currently selected
            // executable, so ignore anything that is not Zed instead of logging noise.
            if (!ZedDiscovery.IsZedExecutable(editorInstallationPath))
                return;

            ZedDiscovery.Refresh();

            if (!ZedDiscovery.TryGetInstallation(editorInstallationPath, out var installation))
            {
                Debug.LogWarning($"[Zed] '{editorInstallationPath}' is not a valid Zed executable.");
                return;
            }

            Debug.Log($"[Zed] Using {installation.Name} at '{installation.ExecutablePath}'.");

            if (ZedPreferences.AutoWriteProjectSettings)
                ZedProjectSettings.Write(Generator.ProjectDirectory, overwrite: false, logResult: false);

            ScheduleSolutionGeneration();
        }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            if (ZedDiscovery.TryGetInstallation(editorPath, out var zed))
            {
                installation = zed.ToCodeEditorInstallation();
                return true;
            }

            installation = default;
            return false;
        }

        public void OnGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(BuildStatusLabel(), CreateStatusStyle());
            GUILayout.EndHorizontal();

            var installation = GetCurrentInstallation();
            if (installation == null)
            {
                EditorGUILayout.HelpBox(
                    "Zed could not be found. Install Zed from https://zed.dev, or use \"Browse...\" to select the zed executable.",
                    MessageType.Warning);

                if (GUILayout.Button("Open External Tools preferences"))
                    OpenExternalToolsPreferences();

                return;
            }

            var version = installation.GetVersionString();
            EditorGUILayout.LabelField("Zed", string.IsNullOrEmpty(version)
                ? installation.ExecutablePath
                : $"{installation.ExecutablePath} (v{version})");

            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            ProjectGenerationToggle(ProjectGenerationFlag.Embedded, "Embedded packages");
            ProjectGenerationToggle(ProjectGenerationFlag.Local, "Local packages");
            ProjectGenerationToggle(ProjectGenerationFlag.Registry, "Registry packages");
            ProjectGenerationToggle(ProjectGenerationFlag.Git, "Git packages");
            ProjectGenerationToggle(ProjectGenerationFlag.BuiltIn, "Built-in packages");
            ProjectGenerationToggle(ProjectGenerationFlag.LocalTarBall, "Local tarball");
            ProjectGenerationToggle(ProjectGenerationFlag.Unknown, "Packages from unknown sources");
            ProjectGenerationToggle(ProjectGenerationFlag.PlayerAssemblies, "Player projects",
                "Generate an additional '<assembly>.Player.csproj' for each player assembly.");
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate project files", GUILayout.Width(190)))
                    RegenerateProjectFiles();

                if (GUILayout.Button("Write .zed/settings.json", GUILayout.Width(190)))
                    WriteProjectSettings(overwrite: false);

                if (GUILayout.Button("Open project in Zed", GUILayout.Width(160)))
                    OpenProjectInZed();
            }

            ZedPreferences.AutoWriteProjectSettings = EditorGUILayout.Toggle(
                new GUIContent("Create .zed/settings.json", "Create a Unity friendly .zed/settings.json next to Assets when it does not exist yet."),
                ZedPreferences.AutoWriteProjectSettings);

            ZedPreferences.OpenWorkspaceFirst = EditorGUILayout.Toggle(
                new GUIContent("Open project folder first", "Open the Unity project folder before the script so Zed starts its language server with the right workspace."),
                ZedPreferences.OpenWorkspaceFirst);

            EditorGUILayout.HelpBox(
                "IntelliSense needs a C# extension in Zed that speaks the solution format (OmniSharp or Roslyn). " +
                $"The solution is generated at '{Generator.SolutionFile()}'.",
                MessageType.Info);
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            var affected = Concat(addedFiles, deletedFiles, movedFiles, movedFromFiles);
            Generator.SyncIfNeeded(affected, importedFiles);
        }

        public void SyncAll()
        {
            Generator.Sync();

            if (ZedPreferences.AutoWriteProjectSettings)
                ZedProjectSettings.Write(Generator.ProjectDirectory, overwrite: false, logResult: false);
        }

        public bool OpenProject(string path, int line, int column)
        {
            if (!ZedDiscovery.TryGetInstallation(CodeEditor.CurrentEditorInstallation, out var installation))
            {
                Debug.LogWarning("[Zed] Could not find the Zed executable. Select it in Edit > Preferences > External Tools.");
                return false;
            }

            if (!string.IsNullOrEmpty(path) && !Generator.IsSupportedFile(path))
                return false;

            if (!Generator.HasSolutionBeenGenerated())
                Generator.Sync();

            return installation.Open(path, line, column, Generator.SolutionFile());
        }

        internal static void RegenerateProjectFiles()
        {
            Generator.Sync(force: true);
            Debug.Log($"[Zed] Regenerated '{Generator.SolutionFile()}'.");
        }

        internal static void WriteProjectSettings(bool overwrite)
        {
            ZedProjectSettings.Write(Generator.ProjectDirectory, overwrite);
        }

        internal static void OpenProjectInZed()
        {
            if (!ZedDiscovery.TryGetInstallation(CodeEditor.CurrentEditorInstallation, out var installation))
            {
                Debug.LogWarning("[Zed] Could not find the Zed executable. Select it in Edit > Preferences > External Tools.");
                OpenExternalToolsPreferences();
                return;
            }

            if (!Generator.HasSolutionBeenGenerated())
                Generator.Sync();

            installation.Open(string.Empty, 0, 0, Generator.SolutionFile());
        }

        internal static void OpenExternalToolsPreferences()
        {
            SettingsService.OpenUserPreferences("Preferences/External Tools");
        }

        private static void ScheduleSolutionGeneration()
        {
            if (k_EnsureSolutionScheduled || Generator.HasSolutionBeenGenerated())
                return;

            k_EnsureSolutionScheduled = true;
            EditorApplication.delayCall += () =>
            {
                k_EnsureSolutionScheduled = false;
                if (!Generator.HasSolutionBeenGenerated())
                    Generator.Sync();
            };
        }

        private static ZedInstallation GetCurrentInstallation()
        {
            if (ZedDiscovery.TryGetInstallation(CodeEditor.CurrentEditorInstallation, out var installation))
                return installation;

            return ZedDiscovery.GetDefaultInstallation();
        }

        private static string BuildStatusLabel()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ZedEditor).Assembly);
            var displayName = string.IsNullOrEmpty(package?.displayName) ? k_DisplayName : package.displayName;
            var version = package?.version;

            return string.IsNullOrEmpty(version)
                ? $"<size=10><color=grey>{displayName} enabled</color></size>"
                : $"<size=10><color=grey>{displayName} v{version} enabled</color></size>";
        }

        private static GUIStyle CreateStatusStyle()
        {
            return new GUIStyle
            {
                richText = true,
                margin = new RectOffset(0, 4, 0, 0),
            };
        }

        private static void ProjectGenerationToggle(ProjectGenerationFlag flag, string label, string tooltip = "")
        {
            var previous = ZedPreferences.HasFlag(flag);
            var updated = EditorGUILayout.Toggle(new GUIContent(label, tooltip), previous);
            if (updated != previous)
                ZedPreferences.ToggleFlag(flag);
        }

        private static string[] Concat(params string[][] arrays)
        {
            var result = new List<string>();
            foreach (var array in arrays)
            {
                if (array == null)
                    continue;

                result.AddRange(array);
            }

            return result.ToArray();
        }

        private static bool IsMainUnityEditorProcess()
        {
            try
            {
                if (AssetDatabase.IsAssetImportWorkerProcess())
                    return false;

                if (UnityEditor.MPE.ProcessService.level == UnityEditor.MPE.ProcessLevel.Secondary)
                    return false;
            }
            catch (Exception)
            {
                // Older editors may not expose these APIs; assume we are the main process.
            }

            return true;
        }
    }
}
