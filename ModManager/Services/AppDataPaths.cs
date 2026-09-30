using System;
using System.IO;

namespace ModManager.Services
{
    internal static class AppDataPaths
    {
        // 所有应用数据都放在程序目录下，便于项目整体复制、迁移和删除。
        public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");

        public static string GamesDirectory => Path.Combine(DataDirectory, "Games");

        public static string GetGameDirectory(string gameId) =>
            Path.Combine(GamesDirectory, SanitizeFileName(gameId, "default"));

        public static string GetGameStateFilePath(string gameId) =>
            Path.Combine(GetGameDirectory(gameId), "state.json");

        public static string GetGameConfigFilePath(string gameId) =>
            Path.Combine(GetGameDirectory(gameId), "game.json");

        public static string GetPersistStateFilePath(string gameId) =>
            Path.Combine(GetGameDirectory(gameId), "Persist", "snapshots.json");

        public static string ResolveGamePath(string gameId, string? path, string? defaultRelativePath = null)
        {
            var effectivePath = string.IsNullOrWhiteSpace(path) ? defaultRelativePath : path.Trim();
            if (string.IsNullOrWhiteSpace(effectivePath)) return string.Empty;
            if (Path.IsPathRooted(effectivePath)) return effectivePath;

            return Path.GetFullPath(Path.Combine(GetGameDirectory(gameId),
                effectivePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static string ToGameRelativeOrAbsolutePath(string gameId, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            path = path.Trim();
            if (!Path.IsPathRooted(path)) return path.Replace('/', Path.DirectorySeparatorChar);

            try
            {
                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var gameDirectory = GetGameDirectory(gameId).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var prefix = gameDirectory + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetRelativePath(gameDirectory, fullPath)
                        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
            }
            catch { }

            return path;
        }

        public static string GetDefaultCharacterInfoPath() => "CharacterInfo.json";

        public static string GetDefaultCharacterPicPath() => "CharacterPic";

        public static string GetDefaultGameIconPath() => "GameIcon.svg";

        public static string SanitizeFileName(string value, string fallback)
        {
            var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            result = result.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(result) ? fallback : result;
        }
    }
}
