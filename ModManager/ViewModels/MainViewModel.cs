using ModManager.Models;
using ModManager.Services;
using ModManager.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ModManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManager");
        private static readonly string StateFile = Path.Combine(StateDirectory, "mod_manager_state.json");
        private static readonly string LegacyStateFile = Path.Combine(StateDirectory, "modstate.json");
        private static readonly string UserCharacterInfoDirectory = Path.Combine(StateDirectory, "CharacterInfo");
        private readonly GimiPersistService _gimiPersistService;
        private readonly Dictionary<string, string?> _sourcesByModPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Character>> _charactersByGame = new(StringComparer.OrdinalIgnoreCase);
        // 列表末尾的“新增角色”占位项，始终保持在角色列表最后一位
        private readonly Character _addPlaceholder;
        // 游戏下拉菜单末尾的“增加游戏”占位项，不参与状态保存。
        private readonly Game _addGamePlaceholder;
        // 同一份无效配置在一次运行中只提示一次，避免切换面板时重复打断操作。
        private readonly HashSet<string> _shownModsRootWarnings = new(StringComparer.OrdinalIgnoreCase);
        private bool _ignoreAddGamePlaceholderSelection;

        // =========================================================
        // Collections
        // =========================================================
        public ObservableCollection<Game> Games { get; } = new ObservableCollection<Game>();
        public ObservableCollection<Character> Characters { get; } = new ObservableCollection<Character>();

        // =========================================================
        // Game
        // =========================================================
        public string ModsRootPath
        {
            get => SelectedGame?.ModsRootPath;
            set
            {
                if (SelectedGame != null && SelectedGame.ModsRootPath != value)
                {
                    SelectedGame.ModsRootPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private Game _selectedGame;
        public Game SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (value?.IsAddGamePlaceholder == true)
                {
                    if (_ignoreAddGamePlaceholderSelection)
                    {
                        OnPropertyChanged(nameof(SelectedGame));
                        return;
                    }
                    OnPropertyChanged(nameof(SelectedGame));
                    Application.Current?.Dispatcher.BeginInvoke(
                        new Action(AddGame),
                        System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }
                if (_selectedGame == value) return;
                SaveCurrentCharactersToCache();
                _selectedGame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModsRootPath));
                LoadCharactersForGame(_selectedGame);
                var gamePath = _selectedGame?.ModsRootPath;
                if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                {
                    LoadFromModsRoot(gamePath);
                }
                else
                {
                    ScheduleModsRootWarning(_selectedGame);
                }
            }
        }

        // =========================================================
        // Character
        // =========================================================
        private Character _selectedCharacter;
        public Character SelectedCharacter
        {
            get => _selectedCharacter;
            set
            {
                if (_selectedCharacter == value) return;
                _selectedCharacter = value;
                OnPropertyChanged();
                SelectedMod = value?.Mods.FirstOrDefault(mod => mod.Enabled) ?? value?.Mods.FirstOrDefault();
            }
        }

        // =========================================================
        // Mod
        // =========================================================
        private Mod _selectedMod;
        public Mod SelectedMod
        {
            get => _selectedMod;
            set
            {
                _selectedMod = value;
                OnPropertyChanged();
                LoadIniData();
            }
        }

        // =========================================================
        // Commands
        // =========================================================
        public ICommand ToggleModCommand { get; }
        public ICommand OpenModsRootCommand { get; }
        public ICommand RefreshModsCommand { get; }
        public ICommand AddPreviewCommand { get; }
        public ICommand AddPreviewFromClipboardCommand { get; }
        public ICommand DeletePreviewCommand { get; }
        public ICommand PrevPreviewCommand { get; }
        public ICommand NextPreviewCommand { get; }
        public ICommand DeleteModCommand { get; }
        public ICommand OpenIniCommand { get; }
        public ICommand SelectIniFileCommand { get; }
        public ICommand OpenModFolderCommand { get; }
        public ICommand AddCharacterCommand { get; }
        public ICommand AddCharacterIconCommand { get; }
        public ICommand RenameCharacterCommand { get; }
        public ICommand EditGameCommand { get; }
        public ICommand DeleteGameCommand { get; }

        // =========================================================
        // UI State
        // =========================================================
        private bool _showIniContent;
        public bool ShowIniContent
        {
            get => _showIniContent;
            set
            {
                if (_showIniContent == value) return;
                _showIniContent = value;
                OnPropertyChanged();
            }
        }

        private bool _showOnlyWithMods;
        public bool ShowOnlyWithMods
        {
            get => _showOnlyWithMods;
            set
            {
                if (_showOnlyWithMods != value)
                {
                    _showOnlyWithMods = value;
                    OnPropertyChanged();
                    CharactersView?.Refresh();
                }
            }
        }

        public ICollectionView CharactersView { get; private set; }

        // =========================================================
        // Constructor
        // =========================================================
        public MainViewModel()
        {
            _gimiPersistService = new GimiPersistService();
            _addPlaceholder = new Character { Id = Guid.NewGuid().ToString(), Name = "", IsAddPlaceholder = true };
            _addGamePlaceholder = new Game
            {
                Id = Guid.NewGuid().ToString(),
                Name = "增加游戏",
                IconPath = GetPackagedIconPath("add.svg"),
                IsAddGamePlaceholder = true
            };

            ToggleModCommand = new RelayCommand(p =>
            {
                if (p is not Mod mod) return;
                SelectedMod = mod;
                ToggleMod(mod);
            }, p => p is Mod);

            OpenModsRootCommand = new RelayCommand(p => OpenModsRoot(), p => SelectedGame != null);
            RefreshModsCommand = new RelayCommand(p => RefreshMods(), p => SelectedGame != null);

            AddPreviewCommand = new RelayCommand(p => AddPreviewsFromFiles(), p => SelectedMod != null);
            AddPreviewFromClipboardCommand = new RelayCommand(p => AddPreviewFromClipboard(), p => SelectedMod != null);
            DeletePreviewCommand = new RelayCommand(p => DeletePreview());
            PrevPreviewCommand = new RelayCommand(p => PrevPreview());
            NextPreviewCommand = new RelayCommand(p => NextPreview());

            DeleteModCommand = new RelayCommand(p => DeleteMod(), p => SelectedMod != null);
            OpenIniCommand = new RelayCommand(p => OpenIniFile(), p => SelectedMod != null);
            SelectIniFileCommand = new RelayCommand(p =>
            {
                if (p is IniFileInfo ini) SelectIniFile(ini);
            }, p => p is IniFileInfo);
            OpenModFolderCommand = new RelayCommand(OpenModFolder, p => SelectedMod != null);
            AddCharacterCommand = new RelayCommand(p => AddCharacter());
            AddCharacterIconCommand = new RelayCommand(p =>
            {
                if (p is Character character) ChangeCharacterIcon(character);
            }, p => p is Character character && !character.IsAddPlaceholder);
            RenameCharacterCommand = new RelayCommand(p =>
            {
                if (p is Character character) RenameCharacter(character);
            }, p => p is Character character && !character.IsAddPlaceholder);
            EditGameCommand = new RelayCommand(p =>
            {
                if (p is Game game) EditGame(game);
            }, p => p is Game game && !game.IsAddGamePlaceholder);
            DeleteGameCommand = new RelayCommand(p =>
            {
                if (p is Game game) DeleteGame(game);
            }, p => p is Game game && !game.IsAddGamePlaceholder);

            var resourceRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "CharacterPic");
            Games.Add(new Game
            {
                Id = "GI",
                Name = "GI",
                Path = Path.Combine(resourceRoot, "GI"),
                CharacterInfoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "CharacterInfo", "GI.json")
            });
            Games.Add(new Game
            {
                Id = "WW",
                Name = "WW",
                Path = Path.Combine(resourceRoot, "WW"),
                CharacterInfoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "CharacterInfo", "WW.json")
            });

            MigrateLegacyStateFile();
            LoadStateOrSample();
            foreach (var game in Games) EnsureGameIconPath(game);
            EnsureAddGamePlaceholder();

            CharactersView = CollectionViewSource.GetDefaultView(Characters);
            CharactersView.Filter = CharacterFilter;

            // The initial game is selected before the view is created; reload once so
            // the character and Mod panels receive their initial view data.
            if (SelectedGame != null)
            {
                LoadCharactersForGame(SelectedGame);
                var initialModsRoot = SelectedGame.ModsRootPath;
                if (!string.IsNullOrEmpty(initialModsRoot) && Directory.Exists(initialModsRoot))
                    LoadFromModsRoot(initialModsRoot);
            }
        }

        // =========================================================
        // Mod Folder
        // =========================================================
        private void EnsureAddGamePlaceholder()
        {
            _addGamePlaceholder.IconPath = GetPackagedIconPath("add.svg");
            if (!Games.Contains(_addGamePlaceholder)) Games.Add(_addGamePlaceholder);
        }

        private static string GetPackagedIconPath(string fileName) =>
            $"pack://siteoforigin:,,,/Resources/Icons/{fileName}";

        private static void EnsureGameIconPath(Game game)
        {
            if (game == null || game.IsAddGamePlaceholder) return;

            var gameId = game.Id?.Trim() ?? string.Empty;
            var iconName = gameId.Equals("GI", StringComparison.OrdinalIgnoreCase)
                ? "GI.svg"
                : gameId.Equals("WW", StringComparison.OrdinalIgnoreCase)
                    ? "WW.svg"
                    : "mod.svg";
            game.IconPath = GetPackagedIconPath(iconName);
        }

        private static bool HasValidModsRoot(Game game) =>
            !string.IsNullOrWhiteSpace(game?.ModsRootPath) && Directory.Exists(game.ModsRootPath);

        private static string DescribeModsRootProblem(string modsRootPath)
        {
            return string.IsNullOrWhiteSpace(modsRootPath)
                ? "尚未设置 Mod 根目录"
                : $"Mod 根目录不存在或无法访问：\n{modsRootPath}";
        }

        private static string GetModsRootWarningKey(Game game) =>
            $"{game?.Id ?? game?.Name}\u001F{game?.ModsRootPath}";

        private void MarkModsRootWarningShown(Game game)
        {
            if (game != null && !HasValidModsRoot(game))
                _shownModsRootWarnings.Add(GetModsRootWarningKey(game));
        }

        private void ScheduleModsRootWarning(Game game)
        {
            if (game == null || HasValidModsRoot(game)) return;
            var warningKey = GetModsRootWarningKey(game);
            if (!_shownModsRootWarnings.Add(warningKey)) return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(game, SelectedGame) || HasValidModsRoot(game)) return;
                var result = MessageBox.Show(
                    $"游戏“{game.Name}”{DescribeModsRootProblem(game.ModsRootPath)}。\n\n"
                    + "Mod 列表、导入和启用/禁用功能将不可用。是否现在修改游戏配置？",
                    "游戏配置不完整",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes) EditGame(game);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static bool ConfirmModsRootConfiguration(string gameName, string modsRootPath, string operation)
        {
            if (!string.IsNullOrWhiteSpace(modsRootPath) && Directory.Exists(modsRootPath)) return true;

            var result = MessageBox.Show(
                $"游戏“{gameName}”{DescribeModsRootProblem(modsRootPath)}。\n\n"
                + "可以先保存游戏信息，但 Mod 列表、导入和启用/禁用功能将不可用。是否仍要保存？",
                operation,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }

        private static bool EnsureValidModsRoot(Game game, string operation)
        {
            if (HasValidModsRoot(game)) return true;

            MessageBox.Show(
                $"无法{operation}：游戏“{game?.Name ?? "当前游戏"}”{DescribeModsRootProblem(game?.ModsRootPath)}。\n\n"
                + "请在左侧游戏图标栏中使用右键菜单的修改功能设置有效的 Mod 根目录。",
                "游戏配置不完整",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        private bool ConfirmDisableWithoutPersistSnapshot(Mod mod)
        {
            var result = MessageBox.Show(
                $"未找到游戏“{SelectedGame?.Name}”的 d3dx_user.ini，无法保存 Mod“{mod.Name}”当前的 Persist 状态。\n\n"
                + "已有快照会保留，但本次运行时状态可能丢失。是否仍要禁用该 Mod？",
                "Persist 配置缺失",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }

        private void OpenModFolder(object parameter)
        {
            if (SelectedMod == null) return;
            var path = SelectedMod.FilePath;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (File.Exists(path)) path = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }

        // =========================================================
        // Refresh Mods
        // =========================================================
        private void RefreshMods()
        {
            if (!EnsureValidModsRoot(SelectedGame, "刷新 Mod 列表")) return;
            LoadFromModsRoot(SelectedGame.ModsRootPath);
        }

        // =========================================================
        // Character Information and Icons
        // =========================================================
        private static readonly string[] CharacterImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        private string GetCharacterInfoPath(Game game)
        {
            if (!string.IsNullOrWhiteSpace(game?.CharacterInfoPath)) return game.CharacterInfoPath;

            var gameId = game?.Id;
            if (string.IsNullOrWhiteSpace(gameId)) gameId = game?.Name;
            if (string.IsNullOrWhiteSpace(gameId)) gameId = "unknown";
            foreach (var invalid in Path.GetInvalidFileNameChars()) gameId = gameId.Replace(invalid, '_');

            var packagedPath = Path.Combine(AppContext.BaseDirectory, "Resources", "CharacterInfo", gameId + ".json");
            if (File.Exists(packagedPath))
            {
                game.CharacterInfoPath = packagedPath;
                return packagedPath;
            }

            Directory.CreateDirectory(UserCharacterInfoDirectory);
            game.CharacterInfoPath = Path.Combine(UserCharacterInfoDirectory, gameId + ".json");
            return game.CharacterInfoPath;
        }

        private string GetCharacterInfoWritePath(Game game)
        {
            var infoPath = GetCharacterInfoPath(game);
            var packagedDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "CharacterInfo");
            var fullInfoPath = Path.GetFullPath(infoPath);
            var fullPackagedDirectory = Path.GetFullPath(packagedDirectory) + Path.DirectorySeparatorChar;
            if (!fullInfoPath.StartsWith(fullPackagedDirectory, StringComparison.OrdinalIgnoreCase)) return infoPath;

            var gameId = game?.Id;
            if (string.IsNullOrWhiteSpace(gameId)) gameId = game?.Name;
            if (string.IsNullOrWhiteSpace(gameId)) gameId = "unknown";
            foreach (var invalid in Path.GetInvalidFileNameChars()) gameId = gameId.Replace(invalid, '_');
            Directory.CreateDirectory(UserCharacterInfoDirectory);
            var userPath = Path.Combine(UserCharacterInfoDirectory, gameId + ".json");
            if (!File.Exists(userPath) && File.Exists(infoPath)) File.Copy(infoPath, userPath);
            game.CharacterInfoPath = userPath;
            return userPath;
        }

        private List<CharacterInfo> ReadCharacterInfoFile(Game game)
        {
            var infoPath = GetCharacterInfoPath(game);
            if (File.Exists(infoPath))
            {
                try
                {
                    var infos = JsonSerializer.Deserialize<List<CharacterInfo>>(File.ReadAllText(infoPath));
                    if (infos != null)
                    {
                        return infos
                            .Where(info => !string.IsNullOrWhiteSpace(info?.Name))
                            .GroupBy(info => info.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                            .Select(group => group.First())
                            .ToList();
                    }
                }
                catch { }
            }

            // 兼容旧版本：首次升级时从现有头像生成角色信息文件；之后列表不再依赖头像。
            var generated = new List<CharacterInfo>();
            if (!string.IsNullOrWhiteSpace(game?.Path) && Directory.Exists(game.Path))
            {
                foreach (var filePath in Directory.EnumerateFiles(game.Path)
                    .Where(path => CharacterImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
                {
                    var name = Path.GetFileNameWithoutExtension(filePath);
                    if (string.IsNullOrWhiteSpace(name) || generated.Any(info => string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                    generated.Add(new CharacterInfo { Id = Guid.NewGuid().ToString(), Name = name });
                }
            }
            SaveCharacterInfoFile(game, generated, showError: false);
            return generated;
        }

        private bool SaveCharacterInfoFile(Game game, IEnumerable<CharacterInfo> infos, bool showError = true)
        {
            try
            {
                var infoPath = GetCharacterInfoWritePath(game);
                Directory.CreateDirectory(Path.GetDirectoryName(infoPath));
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
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(infoPath, JsonSerializer.Serialize(normalized, options));
                return true;
            }
            catch (Exception ex)
            {
                if (showError)
                {
                    MessageBox.Show($"保存角色信息失败：{ex.Message}", "角色", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        private string FindCharacterIcon(Game game, string characterName)
        {
            if (string.IsNullOrWhiteSpace(game?.Path) || !Directory.Exists(game.Path)) return null;
            foreach (var extension in CharacterImageExtensions)
            {
                var path = Path.Combine(game.Path, characterName + extension);
                if (File.Exists(path)) return path;
            }
            return Directory.EnumerateFiles(game.Path)
                .FirstOrDefault(path => CharacterImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileNameWithoutExtension(path), characterName, StringComparison.OrdinalIgnoreCase));
        }

        private string GetCharacterIconDirectory(Game game)
        {
            if (game == null) return null;
            if (string.IsNullOrWhiteSpace(game.Path))
            {
                var gameId = string.IsNullOrWhiteSpace(game.Id) ? game.Name : game.Id;
                if (string.IsNullOrWhiteSpace(gameId)) gameId = "unknown";
                foreach (var invalid in Path.GetInvalidFileNameChars()) gameId = gameId.Replace(invalid, '_');
                game.Path = Path.Combine(StateDirectory, "CharacterPic", gameId);
            }
            return game.Path;
        }

        private List<Character> GetCachedCharacters(string gameId)
        {
            return gameId != null && _charactersByGame.TryGetValue(gameId, out var characters)
                ? characters
                : new List<Character>();
        }

        private void SaveCurrentCharactersToCache()
        {
            if (_selectedGame == null) return;
            _charactersByGame[_selectedGame.Id ?? _selectedGame.Name ?? string.Empty] = Characters
                .Where(character => !character.IsAddPlaceholder)
                .ToList();
        }

        // 保留旧方法作为内部兼容入口，但真正的角色来源已改为角色信息文件。
        public void LoadCharacterIconsFromFolders(string[] folders)
        {
            LoadCharactersForGame(SelectedGame);
        }

        // =========================================================
        // Load Characters
        // =========================================================
        public void LoadCharactersForGame(Game game)
        {
            Characters.Clear();
            SelectedCharacter = null;
            if (game == null) return;

            var cached = GetCachedCharacters(game.Id ?? game.Name ?? string.Empty);
            var infos = ReadCharacterInfoFile(game);
            var characters = new List<Character>();
            var infoChanged = false;

            foreach (var info in infos)
            {
                info.Id ??= Guid.NewGuid().ToString();
                var saved = cached.FirstOrDefault(character =>
                    (!string.IsNullOrWhiteSpace(info.Id) && string.Equals(character.Id, info.Id, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(character.Name, info.Name, StringComparison.OrdinalIgnoreCase));
                var character = saved ?? new Character { Id = info.Id, Name = info.Name };
                character.GameId = game.Id;
                character.Name = info.Name;
                character.IconPath = FindCharacterIcon(game, character.Name);
                characters.Add(character);
            }

            // 将旧状态中尚未写入角色信息文件的角色迁移进去，避免升级后丢失新增角色。
            foreach (var saved in cached)
            {
                if (characters.Any(character => string.Equals(character.Name, saved.Name, StringComparison.OrdinalIgnoreCase))) continue;
                saved.GameId = game.Id;
                saved.IconPath = FindCharacterIcon(game, saved.Name);
                characters.Add(saved);
                infos.Add(new CharacterInfo { Id = saved.Id, Name = saved.Name });
                infoChanged = true;
            }
            if (infoChanged) SaveCharacterInfoFile(game, infos, showError: false);

            foreach (var character in characters) Characters.Add(character);
            Characters.Add(_addPlaceholder);
            SelectedCharacter = Characters.FirstOrDefault(character => !character.IsAddPlaceholder);
            CharactersView?.Refresh();
        }

        // =========================================================
        // Add Character
        // =========================================================
        public void AddCharacter()
        {
            if (SelectedGame == null)
            {
                MessageBox.Show("请先选择游戏。", "新增角色", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialog("新增角色", "请输入角色名：")
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() != true) return;

            var name = dialog.ResultText;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("角色名不能为空，也不能包含文件名非法字符。", "新增角色", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (Characters.Any(character => !character.IsAddPlaceholder && string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("该角色已经存在。", "新增角色", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var character = new Character
            {
                Id = Guid.NewGuid().ToString(),
                GameId = SelectedGame.Id,
                Name = name,
                IconPath = FindCharacterIcon(SelectedGame, name)
            };
            var infos = Characters
                .Where(item => !item.IsAddPlaceholder)
                .Select(item => new CharacterInfo { Id = item.Id, Name = item.Name })
                .Append(new CharacterInfo { Id = character.Id, Name = character.Name });
            if (!SaveCharacterInfoFile(SelectedGame, infos)) return;

            Characters.Remove(_addPlaceholder);
            Characters.Add(character);
            Characters.Add(_addPlaceholder);
            SaveCurrentCharactersToCache();
            CharactersView?.Refresh();
            SelectedCharacter = character;
            SaveState();
        }

        // =========================================================
        // Add Game
        // =========================================================
        public void AddGame()
        {
            var dialog = new GameDialog
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() != true) return;

            var id = dialog.GameId.Trim();
            var name = dialog.GameName.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("游戏 ID 和名称不能为空，游戏 ID 不能包含文件名非法字符。", "增加游戏", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (Games.Any(game => string.Equals(game.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("该游戏 ID 已存在。", "增加游戏", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!ConfirmModsRootConfiguration(name, dialog.ModsRootPath, "增加游戏")) return;

            var game = new Game
            {
                Id = id,
                Name = name,
                Path = string.IsNullOrWhiteSpace(dialog.CharacterPicPath)
                    ? Path.Combine(StateDirectory, "CharacterPic", id)
                    : dialog.CharacterPicPath.Trim(),
                ModsRootPath = dialog.ModsRootPath.Trim(),
                D3dxUserIniPath = dialog.D3dxUserIniPath.Trim()
            };
            EnsureGameIconPath(game);
            MarkModsRootWarningShown(game);
            var addGameIndex = Games.IndexOf(_addGamePlaceholder);
            if (addGameIndex >= 0)
                Games.Insert(addGameIndex, game);
            else
                Games.Add(game);
            SelectedGame = game;
            SaveState();
            if (!string.IsNullOrWhiteSpace(game.ModsRootPath) && Directory.Exists(game.ModsRootPath))
                LoadFromModsRoot(game.ModsRootPath);
        }

        // =========================================================
        // Edit Game
        // =========================================================
        public void EditGame(Game game)
        {
            if (game == null || game.IsAddGamePlaceholder) return;

            var dialog = new GameDialog(game)
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() != true) return;

            var id = dialog.GameId.Trim();
            var name = dialog.GameName.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("游戏 ID 和名称不能为空，游戏 ID 不能包含文件名非法字符。", "修改游戏", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (Games.Any(item => !item.IsAddGamePlaceholder && item != game
                && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("该游戏 ID 已存在。", "修改游戏", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!ConfirmModsRootConfiguration(name, dialog.ModsRootPath, "修改游戏")) return;

            var oldKey = game.Id ?? game.Name ?? string.Empty;
            var newKey = id;
            var isSelected = ReferenceEquals(game, SelectedGame);
            if (isSelected) SaveCurrentCharactersToCache();

            _charactersByGame.TryGetValue(oldKey, out var cachedCharacters);
            if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
            {
                _charactersByGame.Remove(oldKey);
                if (cachedCharacters != null) _charactersByGame[newKey] = cachedCharacters;
            }

            game.Id = id;
            game.Name = name;
            game.ModsRootPath = dialog.ModsRootPath.Trim();
            game.Path = string.IsNullOrWhiteSpace(dialog.CharacterPicPath)
                ? Path.Combine(StateDirectory, "CharacterPic", id)
                : dialog.CharacterPicPath.Trim();
            game.D3dxUserIniPath = dialog.D3dxUserIniPath.Trim();
            EnsureGameIconPath(game);
            MarkModsRootWarningShown(game);
            _gimiPersistService.MoveGamePersistState(oldKey, newKey);

            if (cachedCharacters != null)
            {
                foreach (var character in cachedCharacters) character.GameId = id;
            }

            if (isSelected)
            {
                LoadCharactersForGame(game);
                if (!string.IsNullOrWhiteSpace(game.ModsRootPath) && Directory.Exists(game.ModsRootPath))
                    LoadFromModsRoot(game.ModsRootPath);
            }
            SaveState();
        }

        // =========================================================
        // Delete Game
        // =========================================================
        public void DeleteGame(Game game)
        {
            if (game == null || game.IsAddGamePlaceholder) return;

            if (Games.Count(item => !item.IsAddGamePlaceholder) <= 1)
            {
                MessageBox.Show("至少需要保留一个游戏。", "删除游戏", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"确定删除游戏“{game.Name}”的信息吗？\n不会删除磁盘上的 Mods 文件。",
                "删除游戏",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var isSelected = ReferenceEquals(game, SelectedGame);
            if (isSelected) SaveCurrentCharactersToCache();
            _charactersByGame.Remove(game.Id ?? game.Name ?? string.Empty);
            _gimiPersistService.RemoveGamePersistState(game);
            DeleteUserCharacterInfoFile(game);

            var nextGame = Games.FirstOrDefault(item =>
                !item.IsAddGamePlaceholder && !ReferenceEquals(item, game));
            _ignoreAddGamePlaceholderSelection = true;
            try
            {
                Games.Remove(game);
                if (isSelected) SelectedGame = nextGame;
            }
            finally
            {
                _ignoreAddGamePlaceholderSelection = false;
            }

            SaveState();
        }

        private static void DeleteUserCharacterInfoFile(Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.CharacterInfoPath)) return;

            try
            {
                var userDirectory = Path.GetFullPath(UserCharacterInfoDirectory).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var infoPath = Path.GetFullPath(game.CharacterInfoPath);
                if (infoPath.StartsWith(userDirectory, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(infoPath))
                {
                    File.Delete(infoPath);
                }
            }
            catch { }
        }

        // =========================================================
        // Rename Character
        // =========================================================
        public void RenameCharacter(Character character)
        {
            if (character == null || character.IsAddPlaceholder || SelectedGame == null) return;

            var dialog = new InputDialog("修改角色名", "请输入新的角色名：", character.Name)
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() != true) return;

            var requestedName = dialog.ResultText;
            if (string.IsNullOrWhiteSpace(requestedName) || requestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("角色名不能为空，也不能包含文件名非法字符。", "修改角色名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.Equals(character.Name, requestedName, StringComparison.OrdinalIgnoreCase)) return;
            if (Characters.Any(item => !item.IsAddPlaceholder && item != character
                && string.Equals(item.Name, requestedName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("该角色名已经存在。", "修改角色名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var oldName = character.Name;
            var modsRoot = SelectedGame.ModsRootPath;
            var oldCharacterDir = string.IsNullOrWhiteSpace(modsRoot) ? null : Path.Combine(modsRoot, oldName);
            var newCharacterDir = string.IsNullOrWhiteSpace(modsRoot) ? null : Path.Combine(modsRoot, requestedName);
            var oldIcon = FindCharacterIcon(SelectedGame, oldName);
            var newIcon = string.IsNullOrWhiteSpace(oldIcon) || string.IsNullOrWhiteSpace(SelectedGame.Path)
                ? null
                : Path.Combine(SelectedGame.Path, requestedName + Path.GetExtension(oldIcon));

            if (!string.IsNullOrWhiteSpace(newCharacterDir)
                && (Directory.Exists(newCharacterDir) || File.Exists(newCharacterDir)))
            {
                MessageBox.Show("目标角色的 Mod 文件夹已经存在。", "修改角色名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(newIcon) && File.Exists(newIcon))
            {
                MessageBox.Show("目标角色的头像文件已经存在。", "修改角色名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var movedCharacterDir = false;
            var movedIcon = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(oldCharacterDir) && Directory.Exists(oldCharacterDir))
                {
                    Directory.Move(oldCharacterDir, newCharacterDir);
                    movedCharacterDir = true;
                }
                if (!string.IsNullOrWhiteSpace(oldIcon) && !string.IsNullOrWhiteSpace(newIcon)
                    && !string.Equals(oldIcon, newIcon, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(oldIcon, newIcon);
                    movedIcon = true;
                }

                var infos = ReadCharacterInfoFile(SelectedGame);
                var info = infos.FirstOrDefault(item => string.Equals(item.Id, character.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (info == null)
                {
                    info = new CharacterInfo { Id = character.Id, Name = requestedName };
                    infos.Add(info);
                }
                else
                {
                    info.Name = requestedName;
                }
                if (!SaveCharacterInfoFile(SelectedGame, infos)) throw new IOException("角色信息文件保存失败。");

                if (movedCharacterDir)
                {
                    foreach (var mod in character.Mods)
                    {
                        var oldModPath = mod.FilePath;
                        if (!string.IsNullOrWhiteSpace(mod.FilePath)
                            && mod.FilePath.StartsWith(oldCharacterDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            mod.FilePath = Path.Combine(newCharacterDir, Path.GetRelativePath(oldCharacterDir, mod.FilePath));
                        }
                        UpdatePreviewPathsAfterMove(mod, oldCharacterDir, newCharacterDir, movedDirectory: true);
                        foreach (var ini in mod.IniFiles)
                        {
                            if (!string.IsNullOrWhiteSpace(ini.FilePath)
                                && ini.FilePath.StartsWith(oldCharacterDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            {
                                ini.FilePath = Path.Combine(newCharacterDir, Path.GetRelativePath(oldCharacterDir, ini.FilePath));
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(oldModPath) && !string.Equals(oldModPath, mod.FilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (_sourcesByModPath.TryGetValue(oldModPath, out var source))
                            {
                                _sourcesByModPath.Remove(oldModPath);
                                _sourcesByModPath[mod.FilePath] = source;
                            }
                            _gimiPersistService.MovePersistState(SelectedGame, oldModPath, mod.FilePath);
                        }
                    }
                }

                character.Name = requestedName;
                character.IconPath = newIcon;
                SaveCurrentCharactersToCache();
                CharactersView?.Refresh();
                SaveState();
            }
            catch (Exception ex)
            {
                if (movedIcon && File.Exists(newIcon))
                {
                    try { File.Move(newIcon, oldIcon); } catch { }
                }
                if (movedCharacterDir && Directory.Exists(newCharacterDir))
                {
                    try { Directory.Move(newCharacterDir, oldCharacterDir); } catch { }
                }
                MessageBox.Show($"修改角色名失败：{ex.Message}", "修改角色名", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // Change Character Icon
        // =========================================================
        public void ChangeCharacterIcon(Character character)
        {
            if (character == null || character.IsAddPlaceholder || SelectedGame == null) return;

            var dialog = new OpenFileDialog
            {
                Title = $"为“{character.Name}”选择头像",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog(Application.Current?.MainWindow) != true) return;

            try
            {
                var iconDirectory = GetCharacterIconDirectory(SelectedGame);
                Directory.CreateDirectory(iconDirectory);
                var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                var destination = Path.Combine(iconDirectory, character.Name + extension);
                if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(dialog.FileName, destination, overwrite: true);
                }

                foreach (var oldIcon in Directory.EnumerateFiles(iconDirectory)
                    .Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)
                        && CharacterImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                        && string.Equals(Path.GetFileNameWithoutExtension(path), character.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(oldIcon);
                }

                character.IconPath = destination;
                SaveState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加头像失败：{ex.Message}", "角色头像", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // Character Filter
        // =========================================================
        private bool CharacterFilter(object o)
        {
            if (o is not Models.Character character) return true;
            // 占位项始终放行，保证“新增角色”按钮不被“仅显示有 Mod”过滤掉
            if (character.IsAddPlaceholder) return true;
            if (!ShowOnlyWithMods) return true;
            var path = SelectedGame?.ModsRootPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return true;
            return character.Mods != null && character.Mods.Count > 0;
        }

        // =========================================================
        // Open Mods Root
        // =========================================================
        private void OpenModsRoot()
        {
            if (!EnsureValidModsRoot(SelectedGame, "打开 Mod 根目录")) return;
            try
            {
                var path = SelectedGame?.ModsRootPath;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "open" });
                }
            }
            catch { }
        }

        // =========================================================
        // Toggle Mod
        // =========================================================
        private void ToggleMod(Mod mod)
        {
            if (mod == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "切换 Mod 状态")) return;
            try
            {
                var path = mod.FilePath;
                // Enabled -> Disabled：先读取并保存当前 Persist，再禁用 Mod
                if (mod.Enabled)
                {
                    Debug.WriteLine($"[GIMI Persist] Toggle OFF: saving {mod.Name}");
                    var persistResult = _gimiPersistService.SaveCurrentPersist(SelectedGame, mod);
                    if (persistResult == PersistSnapshotSaveResult.UserIniUnavailable
                        && !ConfirmDisableWithoutPersistSnapshot(mod))
                    {
                        return;
                    }
                }
                // Disabled -> Enabled：先在 DISABLED_ 路径下恢复历史值，再启用 Mod。
                if (!mod.Enabled)
                {
                    Debug.WriteLine($"[GIMI Persist] Toggle ON: restoring {mod.Name}");
                    _gimiPersistService.RestorePersist(SelectedGame, mod);
                }
                if (Directory.Exists(path))
                {
                    MoveMod(mod, path, isDirectory: true);
                }
                else if (File.Exists(path))
                {
                    MoveMod(mod, path, isDirectory: false);
                }
                SaveState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Toggle failed: {ex}");
            }
        }

        // =========================================================
        // Move Mod
        // =========================================================
        private void MoveMod(Mod mod, string path, bool isDirectory)
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent)) return;
            var raw = Path.GetFileName(path);
            string newName;
            if (raw.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase))
            {
                // DISABLED_xxx -> xxx（启用）
                newName = raw.Substring("DISABLED_".Length);
            }
            else
            {
                // xxx -> DISABLED_xxx（禁用）
                newName = "DISABLED_" + raw;
            }
            var dest = Path.Combine(parent, newName);
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                dest = Path.Combine(parent, newName + "_" + Guid.NewGuid().ToString("N"));
            }
            if (isDirectory)
            {
                Directory.Move(path, dest);
                UpdatePreviewPathsAfterMove(mod, path, dest, movedDirectory: true);
            }
            else
            {
                File.Move(path, dest);
                UpdatePreviewPathsAfterMove(mod, path, dest, movedDirectory: false);
            }
            mod.FilePath = dest;
            var display = Path.GetFileName(dest);
            if (display.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase))
            {
                display = display.Substring("DISABLED_".Length);
            }
            mod.Name = display;
            mod.Enabled = !Path.GetFileName(dest).StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================
        // Load State
        // =========================================================
        private static void MigrateLegacyStateFile()
        {
            try
            {
                if (!File.Exists(StateFile) && File.Exists(LegacyStateFile))
                    File.Move(LegacyStateFile, StateFile);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[State] Failed to migrate legacy state file: {ex}");
            }
        }

        private void LoadStateOrSample()
        {
            if (File.Exists(StateFile))
            {
                try
                {
                    var json = File.ReadAllText(StateFile);
                    var doc = JsonSerializer.Deserialize<StateSnapshot>(json);
                    if (doc != null)
                    {
                        // 恢复每个 Mod 的来源信息（按路径建立映射）
                        foreach (var savedMod in doc.Characters?
                            .Where(character => character.Mods != null)
                            .SelectMany(character => character.Mods)
                            .Where(mod => !string.IsNullOrWhiteSpace(mod.FilePath)))
                        {
                            _sourcesByModPath[savedMod.FilePath] = savedMod.Source;
                        }
                        var savedGames = (doc.Games ?? Array.Empty<Game>()).ToArray();
                        if (savedGames.Length > 0)
                        {
                            Games.Clear();
                            foreach (var savedGame in savedGames) Games.Add(savedGame);
                        }

                        _charactersByGame.Clear();
                        var legacyGameId = Games.FirstOrDefault()?.Id ?? Games.FirstOrDefault()?.Name ?? string.Empty;
                        foreach (var savedCharacter in doc.Characters ?? Array.Empty<Character>())
                        {
                            savedCharacter.Id ??= Guid.NewGuid().ToString();
                            savedCharacter.Mods ??= new ObservableCollection<Mod>();
                            var gameId = string.IsNullOrWhiteSpace(savedCharacter.GameId) ? legacyGameId : savedCharacter.GameId;
                            savedCharacter.GameId = gameId;
                            if (!_charactersByGame.TryGetValue(gameId, out var characters))
                            {
                                characters = new List<Character>();
                                _charactersByGame[gameId] = characters;
                            }
                            characters.Add(savedCharacter);
                        }

                        SelectedGame = Games.FirstOrDefault();
                        return;
                    }
                }
                catch { }
            }

            // 首次运行直接使用内置游戏和角色信息文件，不再创建依赖头像的示例角色。
            SelectedGame = Games.FirstOrDefault();
            SaveState();
        }

        // =========================================================
        // Load Mods
        // =========================================================
        public void LoadFromModsRoot(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;
            if (SelectedGame != null) SelectedGame.ModsRootPath = rootPath;
            OnPropertyChanged(nameof(ModsRootPath));
            foreach (var character in Characters) character.Mods.Clear();

            foreach (var characterDir in Directory.GetDirectories(rootPath))
            {
                var folderName = Path.GetFileName(characterDir);
                var character = Characters.FirstOrDefault(c => string.Equals(c.Name, folderName, StringComparison.OrdinalIgnoreCase));
                if (character == null) continue;

                // Mod Directories
                foreach (var modDir in Directory.GetDirectories(characterDir))
                {
                    var modName = Path.GetFileName(modDir);
                    var enabled = !modName.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase);
                    var displayName = enabled ? modName : modName.Substring("DISABLED_".Length);
                    var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = displayName, FilePath = modDir, Enabled = enabled };
                    foreach (var previewPath in FindPreviewsForMod(mod)) mod.PreviewPaths.Add(previewPath);
                    ApplySavedSource(mod);
                    character.Mods.Add(mod);
                }

                // Mod Files
                foreach (var modFile in Directory.GetFiles(characterDir))
                {
                    var modName = Path.GetFileName(modFile);
                    var info = new FileInfo(modFile);
                    var enabled = !modName.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase);
                    var displayName = enabled ? modName : modName.Substring("DISABLED_".Length);
                    var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = displayName, FilePath = modFile, Size = info.Length, Enabled = enabled };
                    foreach (var previewPath in FindPreviewsForMod(mod)) mod.PreviewPaths.Add(previewPath);
                    ApplySavedSource(mod);
                    character.Mods.Add(mod);
                }
            }

            SelectedCharacter = SelectedCharacter ?? Characters.FirstOrDefault();
            SelectedMod = SelectedCharacter?.Mods.FirstOrDefault(m => m.Enabled) ?? SelectedCharacter?.Mods.FirstOrDefault();
            SaveState();
            CharactersView?.Refresh();
        }

        // =========================================================
        // Find Preview Images
        // =========================================================
        private List<string> FindPreviewsInDirectory(string dir)
        {
            var result = new List<string>();
            if (!Directory.Exists(dir)) return result;
            var extensions = new[] { ".png", ".jpg", ".jpeg" };
            return Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(filePath => extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                .Where(filePath => Path.GetFileNameWithoutExtension(filePath).StartsWith("preview_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> FindPreviewsForMod(Mod mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath)) return new List<string>();
            if (Directory.Exists(mod.FilePath)) return FindPreviewsInDirectory(mod.FilePath);
            if (!File.Exists(mod.FilePath)) return new List<string>();

            var parent = Path.GetDirectoryName(mod.FilePath);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return new List<string>();
            var prefix = Path.GetFileNameWithoutExtension(mod.FilePath) + ".preview_";
            var extensions = new[] { ".png", ".jpg", ".jpeg" };
            return Directory.EnumerateFiles(parent, "*.*", SearchOption.TopDirectoryOnly)
                .Where(filePath => extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                .Where(filePath => Path.GetFileNameWithoutExtension(filePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // =========================================================
        // Mod Folder
        // =========================================================
        private string GetModFolder(Mod mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.FilePath)) return null;
            if (Directory.Exists(mod.FilePath)) return mod.FilePath;
            return Path.GetDirectoryName(mod.FilePath);
        }

        // =========================================================
        // Next Preview Path
        // =========================================================
        private string GetNextPreviewPath(Mod mod, string extension = ".png")
        {
            var folder = GetModFolder(mod);
            if (string.IsNullOrWhiteSpace(folder)) return null;

            extension = string.IsNullOrWhiteSpace(extension) ? ".png" : extension.Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension;
            extension = extension.ToLowerInvariant();

            var prefix = Directory.Exists(mod.FilePath)
                ? "preview_"
                : Path.GetFileNameWithoutExtension(mod.FilePath) + ".preview_";
            int max = 0;
            if (Directory.Exists(folder))
            {
                foreach (var filePath in Directory.EnumerateFiles(folder, "*"))
                {
                    var name = Path.GetFileNameWithoutExtension(filePath);
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var numberText = name.Substring(prefix.Length);
                    if (int.TryParse(numberText, out var number) && number > max) max = number;
                }
            }
            return Path.Combine(folder, $"{prefix}{max + 1}{extension}");
        }

        // =========================================================
        // Add Preview
        // =========================================================
        private void AddPreviewsFromFiles()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "添加 Mod 预览图")) return;
            var folder = GetModFolder(mod);
            if (string.IsNullOrEmpty(folder)) return;

            var dialog = new OpenFileDialog
            {
                Title = "选择 Mod 预览图（可多选）",
                Filter = "图片文件|*.png;*.jpg;*.jpeg|PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg",
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true
            };
            if (dialog.ShowDialog() != true) return;

            if (mod.PreviewPaths == null)
                mod.PreviewPaths = new ObservableCollection<string>();

            var addedCount = 0;
            foreach (var sourcePath in dialog.FileNames)
            {
                try
                {
                    var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                    if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") continue;
                    var newPath = GetNextPreviewPath(mod, extension);
                    File.Copy(sourcePath, newPath, overwrite: false);
                    mod.PreviewPaths.Add(newPath);
                    addedCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Preview] Failed to copy '{sourcePath}': {ex}");
                }
            }

            if (addedCount == 0)
            {
                MessageBox.Show("没有成功添加预览图片。", "添加预览", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            mod.CurrentPreviewIndex = mod.PreviewPaths.Count - 1;
            SaveState();
        }

        private void AddPreviewFromClipboard()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "添加 Mod 预览图")) return;
            var folder = GetModFolder(mod);
            if (string.IsNullOrEmpty(folder)) return;
            if (!Clipboard.ContainsImage())
            {
                MessageBox.Show("剪贴板中没有图片", "添加预览", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var bitmap = Clipboard.GetImage();
            if (bitmap == null) return;
            var newPath = GetNextPreviewPath(mod, ".png");
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(newPath)) encoder.Save(stream);
            }
            catch
            {
                MessageBox.Show("保存预览图片失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (mod.PreviewPaths == null) mod.PreviewPaths = new ObservableCollection<string>();
            mod.PreviewPaths.Add(newPath);
            mod.CurrentPreviewIndex = mod.PreviewPaths.Count - 1;
            SaveState();
        }

        // =========================================================
        // Delete Preview
        // =========================================================
        private void DeletePreview()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "删除 Mod 预览图")) return;
            var current = mod.CurrentPreviewPath;
            if (string.IsNullOrEmpty(current)) return;
            try { if (File.Exists(current)) File.Delete(current); } catch { }
            if (mod.PreviewPaths != null && mod.PreviewPaths.Count > 0)
            {
                int index = mod.CurrentPreviewIndex;
                if (index >= 0 && index < mod.PreviewPaths.Count) mod.PreviewPaths.RemoveAt(index);
                mod.CurrentPreviewIndex = Math.Min(mod.CurrentPreviewIndex, mod.PreviewPaths.Count - 1);
            }
            SaveState();
        }

        // =========================================================
        // Previous Preview
        // =========================================================
        private void PrevPreview()
        {
            var mod = SelectedMod;
            if (mod == null || mod.PreviewPaths == null || mod.PreviewPaths.Count <= 1) return;
            int count = mod.PreviewPaths.Count;
            mod.CurrentPreviewIndex = (mod.CurrentPreviewIndex - 1 + count) % count;
        }

        // =========================================================
        // Next Preview
        // =========================================================
        private void NextPreview()
        {
            var mod = SelectedMod;
            if (mod == null || mod.PreviewPaths == null || mod.PreviewPaths.Count <= 1) return;
            int count = mod.PreviewPaths.Count;
            mod.CurrentPreviewIndex = (mod.CurrentPreviewIndex + 1) % count;
        }

        // =========================================================
        // Import Files
        // =========================================================
        public void ImportFiles(string[] paths, Character target)
        {
            if (paths == null || target == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "导入 Mod")) return;
            var rootPath = SelectedGame.ModsRootPath;
            var targetDir = Path.Combine(rootPath, target.Name);
            Directory.CreateDirectory(targetDir);
            foreach (var sourcePath in paths)
            {
                try
                {
                    if (Directory.Exists(sourcePath))
                    {
                        var destination = Path.Combine(targetDir, Path.GetFileName(sourcePath));
                        if (!ConfirmOverwrite(destination)) continue;
                        CopyDirectory(sourcePath, destination);
                        var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(sourcePath), FilePath = destination, Enabled = true };
                        foreach (var previewPath in FindPreviewsInDirectory(destination)) mod.PreviewPaths.Add(previewPath);
                        target.Mods.Add(mod);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                        if (extension == ".zip")
                        {
                            var modFolderName = Path.GetFileNameWithoutExtension(sourcePath);
                            var destination = Path.Combine(targetDir, modFolderName);
                            if (!ConfirmOverwrite(destination)) continue;
                            Directory.CreateDirectory(destination);
                            try
                            {
                                ZipFile.ExtractToDirectory(sourcePath, destination);
                            }
                            catch
                            {
                                // 解压失败时回退为直接复制 zip 文件
                                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                                var fallback = Path.Combine(targetDir, Path.GetFileName(sourcePath));
                                if (!ConfirmOverwrite(fallback)) continue;
                                File.Copy(sourcePath, fallback);
                                var fallbackInfo = new FileInfo(fallback);
                                var fallbackMod = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(fallback), FilePath = fallback, Size = fallbackInfo.Length, Enabled = true };
                                target.Mods.Add(fallbackMod);
                                continue;
                            }
                            var zipMod = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(destination), FilePath = destination, Enabled = true };
                            foreach (var previewPath in FindPreviewsInDirectory(destination)) zipMod.PreviewPaths.Add(previewPath);
                            target.Mods.Add(zipMod);
                        }
                        else
                        {
                            var destination = Path.Combine(targetDir, Path.GetFileName(sourcePath));
                            if (!ConfirmOverwrite(destination)) continue;
                            File.Copy(sourcePath, destination);
                            var info = new FileInfo(destination);
                            var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(destination), FilePath = destination, Size = info.Length, Enabled = true };
                            target.Mods.Add(mod);
                        }
                    }
                }
                catch { }
            }
            SaveState();
        }

        // =========================================================
        // Delete Mod
        // =========================================================
        private void DeleteMod()
        {
            var mod = SelectedMod;
            var character = SelectedCharacter;
            if (mod == null || character == null || string.IsNullOrWhiteSpace(mod.FilePath)) return;
            if (!EnsureValidModsRoot(SelectedGame, "删除 Mod")) return;
            var result = MessageBox.Show($"确定要删除 Mod“{mod.Name}”吗？此操作会删除磁盘上的文件，无法撤销。", "删除 Mod", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                if (Directory.Exists(mod.FilePath)) Directory.Delete(mod.FilePath, recursive: true);
                else if (File.Exists(mod.FilePath)) File.Delete(mod.FilePath);
                _gimiPersistService.RemovePersistState(SelectedGame, mod);
                _sourcesByModPath.Remove(mod.FilePath);
                character.Mods.Remove(mod);
                SelectedMod = character.Mods.FirstOrDefault(m => m.Enabled) ?? character.Mods.FirstOrDefault();
                SaveState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除 Mod 失败：{ex.Message}", "删除 Mod", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // Rename Mod
        // =========================================================
        public void CommitModRename(Mod mod)
        {
            if (mod == null) return;
            var originalName = mod.OriginalNameDuringEdit;
            var requestedName = mod.Name?.Trim();
            mod.OriginalNameDuringEdit = null;
            if (string.IsNullOrWhiteSpace(originalName) || string.Equals(originalName, requestedName, StringComparison.Ordinal)) return;
            if (!EnsureValidModsRoot(SelectedGame, "修改 Mod 名称"))
            {
                mod.Name = originalName;
                return;
            }
            if (string.IsNullOrWhiteSpace(requestedName) || requestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                mod.Name = originalName;
                MessageBox.Show("Mod 名称不能为空，也不能包含文件名非法字符。", "修改 Mod 名称", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var oldPath = mod.FilePath;
                if (string.IsNullOrWhiteSpace(oldPath)) throw new InvalidOperationException("找不到 Mod 的原始路径。");
                var parent = Path.GetDirectoryName(oldPath);
                if (string.IsNullOrWhiteSpace(parent)) throw new InvalidOperationException("找不到 Mod 的父目录。");
                var oldRawName = Path.GetFileName(oldPath);
                var disabledPrefix = oldRawName.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase) ? "DISABLED_" : string.Empty;
                var newPath = Path.Combine(parent, disabledPrefix + requestedName);
                if (File.Exists(newPath) || Directory.Exists(newPath)) throw new IOException("同名 Mod 已存在。");
                // 移动前记住 Persist 身份
                var oldPersistPath = oldPath;
                if (Directory.Exists(oldPath))
                {
                    Directory.Move(oldPath, newPath);
                    UpdatePreviewPathsAfterMove(mod, oldPath, newPath, movedDirectory: true);
                }
                else if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                    UpdatePreviewPathsAfterMove(mod, oldPath, newPath, movedDirectory: false);
                }
                else
                {
                    throw new FileNotFoundException("原始 Mod 文件或目录不存在。", oldPath);
                }
                mod.FilePath = newPath;
                mod.Name = requestedName;
                // 重命名后迁移 Persist 状态，防止状态丢失
                _gimiPersistService.MovePersistState(SelectedGame, oldPersistPath, newPath);
                _sourcesByModPath.Remove(oldPath);
                _sourcesByModPath[newPath] = mod.Source;
                SaveState();
            }
            catch (Exception ex)
            {
                mod.Name = originalName;
                MessageBox.Show($"修改 Mod 名称失败：{ex.Message}", "修改 Mod 名称", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // Mod Source
        // =========================================================
        public void SaveModSource(Mod mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath)) return;
            _sourcesByModPath[mod.FilePath] = mod.Source;
            SaveState();
        }

        private void ApplySavedSource(Mod mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.FilePath)) return;
            if (_sourcesByModPath.TryGetValue(mod.FilePath, out var source)) mod.Source = source;
        }

        // =========================================================
        // INI
        // =========================================================
        private void LoadIniData()
        {
            var mod = SelectedMod;
            if (mod == null) return;

            mod.IniFiles.Clear();
            mod.ToggleIniFiles.Clear();
            mod.SelectedIniFile = null;
            mod.IniFilePath = null;
            mod.IniContent = null;
            var iniPaths = FindIniFiles(mod);
            if (iniPaths.Count == 0)
            {
                return;
            }
            try
            {
                foreach (var iniPath in iniPaths)
                {
                    var content = File.ReadAllText(iniPath);
                    var folder = GetModFolder(mod);
                    var relativePath = Directory.Exists(folder)
                        ? Path.GetRelativePath(folder, iniPath)
                        : Path.GetFileName(iniPath);
                    var ini = new IniFileInfo
                    {
                        FilePath = iniPath,
                        RelativePath = relativePath,
                        Content = content
                    };
                    LoadIniShortcuts(ini);
                    mod.IniFiles.Add(ini);
                    if (ini.HasToggleKey) mod.ToggleIniFiles.Add(ini);
                }

                mod.SelectedIniFile = mod.ToggleIniFiles.FirstOrDefault() ?? mod.IniFiles.FirstOrDefault();
                SaveState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 INI 失败：{ex.Message}", "读取 INI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // Open INI
        // =========================================================
        private void SelectIniFile(IniFileInfo ini)
        {
            if (SelectedMod == null || ini == null) return;
            SelectedMod.SelectedIniFile = ini;
        }

        private void OpenIniFile()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            var ini = mod.SelectedIniFile ?? mod.ToggleIniFiles.FirstOrDefault() ?? mod.IniFiles.FirstOrDefault();
            if (ini == null)
            {
                MessageBox.Show("当前 Mod 中未找到 INI 文件。", "打开 INI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                mod.SelectedIniFile = ini;
                Process.Start(new ProcessStartInfo(ini.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开 INI 失败：{ex.Message}", "打开 INI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // Find INI
        // =========================================================
        private List<string> FindIniFiles(Mod mod)
        {
            if (mod != null && File.Exists(mod.FilePath))
            {
                return string.Equals(Path.GetExtension(mod.FilePath), ".ini", StringComparison.OrdinalIgnoreCase)
                    ? new List<string> { mod.FilePath }
                    : new List<string>();
            }
            var folder = GetModFolder(mod);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return new List<string>();
            try
            {
                return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetExtension(path), ".ini", StringComparison.OrdinalIgnoreCase))
                    // The selected Mod folder itself may be named DISABLED_xxx. Only ignore
                    // disabled folders nested inside that Mod.
                    .Where(path => !Path.GetRelativePath(folder, path)
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(part => part.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INI] Find failed for '{folder}': {ex}");
                return new List<string>();
            }
        }

        // =========================================================
        // INI Shortcuts
        // =========================================================
        private static void LoadIniShortcuts(IniFileInfo ini)
        {
            if (string.IsNullOrWhiteSpace(ini?.Content)) return;
            string? section = null;
            foreach (var line in ini.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
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
                // Different mods use names such as KeyHair or KeyEye instead of KeyToggle.
                // Any valid key section should make this INI selectable in the UI.
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

        // =========================================================
        // Update Preview Paths
        // =========================================================
        private static void UpdatePreviewPathsAfterMove(Mod mod, string oldPath, string newPath, bool movedDirectory)
        {
            for (var index = 0; index < mod.PreviewPaths.Count; index++)
            {
                var previewPath = mod.PreviewPaths[index];
                if (!movedDirectory && string.Equals(previewPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    mod.PreviewPaths[index] = newPath;
                    continue;
                }
                if (!movedDirectory || !previewPath.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                var relativePath = Path.GetRelativePath(oldPath, previewPath);
                mod.PreviewPaths[index] = Path.Combine(newPath, relativePath);
            }
        }

        // =========================================================
        // Confirm Overwrite
        // =========================================================
        private static bool ConfirmOverwrite(string destination)
        {
            if (!File.Exists(destination) && !Directory.Exists(destination)) return true;
            var name = Path.GetFileName(destination);
            var result = MessageBox.Show($"“{name}”已存在。是否覆盖？", "导入 Mod", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return false;
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            else File.Delete(destination);
            return true;
        }

        // =========================================================
        // Copy Directory
        // =========================================================
        private void CopyDirectory(string sourceDir, string destDir)
        {
            var directory = new DirectoryInfo(sourceDir);
            if (!directory.Exists) return;
            Directory.CreateDirectory(destDir);
            foreach (var file in directory.GetFiles()) file.CopyTo(Path.Combine(destDir, file.Name), true);
            foreach (var subDirectory in directory.GetDirectories()) CopyDirectory(subDirectory.FullName, Path.Combine(destDir, subDirectory.Name));
        }

        // =========================================================
        // Save State
        // =========================================================
        public void SaveState()
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                SaveCurrentCharactersToCache();
                // 序列化时排除占位项，避免把“新增角色”按钮持久化到状态文件
                var allCharacters = _charactersByGame.Values
                    .SelectMany(characters => characters)
                    .Where(character => !character.IsAddPlaceholder)
                    .GroupBy(character => $"{character.GameId}\u0000{character.Id}", StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray();
                var savedGames = Games
                    .Where(game => !game.IsAddGamePlaceholder)
                    .ToArray();
                var snapshot = new StateSnapshot { Games = savedGames, Characters = allCharacters };
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(StateFile, JsonSerializer.Serialize(snapshot, options));
            }
            catch { }
        }

        // =========================================================
        // PropertyChanged
        // =========================================================
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // =========================================================
        // State Snapshot
        // =========================================================
        private class StateSnapshot
        {
            public Game[] Games { get; set; }
            public Character[] Characters { get; set; }
        }
    }
}
