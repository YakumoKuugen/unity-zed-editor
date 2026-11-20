using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;

namespace SGame.Zed.Editor.ProjectGeneration
{
    /// <summary>
    /// Resolves the package owning an asset and caches <see cref="PackageInfo"/> lookups.
    /// <see cref="PackageInfo.FindForAssetPath(string)"/> only understands virtual
    /// <c>Packages/Name/...</c> paths, while <c>CompilationPipeline</c> hands out physical paths
    /// (<c>Library/PackageCache/Name@hash/...</c>), so both shapes are supported here.
    /// </summary>
    internal sealed class PackageInfoCache
    {
        private const string k_VirtualPackagesPrefix = "Packages/";
        private const int k_VirtualPackagesPrefixLength = 9;
        private const string k_PackageCacheSegment = "/Library/PackageCache/";
        private const int k_PackageCacheSegmentLength = 22;

        private readonly string m_ProjectDirectory;
        private readonly Dictionary<string, PackageInfo> m_Cache = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PackageInfo> m_Resolved = new List<PackageInfo>();

        public PackageInfoCache(string projectDirectory)
        {
            m_ProjectDirectory = PathUtility.TrimTrailingSeparator(projectDirectory ?? string.Empty);
        }

        public void Clear()
        {
            m_Cache.Clear();
            m_Resolved.Clear();
        }

        /// <summary>Finds the package owning <paramref name="path"/> (virtual or physical).</summary>
        public PackageInfo FindForPath(string path)
        {
            var packageName = GetPackageName(path);
            if (string.IsNullOrEmpty(packageName))
                return null;

            return FindForAssetPath(k_VirtualPackagesPrefix + packageName);
        }

        /// <summary>Finds the package owning a virtual asset path such as <c>Packages/com.unity.ugui/Runtime/X.cs</c>.</summary>
        public PackageInfo FindForAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var unix = PathUtility.ToUnixPath(assetPath);
            if (!unix.StartsWith(k_VirtualPackagesPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var nextSeparator = unix.IndexOf('/', k_VirtualPackagesPrefixLength);
            var packageFolder = nextSeparator < 0 ? unix : unix.Substring(0, nextSeparator);

            if (m_Cache.TryGetValue(packageFolder, out var cached))
                return cached;

            PackageInfo info = null;
            try
            {
                info = PackageInfo.FindForAssetPath(packageFolder);
            }
            catch (Exception)
            {
                // Ignored: a missing package must not break project generation.
            }

            m_Cache[packageFolder] = info;
            if (info != null)
                m_Resolved.Add(info);

            return info;
        }

        /// <summary>Finds the package owning an absolute path, typically under <c>Library/PackageCache</c>.</summary>
        public PackageInfo FindForPhysicalPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return null;

            for (var i = 0; i < m_Resolved.Count; i++)
            {
                var info = m_Resolved[i];
                if (PathUtility.IsSubPathOf(info.resolvedPath, absolutePath))
                    return info;
            }

            return null;
        }

        /// <summary>
        /// Returns the project relative display path for a file coming from a package
        /// (<c>Packages/Name/...</c>), or <c>null</c> when the file is not inside a package.
        /// </summary>
        public string GetLinkPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var unix = PathUtility.ToUnixPath(path);
            if (unix.StartsWith(k_VirtualPackagesPrefix, StringComparison.OrdinalIgnoreCase))
                return unix;

            var cacheIndex = unix.IndexOf(k_PackageCacheSegment, StringComparison.OrdinalIgnoreCase);
            if (cacheIndex >= 0)
            {
                var relative = unix.Substring(cacheIndex + k_PackageCacheSegmentLength);
                var separator = relative.IndexOf('/');
                if (separator <= 0)
                    return null;

                var folder = relative.Substring(0, separator);
                var at = folder.IndexOf('@');
                var name = at > 0 ? folder.Substring(0, at) : folder;
                return k_VirtualPackagesPrefix + name + "/" + relative.Substring(separator + 1);
            }

            return null;
        }

        /// <summary>Maps a virtual <c>Packages/Name/...</c> path to the physical file on disk.</summary>
        public string ResolvePhysicalPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return null;

            var unix = PathUtility.ToUnixPath(virtualPath);
            if (!unix.StartsWith(k_VirtualPackagesPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var nextSeparator = unix.IndexOf('/', k_VirtualPackagesPrefixLength);
            var packageFolder = nextSeparator < 0 ? unix : unix.Substring(0, nextSeparator);
            var remainder = nextSeparator < 0 ? string.Empty : unix.Substring(nextSeparator + 1);

            var info = FindForAssetPath(packageFolder);
            if (info == null || string.IsNullOrEmpty(info.resolvedPath))
                return null;

            if (remainder.Length == 0)
                return PathUtility.NormalizeSeparators(info.resolvedPath);

            return Path.Combine(info.resolvedPath, PathUtility.NormalizeSeparators(remainder));
        }

        private string GetPackageName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var unix = PathUtility.ToUnixPath(path);

            if (unix.StartsWith(k_VirtualPackagesPrefix, StringComparison.OrdinalIgnoreCase))
                return GetSegmentAfter(unix, k_VirtualPackagesPrefixLength);

            var cacheIndex = unix.IndexOf(k_PackageCacheSegment, StringComparison.OrdinalIgnoreCase);
            if (cacheIndex >= 0)
            {
                var relative = unix.Substring(cacheIndex + k_PackageCacheSegmentLength);
                var folder = GetSegmentAfter(relative, 0);
                if (string.IsNullOrEmpty(folder))
                    return null;

                var at = folder.IndexOf('@');
                return at > 0 ? folder.Substring(0, at) : folder;
            }

            if (m_ProjectDirectory.Length > 0)
            {
                var packagesRoot = PathUtility.ToUnixPath(Path.Combine(m_ProjectDirectory, "Packages")) + "/";
                if (unix.StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase))
                    return GetSegmentAfter(unix, packagesRoot.Length);
            }

            return null;
        }

        private static string GetSegmentAfter(string path, int startIndex)
        {
            if (startIndex >= path.Length)
                return null;

            var separator = path.IndexOf('/', startIndex);
            return separator < 0 ? path.Substring(startIndex) : path.Substring(startIndex, separator - startIndex);
        }
    }
}
