using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModManager.Models;

namespace ModManager.Services
{
    /// <summary>
    /// Mod 预览图的定位与路径维护。
    /// 目录型 Mod 的预览图位于其文件夹内（preview_N），文件型 Mod 位于同目录（&lt;名称&gt;.preview_N）。
    /// </summary>
    public class PreviewService
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>Mod 所在文件夹：目录型返回自身，文件型返回父目录。</summary>
        public string? GetModFolder(Mod? mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.FilePath)) return null;
            if (Directory.Exists(mod.FilePath)) return mod.FilePath;
            return Path.GetDirectoryName(mod.FilePath);
        }

        /// <summary>找出该 Mod 已有的全部预览图。</summary>
        public List<string> FindPreviews(Mod mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath)) return new List<string>();
            if (Directory.Exists(mod.FilePath)) return FindPreviewsInDirectory(mod.FilePath);
            if (!File.Exists(mod.FilePath)) return new List<string>();

            var parent = Path.GetDirectoryName(mod.FilePath);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return new List<string>();
            var prefix = Path.GetFileNameWithoutExtension(mod.FilePath) + ".preview_";
            return EnumeratePreviews(parent, prefix);
        }

        /// <summary>目录中的 preview_* 图片。</summary>
        public List<string> FindPreviewsInDirectory(string directory) => EnumeratePreviews(directory, "preview_");

        private static List<string> EnumeratePreviews(string directory, string prefix)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return new List<string>();
            return Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>下一个可用的预览图路径（自动递增编号）。</summary>
        public string? GetNextPreviewPath(Mod mod, string extension = ".png")
        {
            var folder = GetModFolder(mod);
            if (string.IsNullOrWhiteSpace(folder)) return null;

            extension = string.IsNullOrWhiteSpace(extension) ? ".png" : extension.Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension;
            extension = extension.ToLowerInvariant();

            var prefix = Directory.Exists(mod.FilePath)
                ? "preview_"
                : Path.GetFileNameWithoutExtension(mod.FilePath) + ".preview_";

            var max = 0;
            if (Directory.Exists(folder))
            {
                foreach (var filePath in Directory.EnumerateFiles(folder, "*"))
                {
                    var name = Path.GetFileNameWithoutExtension(filePath);
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(name.Substring(prefix.Length), out var number) && number > max) max = number;
                }
            }
            return Path.Combine(folder, $"{prefix}{max + 1}{extension}");
        }

        /// <summary>Mod 文件/目录整体移动后，同步修正预览图路径。</summary>
        public static void UpdatePathsAfterMove(Mod mod, string oldPath, string newPath, bool movedDirectory)
        {
            for (var index = 0; index < mod.PreviewPaths.Count; index++)
            {
                var previewPath = mod.PreviewPaths[index];
                if (!movedDirectory && string.Equals(previewPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    mod.PreviewPaths[index] = newPath;
                    continue;
                }
                if (!movedDirectory || !FileSystemHelper.IsUnder(previewPath, oldPath)) continue;
                mod.PreviewPaths[index] = FileSystemHelper.RepathAfterMove(previewPath, oldPath, newPath);
            }
        }
    }
}
