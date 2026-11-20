using System;
using UnityEditor.Compilation;

namespace SGame.Zed.Editor.ProjectGeneration
{
    /// <summary>One generated <c>.csproj</c>: either an editor assembly or its player counterpart.</summary>
    internal readonly struct AssemblyEntry
    {
        public AssemblyEntry(Assembly assembly, bool isPlayer)
        {
            Assembly = assembly;
            IsPlayer = isPlayer;
        }

        public Assembly Assembly { get; }

        public bool IsPlayer { get; }

        /// <summary>Generated project name, also used as the <c>.csproj</c> file name.</summary>
        public string ProjectName => IsPlayer ? Assembly.name + ".Player" : Assembly.name;

        /// <summary>Output folder, relative to the project root, with a trailing separator.</summary>
        public string RelativeOutputPath => IsPlayer ? @"Temp\bin\Debug\Player\" : @"Temp\bin\Debug\";
    }
}
