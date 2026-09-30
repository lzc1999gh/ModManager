using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ModManager.Models;

namespace ModManager.Services
{
    /// <summary>
    /// 游戏配置与目录/图标管理：Data/Games 下每个游戏一个目录，
    /// 内含 game.json、state.json、CharacterInfo.json、CharacterPic/、Persist/。
    /// </summary>
    public class GameService
    {
        /// <summary>随程序发布的内置图标路径。</summary>
        public static string PackagedIconPath(string fileName) =>
            $"pack://siteoforigin:,,,/Resources/Icons/{fileName}";

        /// <summary>游戏数据目录（Data/Games/&lt;游戏&gt;）。</summary>
        public static string GetDirectory(Game? game) =>
            game == null ? string.Empty : AppDataPaths.GetGameDirectory(game.Id ?? game.Name);

        /// <summary>扫描 Data/Games 下所有游戏配置。</summary>
        public List<Game> LoadGames()
        {
            var games = new List<Game>();
            var gamesDirectory = AppDataPaths.GamesDirectory;
            if (!Directory.Exists(gamesDirectory)) return games;

            foreach (var directory in Directory.EnumerateDirectories(gamesDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var gameId = Path.GetFileName(directory);
                var configPath = AppDataPaths.GetGameConfigFilePath(gameId);
                if (!File.Exists(configPath)) continue;

                try
                {
                    var game = JsonSerializer.Deserialize<Game>(File.ReadAllText(configPath), JsonDefaults.Options);
                    if (game == null || string.IsNullOrWhiteSpace(game.Id)) continue;

                    game.CharacterInfoPath = string.IsNullOrWhiteSpace(game.CharacterInfoPath)
                        ? AppDataPaths.GetDefaultCharacterInfoPath()
                        : game.CharacterInfoPath;
                    game.CharacterPicPath = string.IsNullOrWhiteSpace(game.CharacterPicPath)
                        ? AppDataPaths.GetDefaultCharacterPicPath()
                        : game.CharacterPicPath;
                    game.GameIconPath ??= string.Empty;
                    EnsureDataDirectories(game);
                    EnsureIconPath(game);
                    games.Add(game);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[State] Failed to load game config '{configPath}': {ex}");
                }
            }
            return games;
        }

        /// <summary>保存游戏配置到 game.json。</summary>
        public bool SaveConfiguration(Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Id)) return false;
            try
            {
                EnsureDataDirectories(game);
                var configPath = AppDataPaths.GetGameConfigFilePath(game.Id);
                File.WriteAllText(configPath, JsonSerializer.Serialize(game, JsonDefaults.Options));
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[State] Failed to save game configuration: {ex}");
                return false;
            }
        }

        /// <summary>确保游戏目录、头像目录与 Persist 目录存在。</summary>
        public void EnsureDataDirectories(Game game)
        {
            if (game == null) return;
            var gameKey = game.Id ?? game.Name;
            Directory.CreateDirectory(AppDataPaths.GetGameDirectory(gameKey));
            Directory.CreateDirectory(AppDataPaths.ResolveGamePath(
                gameKey, game.CharacterPicPath, AppDataPaths.GetDefaultCharacterPicPath()));
            Directory.CreateDirectory(Path.Combine(AppDataPaths.GetGameDirectory(gameKey), "Persist"));
        }

        /// <summary>游戏图标文件在游戏目录中的绝对路径。</summary>
        public string GetIconFilePath(Game? game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.GameIconPath)) return string.Empty;
            return AppDataPaths.ResolveGamePath(game.Id ?? game.Name, game.GameIconPath);
        }

        /// <summary>根据配置刷新用于界面绑定的 IconPath / IsVectorIcon。</summary>
        public void EnsureIconPath(Game game)
        {
            if (game == null || game.IsAddGamePlaceholder) return;

            var iconPath = GetIconFilePath(game);
            game.IsVectorIcon = string.IsNullOrWhiteSpace(iconPath)
                || string.Equals(Path.GetExtension(iconPath), ".svg", StringComparison.OrdinalIgnoreCase);
            game.IconPath = !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath)
                ? iconPath
                : PackagedIconPath("mod.svg");
        }

        /// <summary>图标扩展名是否受支持。</summary>
        public static bool IsSupportedIcon(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension is ".svg" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
        }

        /// <summary>
        /// 保存游戏图标：复制到游戏目录下的 GameIcon.&lt;ext&gt; 并清理其它格式；
        /// sourcePath 为空表示清除图标。文件无效时抛出 IOException。
        /// </summary>
        public bool SaveIcon(Game game, string? sourcePath)
        {
            if (game == null) return false;
            var gameDirectory = AppDataPaths.GetGameDirectory(game.Id ?? game.Name);
            Directory.CreateDirectory(gameDirectory);

            sourcePath = sourcePath?.Trim().Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                foreach (var oldIcon in Directory.EnumerateFiles(gameDirectory, "GameIcon.*"))
                    File.Delete(oldIcon);
                game.GameIconPath = string.Empty;
                EnsureIconPath(game);
                return true;
            }

            if (!File.Exists(sourcePath) || !IsSupportedIcon(sourcePath))
                throw new IOException("游戏图标文件不存在或格式不受支持。");

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            var destination = Path.Combine(gameDirectory, "GameIcon" + extension);
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Copy(sourcePath, destination, overwrite: true);

            foreach (var oldIcon in Directory.EnumerateFiles(gameDirectory, "GameIcon.*")
                .Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(oldIcon);
            }

            game.GameIconPath = Path.GetRelativePath(gameDirectory, destination);
            EnsureIconPath(game);
            return true;
        }
    }
}
