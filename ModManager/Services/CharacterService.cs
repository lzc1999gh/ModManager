using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ModManager.Models;

namespace ModManager.Services
{
    /// <summary>
    /// 角色信息与头像资源：角色名保存在 CharacterInfo.json，头像保存在 CharacterPic/。
    /// 角色信息文件是角色的唯一真实来源，头像缺失不影响角色存在。
    /// </summary>
    public class CharacterService
    {
        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        private readonly IDialogService _dialogs;

        public CharacterService(IDialogService dialogs) => _dialogs = dialogs;

        /// <summary>角色信息文件路径（未配置时回落到默认文件名）。</summary>
        public string GetInfoPath(Game game)
        {
            if (game == null) return string.Empty;
            game.CharacterInfoPath = string.IsNullOrWhiteSpace(game.CharacterInfoPath)
                ? AppDataPaths.GetDefaultCharacterInfoPath()
                : game.CharacterInfoPath;
            return AppDataPaths.ResolveGamePath(game.Id ?? game.Name, game.CharacterInfoPath);
        }

        /// <summary>头像目录路径（未配置时回落到默认目录名）。</summary>
        public string GetIconDirectory(Game game)
        {
            if (game == null) return string.Empty;
            game.CharacterPicPath = string.IsNullOrWhiteSpace(game.CharacterPicPath)
                ? AppDataPaths.GetDefaultCharacterPicPath()
                : game.CharacterPicPath;
            return AppDataPaths.ResolveGamePath(game.Id ?? game.Name, game.CharacterPicPath);
        }

        /// <summary>读取角色信息文件，按名称去重并忽略空名称。</summary>
        public List<CharacterInfo> ReadInfoFile(Game game)
        {
            var infoPath = GetInfoPath(game);
            if (!File.Exists(infoPath)) return new List<CharacterInfo>();

            try
            {
                var infos = JsonSerializer.Deserialize<List<CharacterInfo>>(File.ReadAllText(infoPath), JsonDefaults.Options);
                if (infos == null) return new List<CharacterInfo>();
                return infos
                    .Where(info => !string.IsNullOrWhiteSpace(info?.Name))
                    .GroupBy(info => info!.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch
            {
                return new List<CharacterInfo>();
            }
        }

        /// <summary>写入角色信息文件（补全 Id、去重、去空白）。</summary>
        public bool SaveInfoFile(Game game, IEnumerable<CharacterInfo> infos, bool showError = true)
        {
            try
            {
                var infoPath = GetInfoPath(game);
                Directory.CreateDirectory(Path.GetDirectoryName(infoPath) ?? AppDataPaths.GetGameDirectory(game.Id ?? game.Name));
                var normalized = infos
                    .Where(info => !string.IsNullOrWhiteSpace(info?.Name))
                    .Select(info => new CharacterInfo
                    {
                        Id = string.IsNullOrWhiteSpace(info.Id) ? Guid.NewGuid().ToString() : info.Id,
                        Name = info.Name.Trim()
                    })
                    .GroupBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                File.WriteAllText(infoPath, JsonSerializer.Serialize(normalized, JsonDefaults.Options));
                return true;
            }
            catch (Exception ex)
            {
                if (showError) _dialogs.ShowError($"保存角色信息失败：{ex.Message}", "角色");
                return false;
            }
        }

        /// <summary>在头像目录中按名称查找头像（不限扩展名）。</summary>
        public string? FindIcon(Game game, string characterName)
        {
            var iconDirectory = GetIconDirectory(game);
            if (string.IsNullOrWhiteSpace(iconDirectory) || !Directory.Exists(iconDirectory)) return null;

            foreach (var extension in ImageExtensions)
            {
                var path = Path.Combine(iconDirectory, characterName + extension);
                if (File.Exists(path)) return path;
            }
            return Directory.EnumerateFiles(iconDirectory)
                .FirstOrDefault(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileNameWithoutExtension(path), characterName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>用指定图片替换角色头像，并清理该角色的其它格式旧头像。</summary>
        public string SetIconFromFile(Game game, Character character, string sourceFilePath)
        {
            var iconDirectory = GetIconDirectory(game);
            Directory.CreateDirectory(iconDirectory);
            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            var destination = Path.Combine(iconDirectory, character.Name + extension);
            if (!string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceFilePath, destination, overwrite: true);
            }

            foreach (var oldIcon in Directory.EnumerateFiles(iconDirectory)
                .Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)
                    && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileNameWithoutExtension(path), character.Name, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(oldIcon);
            }
            return destination;
        }
    }
}
