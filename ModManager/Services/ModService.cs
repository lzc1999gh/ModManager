using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ModManager.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

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
        /// confirmOverwrite 用于目标已存在时向用户确认。
        /// </summary>
        public List<Mod> Import(IEnumerable<string> paths, Character target, string modsRoot, Func<string, bool> confirmOverwrite)
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
                            var extracted = TryExtractArchive(sourcePath, destination, confirmOverwrite, out var fallbackMod);
                            if (fallbackMod != null) added.Add(fallbackMod);
                            else if (extracted) added.Add(CreateModFromDirectory(destination));
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

        private bool TryExtractArchive(string sourcePath, string destination, Func<string, bool> confirmOverwrite, out Mod? fallbackMod)
        {
            fallbackMod = null;
            if (!confirmOverwrite(destination)) return false;

            Directory.CreateDirectory(destination);
            try
            {
                using var archive = ArchiveFactory.OpenArchive(sourcePath);
                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                {
                    entry.WriteToDirectory(destination, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Import] Extract failed for '{sourcePath}', falling back to copy: {ex}");
                // 解压失败时回退为直接复制压缩包
                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                var fallback = Path.Combine(Path.GetDirectoryName(destination) ?? string.Empty, Path.GetFileName(sourcePath));
                if (!confirmOverwrite(fallback)) return false;
                File.Copy(sourcePath, fallback);
                fallbackMod = new Mod
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = Path.GetFileName(fallback),
                    FilePath = fallback,
                    Size = new FileInfo(fallback).Length,
                    Enabled = true
                };
                return false;
            }
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
