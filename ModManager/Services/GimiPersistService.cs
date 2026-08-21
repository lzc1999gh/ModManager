using ModManager.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Services
{
    /// <summary>
    /// GIMI Mod 的 Persist 状态管理服务：
    /// 负责在 Mod 启停时保存/恢复 d3dx_user.ini 中 global persist 变量的值，
    /// 持久化到 %LocalAppData%\ModManager\gimi-persist.json。
    /// </summary>
    public class GimiPersistService
    {
        private readonly string _userIniPath;

        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManager");

        private static readonly string PersistStateFile = Path.Combine(StateDirectory, "gimi-persist.json");

        /*
         * Persist 状态保存结构：
         * {
         *   "\\mods\\ys\\兹白\\兹白-aiui": {
         *       "zibai.ini\\up1": "0",
         *       "zibai.ini\\aup1": "0",
         *       "zibai.ini\\right1": "2",
         *       ...
         *   }
         * }
         *
         * 注意：这里保存的是 d3dx_user.ini 当前实际值，不是 ini 文件里的默认值。
         * 例如：global persist $RIGHT1 = 0，但 $\mods\ys\兹白\兹白-aiui\zibai.ini\right1 = 2，
         * 那么保存 zibai.ini\right1 = 2
         */
        private readonly Dictionary<string, Dictionary<string, string>> _persistStates =
            new(StringComparer.OrdinalIgnoreCase);

        // 匹配 global persist $UP1 = 0 / global persist $RIGHT1=0，只提取变量名
        private static readonly Regex PersistDeclarationRegex = new Regex(
            @"^\s*global\s+persist\s+\$([A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public GimiPersistService(string userIniPath)
        {
            _userIniPath = userIniPath;
            LoadPersistStates();
        }

        // =========================================================
        // Save Current Persist
        // =========================================================

        /// <summary>
        /// 读取当前 d3dx_user.ini 中属于该 Mod 的 Persist，
        /// 然后保存到 gimi-persist.json。
        /// 注意：这个方法只保存状态，不修改 Mod，不修改 d3dx_user.ini。
        /// </summary>
        public void SaveCurrentPersist(Mod mod)
        {
            if (mod == null)
            {
                Debug.WriteLine("[GIMI Persist] SaveCurrentPersist: Mod is null.");
                return;
            }

            var modPath = NormalizeModPath(mod.FilePath);

            if (string.IsNullOrEmpty(modPath))
            {
                Debug.WriteLine("[GIMI Persist] Cannot save: Mod path is empty.");
                return;
            }

            Debug.WriteLine("[GIMI Persist] =============================");
            Debug.WriteLine($"[GIMI Persist] Saving Persist for Mod: {mod.Name}");
            Debug.WriteLine($"[GIMI Persist] Mod.FilePath: {mod.FilePath}");
            Debug.WriteLine($"[GIMI Persist] Canonical Mod Path: {modPath}");

            var current = ReadCurrentPersist(mod);

            // 用当前结果完整覆盖旧结果，避免残留已不存在的旧 Persist
            _persistStates[modPath] = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);

            SavePersistStates();

            Debug.WriteLine($"[GIMI Persist] Saved {current.Count} values for {mod.Name}");
            Debug.WriteLine("[GIMI Persist] =============================");
        }

        // =========================================================
        // Read Current Persist
        // =========================================================

        /// <summary>
        /// 从 d3dx_user.ini 读取指定 Mod 当前的 Persist。
        /// 例如：Mod 为 H:\XXMI Launcher\GIMI\Mods\ys\兹白\兹白-aiui，
        /// d3dx_user.ini 中 $\mods\ys\兹白\兹白-aiui\zibai.ini\right1 = 2，
        /// ini 中声明 global persist $RIGHT1 = 0，返回 zibai.ini\right1 = 2。
        /// </summary>
        public Dictionary<string, string> ReadCurrentPersist(Mod mod)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (mod == null) return result;

            if (!File.Exists(_userIniPath))
            {
                Debug.WriteLine($"[GIMI Persist] user.ini not found: {_userIniPath}");
                return result;
            }

            var modPath = NormalizeModPath(mod.FilePath);

            if (string.IsNullOrEmpty(modPath)) return result;

            Debug.WriteLine("[GIMI Persist] =============================");
            Debug.WriteLine($"[GIMI Persist] Reading Mod: {mod.Name}");
            Debug.WriteLine($"[GIMI Persist] Mod.FilePath: {mod.FilePath}");
            Debug.WriteLine($"[GIMI Persist] Normalized Mod Path: {modPath}");

            // ① 找到该 Mod 中所有 global persist 声明
            var persistDefinitions = FindPersistDefinitions(mod);

            Debug.WriteLine($"[GIMI Persist] Persist declarations: {persistDefinitions.Count}");

            foreach (var definition in persistDefinitions)
            {
                Debug.WriteLine($"[GIMI Persist] DECLARED: {definition.Key}");
            }

            if (persistDefinitions.Count == 0)
            {
                Debug.WriteLine("[GIMI Persist] No global persist declarations found.");
                Debug.WriteLine("[GIMI Persist] =============================");
                return result;
            }

            // ② 读取 d3dx_user.ini，筛选属于当前 Mod 且是 global persist 的变量
            foreach (var line in File.ReadLines(_userIniPath))
            {
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                var equalIndex = trimmed.IndexOf('=');

                if (equalIndex <= 0) continue;

                var left = trimmed.Substring(0, equalIndex).Trim();
                var value = trimmed.Substring(equalIndex + 1).Trim();

                if (!left.StartsWith(@"$\mods\", StringComparison.OrdinalIgnoreCase)) continue;

                // $\mods\xxx → \mods\xxx
                var fullUserPath = NormalizeUserIniPath(left);

                if (string.IsNullOrEmpty(fullUserPath)) continue;

                // 必须属于当前 Mod
                if (!IsPathInsideMod(fullUserPath, modPath)) continue;

                var relativePath = GetRelativePath(modPath, fullUserPath);

                if (string.IsNullOrEmpty(relativePath)) continue;

                // 例如 zibai.ini\right1
                if (!TrySplitPersistKey(relativePath, out var iniPath, out var variableName)) continue;

                var definitionKey = CreatePersistKey(iniPath, variableName);

                // 只保存 global persist（例如 active 即使存在也不保存）
                if (!persistDefinitions.ContainsKey(definitionKey)) continue;

                result[definitionKey] = value;

                Debug.WriteLine($"[GIMI Persist] FOUND: {definitionKey} = {value}");
            }

            Debug.WriteLine($"[GIMI Persist] Total Persist values: {result.Count}");
            Debug.WriteLine("[GIMI Persist] =============================");

            return result;
        }

        // =========================================================
        // Restore Persist
        // =========================================================

        /// <summary>
        /// 将之前保存的 Persist 快照值写回该 Mod 对应的 ini 文件，
        /// 修改 ini 文件中 global persist 声明行的值，不再读写 d3dx_user.ini。
        /// 只修改当前 Mod 的 ini 文件，不修改：其他 Mod、普通变量、Mod.FilePath、Mod.Name、Mod.Enabled。
        /// </summary>
        public void RestorePersist(Mod mod)
        {
            if (mod == null) return;

            var modPath = NormalizeModPath(mod.FilePath);

            if (string.IsNullOrEmpty(modPath)) return;

            if (!_persistStates.TryGetValue(modPath, out var savedValues) || savedValues == null || savedValues.Count == 0)
            {
                Debug.WriteLine($"[GIMI Persist] No saved state for Mod: {mod.Name}");
                return;
            }

            Debug.WriteLine("[GIMI Persist] =============================");
            Debug.WriteLine($"[GIMI Persist] Restoring Mod: {mod.Name}");
            Debug.WriteLine($"[GIMI Persist] Mod Path: {modPath}");
            Debug.WriteLine($"[GIMI Persist] Saved values: {savedValues.Count}");

            // 按 ini 相对路径分组：ini相对路径 -> (变量名 -> 快照值)
            var grouped = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in savedValues)
            {
                // 键格式：zibai.ini\right1 或 sub\zibai.ini\up1
                if (!TrySplitPersistKey(pair.Key, out var iniRelativePath, out var variableName)) continue;

                if (!grouped.TryGetValue(iniRelativePath, out var variables))
                {
                    variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    grouped[iniRelativePath] = variables;
                }

                variables[variableName] = pair.Value;
            }

            var changedCount = 0;

            foreach (var group in grouped)
            {
                var iniFullPath = ResolveIniFullPath(mod.FilePath, group.Key);

                if (string.IsNullOrEmpty(iniFullPath) || !File.Exists(iniFullPath))
                {
                    Debug.WriteLine($"[GIMI Persist] INI not found: {iniFullPath ?? group.Key}");
                    continue;
                }

                changedCount += UpdatePersistValuesInIni(iniFullPath, group.Value);
            }

            Debug.WriteLine(changedCount == 0
                ? "[GIMI Persist] Nothing to restore."
                : $"[GIMI Persist] Restored {changedCount} values.");

            Debug.WriteLine("[GIMI Persist] =============================");
        }

        // =========================================================
        // Resolve INI Full Path
        // =========================================================

        /// <summary>
        /// 根据 Mod 路径（文件或目录）和 ini 相对路径解析 ini 文件的完整路径。
        /// Mod 本身是 ini 文件时，相对路径即文件名，直接返回 Mod 路径。
        /// </summary>
        private static string ResolveIniFullPath(string modFilePath, string iniRelativePath)
        {
            if (string.IsNullOrWhiteSpace(modFilePath) || string.IsNullOrWhiteSpace(iniRelativePath))
                return string.Empty;

            // Mod 本身就是 ini 文件：快照键中的 ini 相对路径即文件名，完整路径就是 Mod 路径
            if (File.Exists(modFilePath)) return modFilePath;

            // Mod 是目录：ini 相对路径相对于 Mod 目录
            if (!Directory.Exists(modFilePath)) return string.Empty;

            try
            {
                return Path.Combine(modFilePath, iniRelativePath);
            }
            catch
            {
                return string.Empty;
            }
        }

        // =========================================================
        // Update Persist Values In INI
        // =========================================================

        /// <summary>
        /// 将指定 ini 文件中已声明的 global persist 变量值更新为快照值。
        /// 只替换声明行中 = 之后的值部分，保留行首缩进与 "global persist $VAR =" 前缀，
        /// 返回实际修改的行数。
        /// </summary>
        private static int UpdatePersistValuesInIni(string iniFullPath, Dictionary<string, string> variables)
        {
            var changed = 0;

            try
            {
                var lines = File.ReadAllLines(iniFullPath);

                for (int i = 0; i < lines.Length; i++)
                {
                    var match = PersistDeclarationRegex.Match(lines[i]);

                    if (!match.Success) continue;

                    var variableName = match.Groups[1].Value;

                    // 该变量不在待恢复快照中则跳过，不修改
                    if (!variables.TryGetValue(variableName, out var newValue)) continue;

                    // 只替换 = 后的值，保留行首缩进与 "global persist $VAR =" 前缀
                    var equalIndex = lines[i].IndexOf('=');

                    if (equalIndex < 0) continue;

                    lines[i] = lines[i].Substring(0, equalIndex + 1) + " " + newValue;
                    changed++;

                    Debug.WriteLine($"[GIMI Persist] {Path.GetFileName(iniFullPath)}: {variableName} = {newValue}");
                }

                if (changed > 0)
                {
                    File.WriteAllLines(iniFullPath, lines);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Failed to update INI: {iniFullPath}");
                Debug.WriteLine($"[GIMI Persist] {ex}");
            }

            return changed;
        }

        // =========================================================
        // Move Persist State
        // =========================================================

        /// <summary>
        /// Mod 重命名后迁移 Persist 状态。
        /// 这个方法只修改 gimi-persist.json，不修改 d3dx_user.ini。
        /// </summary>
        public void MovePersistState(string oldModPath, string newModPath)
        {
            var oldPath = NormalizeModPath(oldModPath);
            var newPath = NormalizeModPath(newModPath);

            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) return;

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) return;

            if (!_persistStates.TryGetValue(oldPath, out var state)) return;

            _persistStates.Remove(oldPath);
            _persistStates[newPath] = new Dictionary<string, string>(state, StringComparer.OrdinalIgnoreCase);

            SavePersistStates();

            Debug.WriteLine("[GIMI Persist] State moved.");
            Debug.WriteLine($"[GIMI Persist] OLD: {oldPath}");
            Debug.WriteLine($"[GIMI Persist] NEW: {newPath}");
        }

        // =========================================================
        // Find Persist Definitions
        // =========================================================

        private Dictionary<string, string> FindPersistDefinitions(Mod mod)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var iniPath in FindModIniFiles(mod))
            {
                if (!File.Exists(iniPath)) continue;

                var relativeIniPath = GetIniRelativePath(mod, iniPath);

                if (string.IsNullOrEmpty(relativeIniPath)) continue;

                try
                {
                    foreach (var line in File.ReadLines(iniPath))
                    {
                        var match = PersistDeclarationRegex.Match(line);

                        if (!match.Success) continue;

                        var variableName = match.Groups[1].Value;

                        if (string.IsNullOrWhiteSpace(variableName)) continue;

                        var key = CreatePersistKey(relativeIniPath, variableName);

                        result[key] = variableName;

                        Debug.WriteLine($"[GIMI Persist] Definition: {key}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GIMI Persist] Failed reading INI: {iniPath}");
                    Debug.WriteLine($"[GIMI Persist] {ex}");
                }
            }

            return result;
        }

        // =========================================================
        // Find Mod INI Files
        // =========================================================

        private IEnumerable<string> FindModIniFiles(Mod mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath))
                return Enumerable.Empty<string>();

            var path = mod.FilePath;

            // Mod 本身是 ini 文件
            if (File.Exists(path))
            {
                if (string.Equals(Path.GetExtension(path), ".ini", StringComparison.OrdinalIgnoreCase))
                    return new[] { path };

                return Enumerable.Empty<string>();
            }

            // Mod 是目录
            if (!Directory.Exists(path)) return Enumerable.Empty<string>();

            try
            {
                return Directory.EnumerateFiles(path, "*.ini", SearchOption.AllDirectories)
                    .Where(file => !Path.GetFileName(file).StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Find INI failed: {ex}");
                return Enumerable.Empty<string>();
            }
        }

        // =========================================================
        // Get INI Relative Path
        // =========================================================

        private string GetIniRelativePath(Mod mod, string iniPath)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath) || string.IsNullOrWhiteSpace(iniPath))
                return string.Empty;

            var modPath = mod.FilePath;

            // Mod 本身是 ini 文件：返回文件名，例如 zibai.ini
            if (File.Exists(modPath)) return Path.GetFileName(modPath);

            if (!Directory.Exists(modPath)) return string.Empty;

            try
            {
                var relative = Path.GetRelativePath(modPath, iniPath);

                return relative.Replace('/', '\\').TrimStart('\\');
            }
            catch
            {
                return string.Empty;
            }
        }

        // =========================================================
        // Create Persist Key
        // =========================================================

        private static string CreatePersistKey(string iniPath, string variableName)
        {
            if (string.IsNullOrWhiteSpace(iniPath) || string.IsNullOrWhiteSpace(variableName))
                return string.Empty;

            return iniPath.Replace('/', '\\').TrimStart('\\').TrimEnd('\\') + "\\" + variableName.Trim();
        }

        // =========================================================
        // Split Persist Key
        // =========================================================

        private static bool TrySplitPersistKey(string relativePath, out string iniPath, out string variableName)
        {
            iniPath = null;
            variableName = null;

            if (string.IsNullOrWhiteSpace(relativePath)) return false;

            relativePath = relativePath.Replace('/', '\\').Trim();

            var lastSlash = relativePath.LastIndexOf('\\');

            if (lastSlash <= 0 || lastSlash >= relativePath.Length - 1) return false;

            iniPath = relativePath.Substring(0, lastSlash).Trim();
            variableName = relativePath.Substring(lastSlash + 1).Trim();

            return !string.IsNullOrEmpty(iniPath) && !string.IsNullOrEmpty(variableName);
        }

        // =========================================================
        // Is Path Inside Mod
        // =========================================================

        private static bool IsPathInsideMod(string fullPath, string modPath)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(modPath)) return false;

            if (string.Equals(fullPath, modPath, StringComparison.OrdinalIgnoreCase)) return true;

            var prefix = modPath.TrimEnd('\\') + "\\";

            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================
        // Normalize Mod Path
        // =========================================================

        /// <summary>
        /// 将实际 Windows Mod 路径转换成 GIMI canonical path。
        /// 例如：H:\XXMI Launcher\GIMI\Mods\ys\兹白\兹白-aiui → \mods\ys\兹白\兹白-aiui。
        /// DISABLED_ 只删除路径组件前缀，不修改原来的 Mod.FilePath。
        /// </summary>
        private static string NormalizeModPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            path = path.Replace('/', '\\').Trim();

            // 找到 \mods\ 起始位置
            var index = path.IndexOf("\\mods\\", StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                path = path.Substring(index);
            }
            else if (path.StartsWith("mods\\", StringComparison.OrdinalIgnoreCase))
            {
                path = "\\" + path;
            }
            else if (path.Equals("mods", StringComparison.OrdinalIgnoreCase))
            {
                path = "\\mods";
            }
            else
            {
                // 如果路径里完全没有 Mods，不猜测它是什么路径
                return string.Empty;
            }

            // 只删除路径组件开头的 DISABLED_，例如 \mods\ys\兹白\DISABLED_兹白-aiui → \mods\ys\兹白\兹白-aiui
            var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = parts[i].Substring("DISABLED_".Length);
                }
            }

            return "\\" + string.Join("\\", parts).TrimEnd('\\');
        }

        // =========================================================
        // Normalize User INI Path
        // =========================================================

        /// <summary>
        /// $\mods\xxx → \mods\xxx
        /// </summary>
        private static string NormalizeUserIniPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            path = path.Replace('/', '\\').Trim();

            // 去掉最前面的 $
            if (path.StartsWith("$"))
            {
                path = path.Substring(1);
            }

            return path.TrimEnd('\\');
        }

        // =========================================================
        // Get Relative Path
        // =========================================================

        private static string GetRelativePath(string modPath, string fullPath)
        {
            if (string.IsNullOrEmpty(modPath) || string.IsNullOrEmpty(fullPath)) return string.Empty;

            if (string.Equals(modPath, fullPath, StringComparison.OrdinalIgnoreCase)) return string.Empty;

            var prefix = modPath.TrimEnd('\\') + "\\";

            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return string.Empty;

            return fullPath.Substring(prefix.Length).TrimStart('\\');
        }

        // =========================================================
        // Load Persist States
        // =========================================================

        private void LoadPersistStates()
        {
            try
            {
                if (!File.Exists(PersistStateFile))
                {
                    Debug.WriteLine($"[GIMI Persist] State file not found: {PersistStateFile}");
                    return;
                }

                var json = File.ReadAllText(PersistStateFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);

                if (data == null) return;

                _persistStates.Clear();

                foreach (var pair in data)
                {
                    var normalizedModPath = NormalizeModPath(pair.Key);

                    if (string.IsNullOrEmpty(normalizedModPath)) continue;

                    _persistStates[normalizedModPath] = new Dictionary<string, string>(
                        pair.Value ?? new Dictionary<string, string>(),
                        StringComparer.OrdinalIgnoreCase);
                }

                Debug.WriteLine($"[GIMI Persist] Loaded {_persistStates.Count} Mod states.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Load state failed: {ex}");
            }
        }

        // =========================================================
        // Save Persist States
        // =========================================================

        private void SavePersistStates()
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_persistStates, options);

                File.WriteAllText(PersistStateFile, json);

                Debug.WriteLine($"[GIMI Persist] State saved: {PersistStateFile}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Save state failed: {ex}");
            }
        }
    }
}
