using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ModManager.Models;

namespace ModManager.Services
{
    /// <summary>Mod 内 INI 文件的查找、读取与快捷键解析。</summary>
    public class IniService
    {
        /// <summary>填充 Mod 的 INI 集合；没有找到 INI 时返回 false。</summary>
        public bool Populate(Mod mod, string? modFolder)
        {
            mod.IniFiles.Clear();
            mod.ToggleIniFiles.Clear();
            mod.SelectedIniFile = null;
            mod.IniFilePath = null;
            mod.IniContent = null;

            var iniPaths = FindIniFiles(mod, modFolder);
            if (iniPaths.Count == 0) return false;

            foreach (var iniPath in iniPaths)
            {
                var content = File.ReadAllText(iniPath);
                var relativePath = !string.IsNullOrWhiteSpace(modFolder) && Directory.Exists(modFolder)
                    ? Path.GetRelativePath(modFolder, iniPath)
                    : Path.GetFileName(iniPath);
                var ini = new IniFileInfo
                {
                    FilePath = iniPath,
                    RelativePath = relativePath,
                    Content = content
                };
                LoadShortcuts(ini);
                mod.IniFiles.Add(ini);
                if (ini.HasToggleKey) mod.ToggleIniFiles.Add(ini);
            }

            mod.SelectedIniFile = mod.ToggleIniFiles.FirstOrDefault() ?? mod.IniFiles.FirstOrDefault();
            return true;
        }

        /// <summary>查找该 Mod 关联的全部 INI 文件（忽略 Mod 内部被禁用的子目录）。</summary>
        public List<string> FindIniFiles(Mod mod, string? modFolder)
        {
            if (mod != null && File.Exists(mod.FilePath))
            {
                return string.Equals(Path.GetExtension(mod.FilePath), ".ini", StringComparison.OrdinalIgnoreCase)
                    ? new List<string> { mod.FilePath! }
                    : new List<string>();
            }

            if (string.IsNullOrWhiteSpace(modFolder) || !Directory.Exists(modFolder)) return new List<string>();
            try
            {
                return Directory.EnumerateFiles(modFolder, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetExtension(path), ".ini", StringComparison.OrdinalIgnoreCase))
                    // Mod 文件夹本身可能命名为 DISABLED_xxx，只忽略其内部被禁用的子目录
                    .Where(path => !Path.GetRelativePath(modFolder, path)
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(part => ModNaming.IsDisabled(part)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INI] Find failed for '{modFolder}': {ex}");
                return new List<string>();
            }
        }

        /// <summary>解析 [KeyXxx] 段落中的 key= 行，供界面展示快捷键。</summary>
        public static void LoadShortcuts(IniFileInfo ini)
        {
            if (string.IsNullOrWhiteSpace(ini?.Content)) return;

            string? section = null;
            foreach (var line in ini!.Content!.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var text = line.Trim();
                if (text.StartsWith("[") && text.EndsWith("]"))
                {
                    section = text[1..^1];
                    continue;
                }
                if (section == null || !section.StartsWith("Key", StringComparison.OrdinalIgnoreCase)) continue;

                var separator = text.IndexOf('=');
                if (separator <= 0) continue;

                var key = text[..separator].Trim();
                var value = text[(separator + 1)..].Trim();
                if (!key.Equals("key", StringComparison.OrdinalIgnoreCase)) continue;

                // 不同 Mod 会使用 KeyHair、KeyEye 等段落名，任何合法的 Key 段落都视为可切换项
                ini.HasToggleKey = true;
                ini.Shortcuts.Add(new IniShortcut
                {
                    Key = $"{section}: {value}",
                    IniFileName = ini.RelativePath,
                    Section = section,
                    ShortcutValue = value,
                    Value = 0,
                    OptionIndex = 0
                });
            }
        }
    }
}
