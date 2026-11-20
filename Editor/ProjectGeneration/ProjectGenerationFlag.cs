using System;

namespace SGame.Zed.Editor.ProjectGeneration
{
    /// <summary>
    /// Which assemblies the generated solution should contain. Values are stored in
    /// <see cref="UnityEditor.EditorPrefs"/> so they must stay stable.
    /// </summary>
    [Flags]
    public enum ProjectGenerationFlag
    {
        None = 0,
        BuiltIn = 1 << 0,
        Embedded = 1 << 1,
        Local = 1 << 2,
        Registry = 1 << 3,
        Git = 1 << 4,
        LocalTarBall = 1 << 5,
        Unknown = 1 << 6,

        /// <summary>Also generate <c>*.Player.csproj</c> projects for player assemblies.</summary>
        PlayerAssemblies = 1 << 7,

        /// <summary>Packages that ship sources on disk. Built-in modules have no sources, so they are excluded.</summary>
        Default = Local | Embedded | Registry | Git,
    }
}
