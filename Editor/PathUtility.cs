using System;
using System.IO;

namespace SGame.Zed.Editor
{
    /// <summary>
    /// Small path helpers shared by the Zed integration. Everything here is deliberately
    /// allocation friendly because discovery and generation run on the editor main thread.
    /// </summary>
    internal static class PathUtility
    {
        private const char k_WindowsSeparator = '\\';
        private const char k_UnixSeparator = '/';

        /// <summary>Replaces every directory separator with the one used by the current OS.</summary>
        public static string NormalizeSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (Path.DirectorySeparatorChar == k_WindowsSeparator)
                return path.Replace(k_UnixSeparator, k_WindowsSeparator);

            return path.Replace(k_WindowsSeparator, k_UnixSeparator);
        }

        /// <summary>Replaces every directory separator with a forward slash.</summary>
        public static string ToUnixPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            return path.Replace(k_WindowsSeparator, k_UnixSeparator);
        }

        public static string GetAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                return NormalizeSeparators(Path.GetFullPath(path));
            }
            catch (Exception)
            {
                return NormalizeSeparators(path);
            }
        }

        /// <summary>Returns <paramref name="path"/> without a trailing directory separator.</summary>
        public static string TrimTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            var trimmed = path.TrimEnd(k_WindowsSeparator, k_UnixSeparator);

            // Never trim "C:" down to "C" on Windows.
            if (trimmed.Length == 2 && trimmed[1] == ':')
                return path;

            return trimmed;
        }

        public static bool IsSubPathOf(string parent, string candidate)
        {
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(candidate))
                return false;

            parent = TrimTrailingSeparator(NormalizeSeparators(parent));
            candidate = TrimTrailingSeparator(NormalizeSeparators(candidate));

            if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
                return true;

            return candidate.Length > parent.Length
                   && candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                   && (candidate[parent.Length] == k_WindowsSeparator || candidate[parent.Length] == k_UnixSeparator);
        }

        /// <summary>Returns a relative path when the file lives inside the project, otherwise the original path.</summary>
        public static string SkipPathPrefix(string path, string prefix)
        {
            path = NormalizeSeparators(path);
            prefix = TrimTrailingSeparator(NormalizeSeparators(prefix));

            if (!IsSubPathOf(prefix, path))
                return path;

            if (path.Length == prefix.Length)
                return string.Empty;

            return path.Substring(prefix.Length + 1);
        }

        /// <summary>
        /// Quotes an argument for <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/>.
        /// A trailing backslash is doubled so it cannot escape the closing quote.
        /// </summary>
        public static string Quote(string argument)
        {
            if (argument == null)
                return "\"\"";

            var builder = new System.Text.StringBuilder(argument.Length + 2);
            builder.Append('"');
            builder.Append(argument);
            if (argument.Length > 0 && argument[argument.Length - 1] == k_WindowsSeparator)
                builder.Append(k_WindowsSeparator);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
