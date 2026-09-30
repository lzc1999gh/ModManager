using ModManager.Models;
using ModManager.Services;
using ModManager.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ModManager.ViewModels
{
    /// <summary>
    /// 主视图模型：只负责界面状态、命令与流程编排，
    /// 具体文件/配置操作已下沉到 Services 下的各服务类。
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly GameService _gameService = new();
        private readonly CharacterService _characterService;
        private readonly PreviewService _previewService = new();
        private readonly ModService _modService;
        private readonly IniService _iniService = new();
        private readonly StateStore _stateStore = new();
        private readonly GimiPersistService _gimiPersistService = new();
        private readonly CharacterInfoSyncService _characterInfoSyncService = new();
        private readonly IDialogService _dialogs = new DialogService();

        private readonly Dictionary<string, string?> _sourcesByModPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Character>> _charactersByGame = new(StringComparer.OrdinalIgnoreCase);
        // 列表末尾的“新增角色”占位项，始终保持在角色列表最后一位
        private readonly Character _addPlaceholder;
        // 游戏下拉菜单末尾的“增加游戏”占位项，不参与状态保存
        private readonly Game _addGamePlaceholder;
        // 同一份无效配置在一次运行中只提示一次，避免切换面板时重复打断操作
        private readonly HashSet<string> _shownModsRootWarnings = new(StringComparer.OrdinalIgnoreCase);
        private bool _ignoreAddGamePlaceholderSelection;

        // =========================================================
        // Collections
        // =========================================================
        public ObservableCollection<Game> Games { get; } = new();
        public ObservableCollection<Character> Characters { get; } = new();

        public MainViewModel()
        {
            _characterService = new CharacterService(_dialogs);
            _modService = new ModService(_previewService);

            _addPlaceholder = new Character { Id = Guid.NewGuid().ToString(), Name = "", IsAddPlaceholder = true };
            _addGamePlaceholder = new Game
            {
                Id = Guid.NewGuid().ToString(),
                Name = "增加游戏",
                IconPath = GameService.PackagedIconPath("add.svg"),
                IsAddGamePlaceholder = true
            };

            CreateCommands();

            LoadGamesFromData();
            LoadStateOrSample();
            foreach (var game in Games) _gameService.EnsureIconPath(game);
            EnsureAddGamePlaceholder();

            // 初始游戏在视图创建前就已选中，这里重载一次，让角色与 Mod 面板拿到初始数据
            if (SelectedGame != null)
            {
                LoadCharactersForGame(SelectedGame);
                var initialModsRoot = SelectedGame.ModsRootPath;
                if (!string.IsNullOrWhiteSpace(initialModsRoot) && Directory.Exists(initialModsRoot))
                    LoadFromModsRoot(initialModsRoot);
            }
        }

        // =========================================================
        // Game
        // =========================================================
        public string? ModsRootPath
        {
            get => SelectedGame?.ModsRootPath;
            set
            {
                if (SelectedGame != null && SelectedGame.ModsRootPath != value)
                {
                    SelectedGame.ModsRootPath = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        private Game? _selectedGame;
        public Game? SelectedGame
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
                OnPropertyChanged(nameof(CanSyncCharacterInfo));
                CommandManager.InvalidateRequerySuggested();
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
        private Character? _selectedCharacter;
        public Character? SelectedCharacter
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
        private Mod? _selectedMod;
        public Mod? SelectedMod
        {
            get => _selectedMod;
            set
            {
                if (_selectedMod == value) return;
                _selectedMod = value;
                OnPropertyChanged();
                LoadIniData();
            }
        }

        // =========================================================
        // Commands
        // =========================================================
        public ICommand ToggleModCommand { get; private set; } = null!;
        public ICommand OpenModsRootCommand { get; private set; } = null!;
        public ICommand RefreshModsCommand { get; private set; } = null!;
        public ICommand AddPreviewCommand { get; private set; } = null!;
        public ICommand AddPreviewFromClipboardCommand { get; private set; } = null!;
        public ICommand DeletePreviewCommand { get; private set; } = null!;
        public ICommand PrevPreviewCommand { get; private set; } = null!;
        public ICommand NextPreviewCommand { get; private set; } = null!;
        public ICommand DeleteModCommand { get; private set; } = null!;
        public ICommand OpenIniCommand { get; private set; } = null!;
        public ICommand SelectIniFileCommand { get; private set; } = null!;
        public ICommand OpenModFolderCommand { get; private set; } = null!;
        public ICommand AddCharacterCommand { get; private set; } = null!;
        public ICommand SyncCharacterInfoCommand { get; private set; } = null!;
        public ICommand AddCharacterIconCommand { get; private set; } = null!;
        public ICommand RenameCharacterCommand { get; private set; } = null!;
        public ICommand EditGameCommand { get; private set; } = null!;
        public ICommand DeleteGameCommand { get; private set; } = null!;

        private void CreateCommands()
        {
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
            SyncCharacterInfoCommand = new RelayCommand(p => _ = SyncCharacterInfoAsync(), p => CanSyncCharacterInfo);
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
        }

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

        // 角色列表显示范围：默认（不含隐藏）/ 仅有 Mod / 全部（含隐藏）
        private CharacterListMode _listMode = CharacterListMode.Default;
        public CharacterListMode ListMode
        {
            get => _listMode;
            set
            {
                if (_listMode == value) return;
                _listMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefaultListMode));
                OnPropertyChanged(nameof(IsWithModsListMode));
                OnPropertyChanged(nameof(IsAllListMode));
                RefreshCharactersView();
            }
        }

        // 以下三个属性供右上角三个圆点按钮双向绑定（RadioButton 天然互斥）
        public bool IsDefaultListMode
        {
            get => _listMode == CharacterListMode.Default;
            set { if (value) ListMode = CharacterListMode.Default; }
        }

        public bool IsWithModsListMode
        {
            get => _listMode == CharacterListMode.WithMods;
            set { if (value) ListMode = CharacterListMode.WithMods; }
        }

        public bool IsAllListMode
        {
            get => _listMode == CharacterListMode.All;
            set { if (value) ListMode = CharacterListMode.All; }
        }

        /// <summary>
        /// 当前列表模式下实际可见的角色（占位项始终排在最后）。
        /// 用独立集合并以"最小增删"同步，取代 ICollectionView.Refresh()：
        /// Reset 会让 ListBox 丢弃并重建全部容器（GI 130 个），切换模式时肉眼可见地卡；
        /// 只增删差异项则通常只有个位数变更。
        /// </summary>
        public ObservableCollection<Character> VisibleCharacters { get; } = new();

        private int _visibleCharacterCount;

        /// <summary>当前筛选条件下可见的角色数量（不含“新增角色”占位项）。</summary>
        public int VisibleCharacterCount => _visibleCharacterCount;

        /// <summary>按当前列表模式重算可见角色，并同步数量徽标。</summary>
        private void RefreshCharactersView()
        {
            var target = Characters.Where(CharacterFilter).ToList();

            // target 与 VisibleCharacters 都是 Characters 的有序子序列，
            // 因此可以双指针走一遍，左边多出的删掉、右边缺了的补上，其余原地不动。
            var targetSet = new HashSet<Character>(target);
            int ti = 0, ci = 0;

            while (ti < target.Count || ci < VisibleCharacters.Count)
            {
                if (ti < target.Count && ci < VisibleCharacters.Count &&
                    ReferenceEquals(target[ti], VisibleCharacters[ci]))
                {
                    ti++;
                    ci++;
                    continue;
                }

                if (ci < VisibleCharacters.Count && !targetSet.Contains(VisibleCharacters[ci]))
                {
                    VisibleCharacters.RemoveAt(ci);
                    continue;
                }

                if (ti < target.Count)
                {
                    VisibleCharacters.Insert(ci, target[ti]);
                    ti++;
                    ci++;
                    continue;
                }

                break; // 兜底，正常情况下不会走到
            }

            _visibleCharacterCount = VisibleCharacters.Count(character => !character.IsAddPlaceholder);
            OnPropertyChanged(nameof(VisibleCharacterCount));
        }

        public bool CanSyncCharacterInfo => SelectedGame != null
            && !_isSyncingCharacterInfo
            && _characterInfoSyncService.Supports(SelectedGame);

        private bool _isSyncingCharacterInfo;
        public bool IsSyncingCharacterInfo
        {
            get => _isSyncingCharacterInfo;
            private set
            {
                if (_isSyncingCharacterInfo == value) return;
                _isSyncingCharacterInfo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSyncCharacterInfo));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // =========================================================
        // Game Loading / Validation
        // =========================================================
        private void LoadGamesFromData()
        {
            Games.Clear();
            foreach (var game in _gameService.LoadGames()) Games.Add(game);
        }

        private void EnsureAddGamePlaceholder()
        {
            _addGamePlaceholder.IconPath = GameService.PackagedIconPath("add.svg");
            if (!Games.Contains(_addGamePlaceholder)) Games.Add(_addGamePlaceholder);
        }

        private static bool HasValidModsRoot(Game? game) =>
            !string.IsNullOrWhiteSpace(game?.ModsRootPath) && Directory.Exists(game!.ModsRootPath);

        private static string DescribeModsRootProblem(string? modsRootPath) =>
            string.IsNullOrWhiteSpace(modsRootPath)
                ? "尚未设置 Mod 根目录"
                : $"Mod 根目录不存在或无法访问：\n{modsRootPath}";

        private static string GetModsRootWarningKey(Game? game) =>
            $"{game?.Id ?? game?.Name}\u001F{game?.ModsRootPath}";

        private void MarkModsRootWarningShown(Game? game)
        {
            if (game != null && !HasValidModsRoot(game))
                _shownModsRootWarnings.Add(GetModsRootWarningKey(game));
        }

        private void ScheduleModsRootWarning(Game? game)
        {
            if (game == null || HasValidModsRoot(game)) return;
            var warningKey = GetModsRootWarningKey(game);
            if (!_shownModsRootWarnings.Add(warningKey)) return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(game, SelectedGame) || HasValidModsRoot(game)) return;
                var confirmed = _dialogs.Confirm(
                    $"游戏“{game.Name}”{DescribeModsRootProblem(game.ModsRootPath)}。\n\n"
                    + "Mod 列表、导入和启用/禁用功能将不可用。是否现在修改游戏配置？",
                    "游戏配置不完整");
                if (confirmed) EditGame(game);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool ConfirmModsRootConfiguration(string gameName, string? modsRootPath, string operation)
        {
            if (!string.IsNullOrWhiteSpace(modsRootPath) && Directory.Exists(modsRootPath)) return true;

            return _dialogs.Confirm(
                $"游戏“{gameName}”{DescribeModsRootProblem(modsRootPath)}。\n\n"
                + "可以先保存游戏信息，但 Mod 列表、导入和启用/禁用功能将不可用。是否仍要保存？",
                operation);
        }

        private bool EnsureValidModsRoot(Game? game, string operation)
        {
            if (HasValidModsRoot(game)) return true;

            _dialogs.ShowWarning(
                $"无法{operation}：游戏“{game?.Name ?? "当前游戏"}”{DescribeModsRootProblem(game?.ModsRootPath)}。\n\n"
                + "请在左侧游戏图标栏中使用右键菜单的修改功能设置有效的 Mod 根目录。",
                "游戏配置不完整");
            return false;
        }

        // =========================================================
        // Add / Edit / Delete Game
        // =========================================================
        public void AddGame()
        {
            var dialog = new GameDialog { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true) return;

            var id = dialog.GameId.Trim();
            var name = dialog.GameName.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _dialogs.ShowWarning("游戏 ID 和名称不能为空，游戏 ID 不能包含文件名非法字符。", "增加游戏");
                return;
            }
            if (Games.Any(game => string.Equals(game.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogs.ShowWarning("该游戏 ID 已存在。", "增加游戏");
                return;
            }
            if (!ConfirmModsRootConfiguration(name, dialog.ModsRootPath, "增加游戏")) return;

            var game = new Game
            {
                Id = id,
                Name = name,
                CharacterInfoPath = AppDataPaths.GetDefaultCharacterInfoPath(),
                CharacterPicPath = AppDataPaths.GetDefaultCharacterPicPath(),
                GameIconPath = string.Empty,
                ModsRootPath = dialog.ModsRootPath.Trim(),
                D3dxUserIniPath = dialog.D3dxUserIniPath.Trim()
            };
            try
            {
                _gameService.EnsureDataDirectories(game);
                if (!_gameService.SaveIcon(game, dialog.GameIconPath)) return;
                if (!File.Exists(_characterService.GetInfoPath(game))
                    && !_characterService.SaveInfoFile(game, Array.Empty<CharacterInfo>())) return;
                if (!_gameService.SaveConfiguration(game)) return;
            }
            catch (Exception ex)
            {
                _dialogs.ShowError($"创建游戏数据失败：{ex.Message}", "增加游戏");
                return;
            }

            _gameService.EnsureIconPath(game);
            MarkModsRootWarningShown(game);
            var addGameIndex = Games.IndexOf(_addGamePlaceholder);
            if (addGameIndex >= 0) Games.Insert(addGameIndex, game);
            else Games.Add(game);

            SelectedGame = game;
            SaveState();
            if (!string.IsNullOrWhiteSpace(game.ModsRootPath) && Directory.Exists(game.ModsRootPath))
                LoadFromModsRoot(game.ModsRootPath);
        }

        public void EditGame(Game game)
        {
            if (game == null || game.IsAddGamePlaceholder) return;

            var dialog = new GameDialog(game) { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true) return;

            var id = dialog.GameId.Trim();
            var name = dialog.GameName.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _dialogs.ShowWarning("游戏 ID 和名称不能为空，游戏 ID 不能包含文件名非法字符。", "修改游戏");
                return;
            }
            if (Games.Any(item => !item.IsAddGamePlaceholder && item != game
                && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogs.ShowWarning("该游戏 ID 已存在。", "修改游戏");
                return;
            }
            if (!ConfirmModsRootConfiguration(name, dialog.ModsRootPath, "修改游戏")) return;

            var oldKey = StateStore.GetGameKey(game);
            var newKey = id;
            var isSelected = ReferenceEquals(game, SelectedGame);
            if (isSelected) SaveCurrentCharactersToCache();

            var oldGameDirectory = AppDataPaths.GetGameDirectory(oldKey);
            var newGameDirectory = AppDataPaths.GetGameDirectory(newKey);
            var movedGameDirectory = false;
            if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(newGameDirectory))
            {
                _dialogs.ShowWarning("目标游戏 ID 的数据目录已经存在。", "修改游戏");
                return;
            }

            var iconSourcePath = dialog.GameIconPath?.Trim().Trim('"') ?? string.Empty;
            _charactersByGame.TryGetValue(oldKey, out var cachedCharacters);
            if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
            {
                _charactersByGame.Remove(oldKey);
                if (cachedCharacters != null) _charactersByGame[newKey] = cachedCharacters;
            }

            try
            {
                if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(oldGameDirectory))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newGameDirectory) ?? AppDataPaths.GamesDirectory);
                    Directory.Move(oldGameDirectory, newGameDirectory);
                    movedGameDirectory = true;
                }

                if (movedGameDirectory && Path.IsPathRooted(iconSourcePath)
                    && FileSystemHelper.IsUnder(iconSourcePath, oldGameDirectory))
                {
                    iconSourcePath = FileSystemHelper.RepathAfterMove(iconSourcePath, oldGameDirectory, newGameDirectory);
                }

                game.Id = id;
                game.Name = name;
                game.ModsRootPath = dialog.ModsRootPath.Trim();
                game.CharacterInfoPath = string.IsNullOrWhiteSpace(game.CharacterInfoPath)
                    ? AppDataPaths.GetDefaultCharacterInfoPath()
                    : game.CharacterInfoPath;
                game.CharacterPicPath = string.IsNullOrWhiteSpace(game.CharacterPicPath)
                    ? AppDataPaths.GetDefaultCharacterPicPath()
                    : game.CharacterPicPath;
                game.D3dxUserIniPath = dialog.D3dxUserIniPath.Trim();
                _gameService.EnsureDataDirectories(game);
                if (!_gameService.SaveIcon(game, iconSourcePath)) throw new IOException("游戏图标保存失败。");
                _gameService.EnsureIconPath(game);
            }
            catch (Exception ex)
            {
                if (movedGameDirectory && Directory.Exists(newGameDirectory))
                {
                    try { Directory.Move(newGameDirectory, oldGameDirectory); }
                    catch (Exception rollbackEx) { Debug.WriteLine($"[Game] Rollback failed: {rollbackEx}"); }
                }
                _dialogs.ShowError($"修改游戏失败：{ex.Message}", "修改游戏");
                return;
            }
            MarkModsRootWarningShown(game);

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

        public void DeleteGame(Game game)
        {
            if (game == null || game.IsAddGamePlaceholder) return;

            if (Games.Count(item => !item.IsAddGamePlaceholder) <= 1)
            {
                _dialogs.ShowInformation("至少需要保留一个游戏。", "删除游戏");
                return;
            }

            if (!_dialogs.Confirm(
                $"确定删除游戏“{game.Name}”的信息吗？\n不会删除磁盘上的 Mods 文件。",
                "删除游戏")) return;

            var isSelected = ReferenceEquals(game, SelectedGame);
            if (isSelected) SaveCurrentCharactersToCache();
            var gameKey = StateStore.GetGameKey(game);
            _charactersByGame.Remove(gameKey);
            _gimiPersistService.RemoveGamePersistState(game);

            var gameDirectory = AppDataPaths.GetGameDirectory(gameKey);
            if (Directory.Exists(gameDirectory)) Directory.Delete(gameDirectory, recursive: true);

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

        // =========================================================
        // Characters
        // =========================================================
        private List<Character> GetCachedCharacters(string gameKey) =>
            _charactersByGame.TryGetValue(gameKey, out var characters) ? characters : new List<Character>();

        private void SaveCurrentCharactersToCache()
        {
            if (_selectedGame == null) return;
            _charactersByGame[StateStore.GetGameKey(_selectedGame)] = Characters
                .Where(character => !character.IsAddPlaceholder)
                .ToList();
        }

        public void LoadCharactersForGame(Game? game)
        {
            Characters.Clear();
            SelectedCharacter = null;
            if (game == null)
            {
                // 没有游戏时也要把可见列表同步清空，避免残留上一款游戏的角色
                RefreshCharactersView();
                return;
            }

            var cached = GetCachedCharacters(StateStore.GetGameKey(game));
            var infos = _characterService.ReadInfoFile(game);
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
                character.IconPath = _characterService.FindIcon(game, character.Name);
                characters.Add(character);
            }

            // 把旧状态中尚未写入角色信息文件的角色迁移进去，避免升级后丢失
            foreach (var saved in cached)
            {
                if (characters.Any(character => string.Equals(character.Name, saved.Name, StringComparison.OrdinalIgnoreCase))) continue;
                saved.GameId = game.Id;
                saved.IconPath = _characterService.FindIcon(game, saved.Name);
                characters.Add(saved);
                infos.Add(new CharacterInfo { Id = saved.Id, Name = saved.Name });
                infoChanged = true;
            }
            if (infoChanged) _characterService.SaveInfoFile(game, infos, showError: false);

            foreach (var character in characters) Characters.Add(character);
            Characters.Add(_addPlaceholder);
            SelectedCharacter = Characters.FirstOrDefault(character => !character.IsAddPlaceholder);
            RefreshCharactersView();
        }

        public void AddCharacter()
        {
            if (SelectedGame == null)
            {
                _dialogs.ShowWarning("请先选择游戏。", "新增角色");
                return;
            }

            var dialog = new InputDialog("新增角色", "请输入角色名：") { Owner = Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true) return;

            var name = dialog.ResultText;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _dialogs.ShowWarning("角色名不能为空，也不能包含文件名非法字符。", "新增角色");
                return;
            }
            if (Characters.Any(character => !character.IsAddPlaceholder
                && string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogs.ShowWarning("该角色已经存在。", "新增角色");
                return;
            }

            var character = new Character
            {
                Id = Guid.NewGuid().ToString(),
                GameId = SelectedGame.Id,
                Name = name,
                IconPath = _characterService.FindIcon(SelectedGame, name)
            };
            var infos = Characters
                .Where(item => !item.IsAddPlaceholder)
                .Select(item => new CharacterInfo { Id = item.Id, Name = item.Name })
                .Append(new CharacterInfo { Id = character.Id, Name = character.Name });
            if (!_characterService.SaveInfoFile(SelectedGame, infos)) return;

            Characters.Remove(_addPlaceholder);
            Characters.Add(character);
            Characters.Add(_addPlaceholder);
            SaveCurrentCharactersToCache();
            RefreshCharactersView();
            SelectedCharacter = character;
            SaveState();
        }

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
                character.IconPath = _characterService.SetIconFromFile(SelectedGame, character, dialog.FileName);
                SaveState();
            }
            catch (Exception ex)
            {
                _dialogs.ShowError($"添加头像失败：{ex.Message}", "角色头像");
            }
        }

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
                _dialogs.ShowWarning("角色名不能为空，也不能包含文件名非法字符。", "修改角色名");
                return;
            }
            if (string.Equals(character.Name, requestedName, StringComparison.OrdinalIgnoreCase)) return;
            if (Characters.Any(item => !item.IsAddPlaceholder && item != character
                && string.Equals(item.Name, requestedName, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogs.ShowWarning("该角色名已经存在。", "修改角色名");
                return;
            }

            var oldName = character.Name;
            var modsRoot = SelectedGame.ModsRootPath;
            var oldCharacterDir = string.IsNullOrWhiteSpace(modsRoot) ? null : Path.Combine(modsRoot, oldName);
            var newCharacterDir = string.IsNullOrWhiteSpace(modsRoot) ? null : Path.Combine(modsRoot, requestedName);
            var oldIcon = _characterService.FindIcon(SelectedGame, oldName);
            var iconDirectory = _characterService.GetIconDirectory(SelectedGame);
            var newIcon = string.IsNullOrWhiteSpace(oldIcon) || string.IsNullOrWhiteSpace(iconDirectory)
                ? null
                : Path.Combine(iconDirectory, requestedName + Path.GetExtension(oldIcon));

            if (!string.IsNullOrWhiteSpace(newCharacterDir) && FileSystemHelper.EntryExists(newCharacterDir))
            {
                _dialogs.ShowWarning("目标角色的 Mod 文件夹已经存在。", "修改角色名");
                return;
            }
            if (!string.IsNullOrWhiteSpace(newIcon) && File.Exists(newIcon))
            {
                _dialogs.ShowWarning("目标角色的头像文件已经存在。", "修改角色名");
                return;
            }

            var movedCharacterDir = false;
            var movedIcon = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(oldCharacterDir) && Directory.Exists(oldCharacterDir))
                {
                    Directory.Move(oldCharacterDir, newCharacterDir!);
                    movedCharacterDir = true;
                }
                if (!string.IsNullOrWhiteSpace(oldIcon) && !string.IsNullOrWhiteSpace(newIcon)
                    && !string.Equals(oldIcon, newIcon, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(oldIcon, newIcon);
                    movedIcon = true;
                }

                var infos = _characterService.ReadInfoFile(SelectedGame);
                var info = infos.FirstOrDefault(item =>
                    string.Equals(item.Id, character.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (info == null)
                {
                    infos.Add(new CharacterInfo { Id = character.Id, Name = requestedName });
                }
                else
                {
                    info.Name = requestedName;
                }
                if (!_characterService.SaveInfoFile(SelectedGame, infos)) throw new IOException("角色信息文件保存失败。");

                if (movedCharacterDir)
                {
                    RepathCharacterMods(character, oldCharacterDir!, newCharacterDir!);
                }

                character.Name = requestedName;
                character.IconPath = newIcon;
                SaveCurrentCharactersToCache();
                RefreshCharactersView();
                SaveState();
            }
            catch (Exception ex)
            {
                if (movedIcon && File.Exists(newIcon))
                {
                    try { File.Move(newIcon!, oldIcon!); }
                    catch (Exception rollbackEx) { Debug.WriteLine($"[Character] Icon rollback failed: {rollbackEx}"); }
                }
                if (movedCharacterDir && Directory.Exists(newCharacterDir))
                {
                    try { Directory.Move(newCharacterDir!, oldCharacterDir!); }
                    catch (Exception rollbackEx) { Debug.WriteLine($"[Character] Directory rollback failed: {rollbackEx}"); }
                }
                _dialogs.ShowError($"修改角色名失败：{ex.Message}", "修改角色名");
            }
        }

        /// <summary>
        /// 隐藏 / 取消隐藏角色。隐藏只是显示层的开关：不删除角色信息、
        /// 不移动 Mod 目录、不影响 persist 状态，随时可在“全部列表”里取消隐藏。
        /// </summary>
        public void ToggleCharacterHidden(Character character)
        {
            if (character == null || character.IsAddPlaceholder) return;

            character.IsHidden = !character.IsHidden;

            // 隐藏后该角色会立刻从当前列表消失，若它正被选中则把选中项让给下一个可见角色
            if (character.IsHidden && ReferenceEquals(SelectedCharacter, character))
            {
                SelectedCharacter = Characters.FirstOrDefault(item =>
                    !item.IsAddPlaceholder && !item.IsHidden);
            }

            RefreshCharactersView();
            SaveState();
        }

        /// <summary>角色目录改名后，同步其 Mod、INI、预览图与 Persist 状态中的路径。</summary>
        private void RepathCharacterMods(Character character, string oldCharacterDir, string newCharacterDir)
        {
            foreach (var mod in character.Mods)
            {
                var oldModPath = mod.FilePath;
                if (FileSystemHelper.IsUnder(mod.FilePath, oldCharacterDir))
                {
                    mod.FilePath = FileSystemHelper.RepathAfterMove(mod.FilePath!, oldCharacterDir, newCharacterDir);
                }
                // 预览图与 Mod 同级或位于 Mod 目录内，统一按移动后的根路径换算
                PreviewService.UpdatePathsAfterMove(mod, oldCharacterDir, newCharacterDir, movedDirectory: true);
                foreach (var ini in mod.IniFiles)
                {
                    if (FileSystemHelper.IsUnder(ini.FilePath, oldCharacterDir))
                        ini.FilePath = FileSystemHelper.RepathAfterMove(ini.FilePath, oldCharacterDir, newCharacterDir);
                }
                if (!string.IsNullOrWhiteSpace(oldModPath)
                    && !string.Equals(oldModPath, mod.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (_sourcesByModPath.TryGetValue(oldModPath, out var source))
                    {
                        _sourcesByModPath.Remove(oldModPath);
                        _sourcesByModPath[mod.FilePath!] = source;
                    }
                    _gimiPersistService.MovePersistState(SelectedGame!, oldModPath, mod.FilePath!);
                }
            }
        }

        private bool CharacterFilter(object o)
        {
            if (o is not Character character) return true;
            // 占位项始终放行，保证“新增角色”按钮不被任何筛选模式过滤掉
            if (character.IsAddPlaceholder) return true;

            // 全部列表：隐藏角色也显示（界面会做淡化标识）
            if (_listMode == CharacterListMode.All) return true;

            // 隐藏的角色在其余模式下都不出现，也不受“有 Mod”模式影响
            if (character.IsHidden) return false;

            // 有 Mod 列表：Mods 集合由扫描结果填充，未配置 Mod 根目录时天然为空
            if (_listMode == CharacterListMode.WithMods)
                return character.Mods != null && character.Mods.Count > 0;

            return true;
        }

        // =========================================================
        // Character Info Sync
        // =========================================================
        private async Task SyncCharacterInfoAsync()
        {
            if (!CanSyncCharacterInfo) return;

            var game = SelectedGame!;
            IsSyncingCharacterInfo = true;
            try
            {
                var remoteCharacters = await _characterInfoSyncService.FetchCharacterInfosAsync(game);
                var currentInfos = _characterService.ReadInfoFile(game);
                var existingNames = new HashSet<string>(
                    currentInfos.Select(info => info.Name.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                var addedCount = 0;

                var iconDirectory = _characterService.GetIconDirectory(game);
                var avatarFailureCount = 0;
                foreach (var remoteCharacter in remoteCharacters)
                {
                    if (string.IsNullOrWhiteSpace(remoteCharacter.ImageUrl))
                    {
                        avatarFailureCount++;
                        if (!existingNames.Add(remoteCharacter.Name)) continue;
                    }
                    else if (!existingNames.Add(remoteCharacter.Name))
                    {
                        if (_characterService.FindIcon(game, remoteCharacter.Name) != null) continue;
                    }

                    if (!string.IsNullOrWhiteSpace(remoteCharacter.ImageUrl))
                    {
                        try
                        {
                            await _characterInfoSyncService.DownloadAvatarAsync(
                                remoteCharacter.ImageUrl,
                                Path.Combine(iconDirectory, remoteCharacter.Name + ".png"));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Sync] Avatar download failed for '{remoteCharacter.Name}': {ex}");
                            avatarFailureCount++;
                        }
                    }

                    if (currentInfos.All(info => !string.Equals(info.Name, remoteCharacter.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        currentInfos.Add(new CharacterInfo { Id = Guid.NewGuid().ToString(), Name = remoteCharacter.Name });
                        addedCount++;
                    }
                }

                if (addedCount > 0 && !_characterService.SaveInfoFile(game, currentInfos)) return;

                if (ReferenceEquals(SelectedGame, game))
                {
                    LoadCharactersForGame(game);
                    SaveState();
                }

                _dialogs.ShowInformation(
                    $"同步完成。\n图鉴角色：{remoteCharacters.Count} 个\n新增角色：{addedCount} 个\n"
                    + (avatarFailureCount == 0 ? "头像已保存。" : $"头像下载失败：{avatarFailureCount} 个。"),
                    "同步角色");
            }
            catch (TaskCanceledException)
            {
                _dialogs.ShowWarning("同步超时，请检查网络连接后重试。", "同步角色");
            }
            catch (Exception ex)
            {
                _dialogs.ShowError($"同步角色失败：{ex.Message}", "同步角色");
            }
            finally
            {
                IsSyncingCharacterInfo = false;
            }
        }

        // =========================================================
        // Mods Root
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
            catch (Exception ex)
            {
                Debug.WriteLine($"[Mods] Open root failed: {ex}");
            }
        }

        private void RefreshMods()
        {
            if (!EnsureValidModsRoot(SelectedGame, "刷新 Mod 列表")) return;
            LoadFromModsRoot(SelectedGame!.ModsRootPath!);
        }

        private void OpenModFolder(object? parameter)
        {
            var path = SelectedMod?.FilePath;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (File.Exists(path)) path = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }

        public void LoadFromModsRoot(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;
            if (SelectedGame != null) SelectedGame.ModsRootPath = rootPath;
            OnPropertyChanged(nameof(ModsRootPath));
            foreach (var character in Characters) character.Mods.Clear();

            _modService.ScanInto(rootPath, Characters, ApplySavedSource);

            SelectedCharacter ??= Characters.FirstOrDefault();
            SelectedMod = SelectedCharacter?.Mods.FirstOrDefault(m => m.Enabled) ?? SelectedCharacter?.Mods.FirstOrDefault();
            SaveState();
            RefreshCharactersView();
        }

        // =========================================================
        // Toggle / Delete / Rename Mod
        // =========================================================
        private void ToggleMod(Mod mod)
        {
            if (mod == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "切换 Mod 状态")) return;

            try
            {
                // 启用 -> 禁用：先保存当前 Persist，再改名禁用
                if (mod.Enabled)
                {
                    Debug.WriteLine($"[GIMI Persist] Toggle OFF: saving {mod.Name}");
                    var persistResult = _gimiPersistService.SaveCurrentPersist(SelectedGame!, mod);
                    if (persistResult == PersistSnapshotSaveResult.UserIniUnavailable
                        && !ConfirmDisableWithoutPersistSnapshot(mod))
                    {
                        return;
                    }
                }
                // 禁用 -> 启用：先在 DISABLED_ 路径下恢复历史值，再启用
                else
                {
                    Debug.WriteLine($"[GIMI Persist] Toggle ON: restoring {mod.Name}");
                    _gimiPersistService.RestorePersist(SelectedGame!, mod);
                }

                _modService.ToggleEnabled(mod);
                SaveState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GIMI Persist] Toggle failed: {ex}");
            }
        }

        private bool ConfirmDisableWithoutPersistSnapshot(Mod mod) =>
            _dialogs.Confirm(
                $"未找到游戏“{SelectedGame?.Name}”的 d3dx_user.ini，无法保存 Mod“{mod.Name}”当前的 Persist 状态。\n\n"
                + "已有快照会保留，但本次运行时状态可能丢失。是否仍要禁用该 Mod？",
                "Persist 配置缺失");

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
                _dialogs.ShowWarning("Mod 名称不能为空，也不能包含文件名非法字符。", "修改 Mod 名称");
                return;
            }

            var oldPath = mod.FilePath;
            try
            {
                var newPath = _modService.Rename(mod, requestedName!);
                _gimiPersistService.MovePersistState(SelectedGame!, oldPath!, newPath);
                _sourcesByModPath.Remove(oldPath!);
                _sourcesByModPath[newPath] = mod.Source;
                SaveState();
            }
            catch (Exception ex)
            {
                mod.Name = originalName;
                _dialogs.ShowError($"修改 Mod 名称失败：{ex.Message}", "修改 Mod 名称");
            }
        }

        private void DeleteMod()
        {
            var mod = SelectedMod;
            var character = SelectedCharacter;
            if (mod == null || character == null || string.IsNullOrWhiteSpace(mod.FilePath)) return;
            if (!EnsureValidModsRoot(SelectedGame, "删除 Mod")) return;

            if (!_dialogs.Confirm(
                $"确定要删除 Mod“{mod.Name}”吗？此操作会删除磁盘上的文件，无法撤销。",
                "删除 Mod")) return;

            try
            {
                _modService.DeleteFiles(mod);
                _gimiPersistService.RemovePersistState(SelectedGame!, mod);
                _sourcesByModPath.Remove(mod.FilePath);
                character.Mods.Remove(mod);
                SelectedMod = character.Mods.FirstOrDefault(m => m.Enabled) ?? character.Mods.FirstOrDefault();
                SaveState();
            }
            catch (Exception ex)
            {
                _dialogs.ShowError($"删除 Mod 失败：{ex.Message}", "删除 Mod");
            }
        }

        public void ImportFiles(string[] paths, Character target)
        {
            if (paths == null || target == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "导入 Mod")) return;

            var imported = _modService.Import(paths, target, SelectedGame!.ModsRootPath!, ConfirmOverwrite);
            foreach (var mod in imported)
            {
                ApplySavedSource(mod);
                target.Mods.Add(mod);
            }
            SaveState();
        }

        private bool ConfirmOverwrite(string destination)
        {
            if (!FileSystemHelper.EntryExists(destination)) return true;
            var name = Path.GetFileName(destination);
            if (!_dialogs.Confirm($"“{name}”已存在。是否覆盖？", "导入 Mod")) return false;
            FileSystemHelper.DeleteEntry(destination);
            return true;
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
        // Previews
        // =========================================================
        private void AddPreviewsFromFiles()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "添加 Mod 预览图")) return;
            var folder = _previewService.GetModFolder(mod);
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

            var addedCount = 0;
            foreach (var sourcePath in dialog.FileNames)
            {
                try
                {
                    var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                    if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") continue;
                    var newPath = _previewService.GetNextPreviewPath(mod, extension);
                    if (string.IsNullOrWhiteSpace(newPath)) continue;
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
                _dialogs.ShowWarning("没有成功添加预览图片。", "添加预览");
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
            if (string.IsNullOrEmpty(_previewService.GetModFolder(mod))) return;
            if (!Clipboard.ContainsImage())
            {
                _dialogs.ShowInformation("剪贴板中没有图片", "添加预览");
                return;
            }

            var bitmap = Clipboard.GetImage();
            if (bitmap == null) return;
            var newPath = _previewService.GetNextPreviewPath(mod, ".png");
            if (string.IsNullOrWhiteSpace(newPath)) return;
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(newPath);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preview] Failed to save clipboard image: {ex}");
                _dialogs.ShowError("保存预览图片失败", "错误");
                return;
            }
            mod.PreviewPaths.Add(newPath);
            mod.CurrentPreviewIndex = mod.PreviewPaths.Count - 1;
            SaveState();
        }

        private void DeletePreview()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            if (!EnsureValidModsRoot(SelectedGame, "删除 Mod 预览图")) return;
            var current = mod.CurrentPreviewPath;
            if (string.IsNullOrEmpty(current)) return;

            FileSystemHelper.DeleteEntry(current);
            if (mod.PreviewPaths.Count > 0)
            {
                var index = mod.CurrentPreviewIndex;
                if (index >= 0 && index < mod.PreviewPaths.Count) mod.PreviewPaths.RemoveAt(index);
                mod.CurrentPreviewIndex = Math.Min(mod.CurrentPreviewIndex, mod.PreviewPaths.Count - 1);
            }
            SaveState();
        }

        private void PrevPreview()
        {
            var mod = SelectedMod;
            if (mod?.PreviewPaths == null || mod.PreviewPaths.Count <= 1) return;
            var count = mod.PreviewPaths.Count;
            mod.CurrentPreviewIndex = (mod.CurrentPreviewIndex - 1 + count) % count;
        }

        private void NextPreview()
        {
            var mod = SelectedMod;
            if (mod?.PreviewPaths == null || mod.PreviewPaths.Count <= 1) return;
            var count = mod.PreviewPaths.Count;
            mod.CurrentPreviewIndex = (mod.CurrentPreviewIndex + 1) % count;
        }

        // =========================================================
        // INI
        // =========================================================
        private void LoadIniData()
        {
            var mod = SelectedMod;
            if (mod == null) return;

            try
            {
                if (_iniService.Populate(mod, _previewService.GetModFolder(mod))) SaveState();
            }
            catch (Exception ex)
            {
                _dialogs.ShowError($"读取 INI 失败：{ex.Message}", "读取 INI");
            }
        }

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
                _dialogs.ShowInformation("当前 Mod 中未找到 INI 文件。", "打开 INI");
                return;
            }
            try
            {
                mod.SelectedIniFile = ini;
                Process.Start(new ProcessStartInfo(ini.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _dialogs.ShowError($"打开 INI 失败：{ex.Message}", "打开 INI");
            }
        }

        // =========================================================
        // State
        // =========================================================
        private void LoadStateOrSample()
        {
            foreach (var game in Games)
            {
                var characters = _stateStore.Load(game);
                foreach (var character in characters)
                {
                    foreach (var mod in character.Mods)
                    {
                        if (!string.IsNullOrWhiteSpace(mod.FilePath))
                            _sourcesByModPath[mod.FilePath!] = mod.Source;
                    }
                }
                _charactersByGame[StateStore.GetGameKey(game)] = characters;
            }

            SelectedGame = Games.FirstOrDefault();
        }

        public void SaveState()
        {
            try
            {
                SaveCurrentCharactersToCache();
                foreach (var game in Games.Where(game => !game.IsAddGamePlaceholder))
                {
                    _gameService.SaveConfiguration(game);
                    var characters = _charactersByGame.TryGetValue(StateStore.GetGameKey(game), out var cached)
                        ? cached
                        : new List<Character>();
                    _stateStore.Save(game, characters);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[State] Save failed: {ex}");
            }
        }

        // =========================================================
        // PropertyChanged
        // =========================================================
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
