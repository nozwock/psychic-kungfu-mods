using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Common;

// https://github.com/nozwock/tale-of-immortal-tool/blob/e7435fb756201afb3a23c96676a9f90f2955f855/src/PathUtils.cs#L3
internal static class PathUtils
{
    public static StringComparison StringComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static string NormalizeSeparator(string path) => path.Replace("\\", "/");

    public static bool Equals(string path, string other) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(other), StringComparison);

    public static string TrimEndPathSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string[] Split(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string? GetParent(string path) =>
        Path.GetDirectoryName(TrimEndPathSeparator(path));

    public static string WithBaseName(string path, string basename) =>
        Path.Combine(GetParent(path) ?? "", basename);

    public static string GetBaseName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var trimmed = TrimEndPathSeparator(path);
        return string.IsNullOrEmpty(trimmed) ? path : Path.GetFileName(trimmed);
    }
}
