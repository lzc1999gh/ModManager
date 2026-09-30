using System.IO;

namespace ModManager.Services
{
    /// <summary>文件/目录操作的小工具，收敛原先散落在 ViewModel 中的重复实现。</summary>
    public static class FileSystemHelper
    {
        /// <summary>路径是文件或目录时返回 true。</summary>
        public static bool EntryExists(string? path) =>
            !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

        /// <summary>删除文件或目录（不存在则忽略），目录递归删除。</summary>
        public static void DeleteEntry(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>移动文件或目录到目标路径。</summary>
        public static void MoveEntry(string source, string destination)
        {
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
        }

        /// <summary>path 是否位于 root 之下（含更深层级）。</summary>
        public static bool IsUnder(string? path, string? root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            return path!.StartsWith(root!.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把旧根路径下的路径换算到新根路径下。</summary>
        public static string RepathAfterMove(string path, string oldRoot, string newRoot) =>
            Path.Combine(newRoot, Path.GetRelativePath(oldRoot, path));

        /// <summary>目标已存在时追加唯一后缀，避免覆盖。</summary>
        public static string EnsureUniquePath(string desiredPath)
        {
            if (!EntryExists(desiredPath)) return desiredPath;
            var parent = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            var name = Path.GetFileName(desiredPath);
            return Path.Combine(parent, $"{name}_{System.Guid.NewGuid():N}");
        }

        /// <summary>递归复制目录内容。</summary>
        public static void CopyDirectory(string sourceDir, string destDir)
        {
            var directory = new DirectoryInfo(sourceDir);
            if (!directory.Exists) return;
            Directory.CreateDirectory(destDir);
            foreach (var file in directory.GetFiles()) file.CopyTo(Path.Combine(destDir, file.Name), true);
            foreach (var subDirectory in directory.GetDirectories())
                CopyDirectory(subDirectory.FullName, Path.Combine(destDir, subDirectory.Name));
        }
    }
}
