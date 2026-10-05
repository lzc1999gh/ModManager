using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ModManager.Models;

namespace ModManager.Services
{
    /// <summary>
    /// Mod 的磁盘操作：扫描 Mods 根目录、启用/禁用改名、重命名、删除与导入。
    /// 沉浸式 Persist 状态与界面交互由 ViewModel 负责编排。
    /// </summary>
    public class ModService
    {
        public static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz" };

        private readonly PreviewService _previews;

        public ModService(PreviewService previews) => _previews = previews;

        /// <summary>
        /// 扫描 Mod 根目录，把每个角色文件夹下的 Mod 填充进对应角色对象。
        /// applySavedSource 用于回填持久化的来源信息。
        /// </summary>
        public void ScanInto(string rootPath, IEnumerable<Character> characters, Action<Mod>? applySavedSource = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;
            var characterList = characters.ToList();

            foreach (var characterDir in Directory.GetDirectories(rootPath))
            {
                var folderName = Path.GetFileName(characterDir);
                var character = characterList.FirstOrDefault(c =>
                    string.Equals(c.Name, folderName, StringComparison.OrdinalIgnoreCase));
                if (character == null) continue;

                foreach (var modDir in Directory.GetDirectories(characterDir))
                {
                    var rawName = Path.GetFileName(modDir);
                    var mod = new Mod
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = ModNaming.ToDisplayName(rawName),
                        FilePath = modDir,
                        Enabled = !ModNaming.IsDisabled(rawName)
                    };
                    foreach (var previewPath in _previews.FindPreviews(mod)) mod.PreviewPaths.Add(previewPath);
                    applySavedSource?.Invoke(mod);
                    character.Mods.Add(mod);
                }

                foreach (var modFile in Directory.GetFiles(characterDir))
                {
                    var rawName = Path.GetFileName(modFile);
                    var mod = new Mod
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = ModNaming.ToDisplayName(rawName),
                        FilePath = modFile,
                        Size = new FileInfo(modFile).Length,
                        Enabled = !ModNaming.IsDisabled(rawName)
                    };
                    foreach (var previewPath in _previews.FindPreviews(mod)) mod.PreviewPaths.Add(previewPath);
                    applySavedSource?.Invoke(mod);
                    character.Mods.Add(mod);
                }
            }
        }

        /// <summary>启用/禁用 Mod：切换磁盘上的 DISABLED_ 前缀并同步对象状态。</summary>
        public void ToggleEnabled(Mod mod)
        {
            var path = mod.FilePath;
            if (string.IsNullOrWhiteSpace(path)) return;

            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent)) return;

            var raw = Path.GetFileName(path);
            var newName = ModNaming.IsDisabled(raw)
                ? ModNaming.ToDisplayName(raw)
                : ModNaming.WithDisabledPrefix(raw);
            var destination = FileSystemHelper.EnsureUniquePath(Path.Combine(parent, newName));

            FileSystemHelper.MoveEntry(path, destination);
            PreviewService.UpdatePathsAfterMove(mod, path, destination, movedDirectory: Directory.Exists(destination));

            mod.FilePath = destination;
            mod.Name = ModNaming.ToDisplayName(Path.GetFileName(destination));
            mod.Enabled = !ModNaming.IsDisabled(Path.GetFileName(destination));
        }

        /// <summary>重命名 Mod（保留禁用前缀），返回新的磁盘路径。</summary>
        public string Rename(Mod mod, string requestedName)
        {
            var oldPath = mod.FilePath;
            if (string.IsNullOrWhiteSpace(oldPath)) throw new InvalidOperationException("找不到 Mod 的原始路径。");

            var parent = Path.GetDirectoryName(oldPath);
            if (string.IsNullOrWhiteSpace(parent)) throw new InvalidOperationException("找不到 Mod 的父目录。");

            var rawName = Path.GetFileName(oldPath);
            var disabledPrefix = ModNaming.IsDisabled(rawName) ? ModNaming.DisabledPrefix : string.Empty;
            var newPath = Path.Combine(parent, disabledPrefix + requestedName);
            if (FileSystemHelper.EntryExists(newPath)) throw new IOException("同名 Mod 已存在。");

            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
                PreviewService.UpdatePathsAfterMove(mod, oldPath, newPath, movedDirectory: true);
            }
            else if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath);
                PreviewService.UpdatePathsAfterMove(mod, oldPath, newPath, movedDirectory: false);
            }
            else
            {
                throw new FileNotFoundException("原始 Mod 文件或目录不存在。", oldPath);
            }

            mod.FilePath = newPath;
            mod.Name = requestedName;
            return newPath;
        }

        /// <summary>删除 Mod 对应的文件或目录。</summary>
        public void DeleteFiles(Mod mod) => FileSystemHelper.DeleteEntry(mod.FilePath);

        /// <summary>
        /// 导入文件/目录/压缩包到指定角色目录，返回新增的 Mod 列表。
        /// confirmOverwrite 用于目标已存在时向用户确认；
        /// reportIssue 用于收集导入问题（如压缩包解压失败），由调用方统一展示。
        /// </summary>
        public List<Mod> Import(IEnumerable<string> paths, Character target, string modsRoot,
            Func<string, bool> confirmOverwrite, Action<string>? reportIssue = null)
        {
            var added = new List<Mod>();
            if (paths == null || target == null) return added;

            var targetDir = Path.Combine(modsRoot, target.Name);
            Directory.CreateDirectory(targetDir);

            foreach (var sourcePath in paths)
            {
                try
                {
                    if (Directory.Exists(sourcePath))
                    {
                        var destination = Path.Combine(targetDir, Path.GetFileName(sourcePath));
                        if (!confirmOverwrite(destination)) continue;
                        FileSystemHelper.CopyDirectory(sourcePath, destination);
                        added.Add(CreateModFromDirectory(destination));
                    }
                    else if (File.Exists(sourcePath))
                    {
                        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                        if (ArchiveExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                        {
                            var destination = Path.Combine(targetDir, Path.GetFileNameWithoutExtension(sourcePath));
                            if (!confirmOverwrite(destination)) continue;

                            if (ArchiveExtractor.TryExtract(sourcePath, destination, out var extractError))
                            {
                                added.Add(CreateModFromDirectory(destination));
                            }
                            else
                            {
                                // 解压失败：清理残留并跳过。压缩包不能被游戏当作 Mod 使用，
                                // 不再原样复制进 Mod 列表，把原因明确告诉用户。
                                FileSystemHelper.DeleteEntry(destination);
                                Debug.WriteLine($"[Import] Extract failed for '{sourcePath}': {extractError}");
                                reportIssue?.Invoke(
                                    $"“{Path.GetFileName(sourcePath)}”解压失败：{extractError ?? "未知原因"}。" +
                                    "已跳过；可手动解压后把得到的文件夹拖入列表导入。");
                            }
                        }
                        else
                        {
                            var destination = Path.Combine(targetDir, Path.GetFileName(sourcePath));
                            if (!confirmOverwrite(destination)) continue;
                            File.Copy(sourcePath, destination);
                            var info = new FileInfo(destination);
                            added.Add(new Mod
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = Path.GetFileName(destination),
                                FilePath = destination,
                                Size = info.Length,
                                Enabled = true
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Import] Failed to import '{sourcePath}': {ex}");
                }
            }
            return added;
        }

        private Mod CreateModFromDirectory(string directory)
        {
            var mod = new Mod
            {
                Id = Guid.NewGuid().ToString(),
                Name = Path.GetFileName(directory),
                FilePath = directory,
                Enabled = true
            };
            foreach (var previewPath in _previews.FindPreviewsInDirectory(directory)) mod.PreviewPaths.Add(previewPath);
            return mod;
        }
    }
}
