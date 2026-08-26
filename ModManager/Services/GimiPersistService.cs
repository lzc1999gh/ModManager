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
    /// 管理 Mod 的 global persist 历史状态。
    ///
    /// d3dx_user.ini 只保存当前生效 Mod 的运行时值；
    /// 本服务在切换前读取当前值，并把每个 Mod 的历史快照保存到
    /// %LocalAppData%\ModManager\mod_persist_snapshots.json。
    ///
    /// 快捷键 [Key...] key= 不属于本服务的处理范围。
    /// </summary>
    public class GimiPersistService
    {
        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManager");

        private static readonly string PersistStateFile = Path.Combine(StateDirectory, "mod_persist_snapshots.json");
        private static readonly string LegacyPersistStateFile = Path.Combine(StateDirectory, "gimi-persist.json");
        private const string LegacyGameKey = "__legacy__";

        // 游戏 ID -> 规范化 Mod 路径 -> ini相对路径\变量名 -> 值
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _persistStates =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex PersistDeclarationRegex = new Regex(
            @"^\s*global\s+persist\s+\$([A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class PersistReadResult
        {
            public bool FileAvailable { get; init; }
            public Dictionary<string, string> Values { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public GimiPersistService()
        {
            MigrateLegacyPersistStateFile();
            LoadPersistStates();
        }

        /// <summary>
        /// 在禁用当前 Mod 前读取 d3dx_user.ini 中当前生效的值。
        /// 读取失败或没有匹配值时保留已有历史快照，避免一次路径/写盘异常清空状态。
        /// </summary>
        public void SaveCurrentPersist(Game game, Mod mod)
        {
            if (game == null || mod == null) return;

            var modPath = NormalizeModPath(game, mod.FilePath);
            if (string.IsNullOrEmpty(modPath))
            {
                Debug.WriteLine("[GIMI Persist] Cannot save: logical Mod path is empty.");
                return;
            }

            var read = ReadCurrentPersist(game, mod, modPath);
            if (!read.FileAvailable)
            {
                Debug.WriteLine("[GIMI Persist] Current d3dx_user.ini is unavailable; old snapshot is kept.");
                return;
            }

            if (read.Values.Count == 0)
            {
                Debug.WriteLine($"[GIMI Persist] No current values found for {mod.Name}; old snapshot is kept.");
                return;
            }

            var gameStates = GetOrCreateGameStates(GetGameKey(game));
            if (!gameStates.TryGetValue(modPath, out var modStates))
            {
                modStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                gameStates[modPath] = modStates;
            }

            foreach (var pair in read.Values)
                modStates[pair.Key] = pair.Value;

            // 旧版本状态没有游戏维度。成功从当前游戏读取到值后，清理同一 Mod 的旧兼容记录。
            if (_persistStates.TryGetValue(LegacyGameKey, out var legacyStates))
            {
                legacyStates.Remove(modPath);
                if (legacyStates.Count == 0) _persistStates.Remove(LegacyGameKey);
            }

            SavePersistStates();
            Debug.WriteLine($"[GIMI Persist] Saved {read.Values.Count} values for {game.Name}/{mod.Name}.");
        }

        /// <summary>
        /// 启用 Mod 前，将该 Mod 的历史快照写回 Mod INI 中的 global persist 声明。
        /// 不直接写 d3dx_user.ini，等待 3DMigoto/XXMI 重新加载后生成当前 Mod 的用户状态。
        /// </summary>
        public void RestorePersist(Game game, Mod mod)
        {
            if (game == null || mod == null) return;

            var modPath = NormalizeModPath(game, mod.FilePath);
            if (string.IsNullOrEmpty(modPath)) return;

            var savedValues = GetSavedValues(game, modPath);
            if (savedValues == null || savedValues.Count == 0)
            {
                Debug.WriteLine($"[GIMI Persist] No saved state for {game.Name}/{mod.Name}.");
                return;
            }

            var grouped = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in savedValues)
            {
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
                ? $"[GIMI Persist] Nothing to restore for {game.Name}/{mod.Name}."
                : $"[GIMI Persist] Restored {changedCount} values for {game.Name}/{mod.Name}.");
        }

        /// <summary>
        /// 删除指定 Mod 的历史 persist 快照。磁盘 Mod 删除成功后调用。
        /// </summary>
        public void RemovePersistState(Game game, Mod mod)
        {
            if (game == null || mod == null) return;
            RemovePersistState(game, mod.FilePath);
        }

        public void RemovePersistState(Game game, string modFilePath)
        {
            if (game == null) return;

            var modPath = NormalizeModPath(game, modFilePath);
            if (string.IsNullOrEmpty(modPath)) return;

            var changed = false;
            var gameKey = GetGameKey(game);
            if (_persistStates.TryGetValue(gameKey, out var gameStates))
            {
                changed = gameStates.Remove(modPath);
                if (gameStates.Count == 0) _persistStates.Remove(gameKey);
            }

            if (_persistStates.TryGetValue(LegacyGameKey, out var legacyStates))
            {
                changed |= legacyStates.Remove(modPath);
                if (legacyStates.Count == 0) _persistStates.Remove(LegacyGameKey);
            }

            if (changed) SavePersistStates();
            Debug.WriteLine($"[GIMI Persist] Removed snapshot for {game.Name}/{modPath}.");
        }

        /// <summary>
        /// Mod 或角色目录改名后迁移历史快照。不会修改 d3dx_user.ini。
        /// </summary>
        public void MovePersistState(Game game, string oldModPath, string newModPath)
        {
            if (game == null) return;

            var oldPath = NormalizeModPath(game, oldModPath);
            var newPath = NormalizeModPath(game, newModPath);
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)
                || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) return;

            var gameKey = GetGameKey(game);
            Dictionary<string, string> oldState;
            if (!_persistStates.TryGetValue(gameKey, out var gameStates)
                || !gameStates.TryGetValue(oldPath, out oldState))
            {
                // 兼容旧版本的无游戏维度状态。
                if (!_persistStates.TryGetValue(LegacyGameKey, out var legacyStates)
                    || !legacyStates.TryGetValue(oldPath, out oldState)) return;

                gameStates = GetOrCreateGameStates(gameKey);
            }

            if (!gameStates.TryGetValue(newPath, out var newState))
            {
                newState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                gameStates[newPath] = newState;
            }

            foreach (var pair in oldState) newState[pair.Key] = pair.Value;
            gameStates.Remove(oldPath);

            if (_persistStates.TryGetValue(LegacyGameKey, out var oldLegacyStates))
            {
                oldLegacyStates.Remove(oldPath);
                if (oldLegacyStates.Count == 0) _persistStates.Remove(LegacyGameKey);
            }

            SavePersistStates();
            Debug.WriteLine($"[GIMI Persist] State moved: {oldPath} -> {newPath}.");
        }

        public void MoveGamePersistState(string oldGameKey, string newGameKey)
        {
            oldGameKey = NormalizeGameKey(oldGameKey);
            newGameKey = NormalizeGameKey(newGameKey);
            if (string.Equals(oldGameKey, newGameKey, StringComparison.OrdinalIgnoreCase)) return;
            if (!_persistStates.TryGetValue(oldGameKey, out var oldStates)) return;

            var newStates = GetOrCreateGameStates(newGameKey);
            foreach (var mod in oldStates)
            {
                if (!newStates.TryGetValue(mod.Key, out var values))
                {
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    newStates[mod.Key] = values;
                }

                foreach (var pair in mod.Value) values[pair.Key] = pair.Value;
            }

            _persistStates.Remove(oldGameKey);
            SavePersistStates();
        }

        public void RemoveGamePersistState(Game game)
        {
            if (game == null) return;
            if (_persistStates.Remove(GetGameKey(game))) SavePersistStates();
        }

        // =========================================================
        // Reading current d3dx_user.ini
        // =========================================================

        private PersistReadResult ReadCurrentPersist(Game game, Mod mod, string modPath)
        {
            var result = new PersistReadResult();
            var userIniPath = ResolveUserIniPath(game);
            if (string.IsNullOrWhiteSpace(userIniPath) || !File.Exists(userIniPath))
            {
                Debug.WriteLine($"[GIMI Persist] user.ini not found: {userIniPath ?? "(empty path)"}");
                return result;
            }

            var persistDefinitions = FindPersistDefinitions(game, mod);
            if (persistDefinitions.Count == 0)
                return new PersistReadResult { FileAvailable = true };

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in File.ReadLines(userIniPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                    var equalIndex = trimmed.IndexOf('=');
                    if (equalIndex <= 0) continue;

                    var left = trimmed.Substring(0, equalIndex).Trim();
                    var value = trimmed.Substring(equalIndex + 1).Trim();
                    if (!left.StartsWith(@"$\mods\", StringComparison.OrdinalIgnoreCase)) continue;

                    var fullUserPath = NormalizeUserIniPath(left);
                    if (!IsPathInsideMod(fullUserPath, modPath)) continue;

                    var relativePath = GetRelativePath(modPath, fullUserPath);
                    if (!TrySplitPersistKey(relativePath, out var iniPath, out var variableName)) continue;

                    var definitionKey = CreatePersistKey(iniPath, variableName);
                    if (persistDefinitions.ContainsKey(definitionKey)) values[definitionKey] = value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Failed reading user.ini: {ex}");
                return result;
            }

            return new PersistReadResult { FileAvailable = true, Values = values };
        }

        private Dictionary<string, string> FindPersistDefinitions(Game game, Mod mod)
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
                        result[CreatePersistKey(relativeIniPath, variableName)] = variableName;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GIMI Persist] Failed reading INI '{iniPath}': {ex}");
                }
            }

            return result;
        }

        private IEnumerable<string> FindModIniFiles(Mod mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath)) return Enumerable.Empty<string>();

            var path = mod.FilePath;
            if (File.Exists(path))
            {
                return string.Equals(Path.GetExtension(path), ".ini", StringComparison.OrdinalIgnoreCase)
                    ? new[] { path }
                    : Enumerable.Empty<string>();
            }

            if (!Directory.Exists(path)) return Enumerable.Empty<string>();

            try
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(file => string.Equals(Path.GetExtension(file), ".ini", StringComparison.OrdinalIgnoreCase))
                    .Where(file => !Path.GetRelativePath(path, file)
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(part => part.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Find INI failed: {ex}");
                return Enumerable.Empty<string>();
            }
        }

        private static string GetIniRelativePath(Mod mod, string iniPath)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath) || string.IsNullOrWhiteSpace(iniPath)) return string.Empty;
            if (File.Exists(mod.FilePath)) return Path.GetFileName(mod.FilePath);
            if (!Directory.Exists(mod.FilePath)) return string.Empty;

            try
            {
                return Path.GetRelativePath(mod.FilePath, iniPath).Replace('/', '\\').TrimStart('\\');
            }
            catch
            {
                return string.Empty;
            }
        }

        // =========================================================
        // Logical path and state helpers
        // =========================================================

        private static string ResolveUserIniPath(Game game)
        {
            if (game == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(game.D3dxUserIniPath)) return game.D3dxUserIniPath.Trim();

            if (!string.IsNullOrWhiteSpace(game.ModsRootPath))
            {
                try
                {
                    var root = Path.GetFullPath(game.ModsRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var parent = Directory.GetParent(root)?.FullName;
                    if (!string.IsNullOrWhiteSpace(parent)) return Path.Combine(parent, "d3dx_user.ini");
                }
                catch { }
            }

            return string.Empty;
        }

        private static string GetGameKey(Game game) => NormalizeGameKey(game?.Id ?? game?.Name);

        private static string NormalizeGameKey(string key) =>
            string.IsNullOrWhiteSpace(key) ? "default" : key.Trim();

        private Dictionary<string, Dictionary<string, string>> GetOrCreateGameStates(string gameKey)
        {
            gameKey = NormalizeGameKey(gameKey);
            if (!_persistStates.TryGetValue(gameKey, out var states))
            {
                states = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                _persistStates[gameKey] = states;
            }

            return states;
        }

        private Dictionary<string, string> GetSavedValues(Game game, string modPath)
        {
            if (_persistStates.TryGetValue(GetGameKey(game), out var gameStates)
                && gameStates.TryGetValue(modPath, out var values))
            {
                return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            }

            if (_persistStates.TryGetValue(LegacyGameKey, out var legacyStates)
                && legacyStates.TryGetValue(modPath, out var legacyValues))
            {
                return new Dictionary<string, string>(legacyValues, StringComparer.OrdinalIgnoreCase);
            }

            return null;
        }

        private static string NormalizeModPath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            path = path.Replace('/', '\\').Trim();

            if (!string.IsNullOrWhiteSpace(game?.ModsRootPath))
            {
                try
                {
                    var root = Path.GetFullPath(game.ModsRootPath).TrimEnd('\\');
                    var fullPath = Path.GetFullPath(path).TrimEnd('\\');
                    var prefix = root + "\\";
                    if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var relative = fullPath.Substring(prefix.Length);
                        var parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            parts[^1] = RemoveDisabledPrefix(parts[^1]);
                            return @"\mods\" + string.Join("\\", parts);
                        }
                    }
                }
                catch { }
            }

            // 兼容旧状态：从实际路径中定位 Mods，并只处理最后一个组件的 DISABLED_。
            var markerIndex = path.IndexOf(@"\mods\", StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                var relative = path.Substring(markerIndex + @"\mods\".Length);
                var parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    parts[^1] = RemoveDisabledPrefix(parts[^1]);
                    return @"\mods\" + string.Join("\\", parts);
                }
            }

            if (path.StartsWith("mods\\", StringComparison.OrdinalIgnoreCase))
            {
                var relative = path.Substring("mods\\".Length);
                var parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    parts[^1] = RemoveDisabledPrefix(parts[^1]);
                    return @"\mods\" + string.Join("\\", parts);
                }
            }

            return string.Empty;
        }

        private static string RemoveDisabledPrefix(string name) =>
            name.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase)
                ? name.Substring("DISABLED_".Length)
                : name;

        private static string NormalizeUserIniPath(string path)
        {
            path = path.Replace('/', '\\').Trim();
            if (path.StartsWith("$")) path = path.Substring(1);
            return path.TrimEnd('\\');
        }

        private static bool IsPathInsideMod(string fullPath, string modPath)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(modPath)) return false;
            var prefix = modPath.TrimEnd('\\') + "\\";
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelativePath(string modPath, string fullPath)
        {
            var prefix = modPath.TrimEnd('\\') + "\\";
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(prefix.Length).TrimStart('\\')
                : string.Empty;
        }

        private static string CreatePersistKey(string iniPath, string variableName) =>
            iniPath.Replace('/', '\\').TrimStart('\\').TrimEnd('\\') + "\\" + variableName.Trim();

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

        private static string ResolveIniFullPath(string modFilePath, string iniRelativePath)
        {
            if (string.IsNullOrWhiteSpace(modFilePath) || string.IsNullOrWhiteSpace(iniRelativePath)) return string.Empty;
            if (File.Exists(modFilePath)) return modFilePath;
            if (!Directory.Exists(modFilePath)) return string.Empty;
            try { return Path.Combine(modFilePath, iniRelativePath); }
            catch { return string.Empty; }
        }

        private static int UpdatePersistValuesInIni(string iniFullPath, Dictionary<string, string> variables)
        {
            var changed = 0;
            try
            {
                var lines = File.ReadAllLines(iniFullPath);
                for (var i = 0; i < lines.Length; i++)
                {
                    var match = PersistDeclarationRegex.Match(lines[i]);
                    if (!match.Success) continue;
                    if (!variables.TryGetValue(match.Groups[1].Value, out var newValue)) continue;

                    var equalIndex = lines[i].IndexOf('=');
                    if (equalIndex < 0) continue;
                    lines[i] = lines[i].Substring(0, equalIndex + 1) + " " + newValue;
                    changed++;
                }

                if (changed > 0) File.WriteAllLines(iniFullPath, lines);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Failed to update INI '{iniFullPath}': {ex}");
            }

            return changed;
        }

        // =========================================================
        // State file
        // =========================================================

        private static void MigrateLegacyPersistStateFile()
        {
            try
            {
                if (!File.Exists(PersistStateFile) && File.Exists(LegacyPersistStateFile))
                    File.Move(LegacyPersistStateFile, PersistStateFile);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Failed to migrate legacy state file: {ex}");
            }
        }

        private void LoadPersistStates()
        {
            if (!File.Exists(PersistStateFile)) return;

            try
            {
                var json = File.ReadAllText(PersistStateFile);
                try
                {
                    var nested = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(json);
                    if (nested != null)
                    {
                        foreach (var game in nested)
                        {
                            var gameStates = GetOrCreateGameStates(game.Key);
                            foreach (var mod in game.Value ?? new Dictionary<string, Dictionary<string, string>>())
                                gameStates[mod.Key] = new Dictionary<string, string>(mod.Value ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        }
                        return;
                    }
                }
                catch (JsonException) { }

                // 兼容旧版：{ modPath: { ini\variable: value } }
                var legacy = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                if (legacy != null)
                {
                    _persistStates[LegacyGameKey] = legacy.ToDictionary(
                        pair => pair.Key,
                        pair => new Dictionary<string, string>(pair.Value ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Load state failed: {ex}");
            }
        }

        private void SavePersistStates()
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(PersistStateFile, JsonSerializer.Serialize(_persistStates, options));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Save state failed: {ex}");
            }
        }
    }
}
