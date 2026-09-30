using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ModManager.Models;

namespace ModManager.Services
{
    /// <summary>按游戏读写角色状态快照（Data/Games/&lt;游戏&gt;/state.json）。</summary>
    public class StateStore
    {
        private sealed class GameStateSnapshot
        {
            public List<Character> Characters { get; set; } = new();
        }

        /// <summary>读取指定游戏的角色快照；文件不存在或损坏时返回空列表。</summary>
        public List<Character> Load(Game game)
        {
            var gameKey = GetGameKey(game);
            var stateFile = AppDataPaths.GetGameStateFilePath(gameKey);
            if (!File.Exists(stateFile)) return new List<Character>();

            try
            {
                var snapshot = JsonSerializer.Deserialize<GameStateSnapshot>(File.ReadAllText(stateFile), JsonDefaults.Options);
                var characters = snapshot?.Characters ?? new List<Character>();
                foreach (var character in characters)
                {
                    if (string.IsNullOrWhiteSpace(character.Id)) character.Id = Guid.NewGuid().ToString();
                    character.Mods ??= new ObservableCollection<Mod>();
                    character.GameId = game.Id;
                }
                return characters;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[State] Failed to load game state '{stateFile}': {ex}");
                return new List<Character>();
            }
        }

        /// <summary>写入指定游戏的角色快照（自动忽略占位项）。</summary>
        public void Save(Game game, IEnumerable<Character> characters)
        {
            var gameKey = GetGameKey(game);
            var snapshot = new GameStateSnapshot
            {
                Characters = characters.Where(character => !character.IsAddPlaceholder).ToList()
            };
            var stateFile = AppDataPaths.GetGameStateFilePath(gameKey);
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile) ?? AppDataPaths.GetGameDirectory(gameKey));
            File.WriteAllText(stateFile, JsonSerializer.Serialize(snapshot, JsonDefaults.Options));
        }

        /// <summary>游戏的唯一键：优先 Id，其次 Name。</summary>
        public static string GetGameKey(Game game) => game.Id ?? game.Name ?? string.Empty;
    }
}
