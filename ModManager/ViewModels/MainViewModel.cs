using ModManager.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using static System.Net.Mime.MediaTypeNames;


namespace ModManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private static readonly string StateDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ModManager");

        private static readonly string StateFile = Path.Combine(
            StateDirectory,
            "modstate.json");
        private readonly Dictionary<string, string?> _sourcesByModPath = new(StringComparer.OrdinalIgnoreCase);
        public ObservableCollection<Game> Games { get; } = new ObservableCollection<Game>();
        public ObservableCollection<Character> Characters { get; } = new ObservableCollection<Character>();

        /// <summary>
        /// 当前选中游戏的 Mods 根目录路径（代理到 SelectedGame.ModsRootPath）。
        /// 切换游戏时自动触发 PropertyChanged 以刷新 UI 绑定。
        /// </summary>
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
                if (_selectedGame != value)
                {
                    _selectedGame = value;
                    OnPropertyChanged();
                    // 切换游戏时，刷新 ModsRootPath 绑定和角色列表
                    OnPropertyChanged(nameof(ModsRootPath));
                    OnPropertyChanged(nameof(ModsRootPathIsUserSet));
                    LoadCharactersForGame(_selectedGame);
                    // 如果新游戏之前已设置过 ModsRootPath，自动加载其 mods
                    var gamePath = _selectedGame?.ModsRootPath;
                    if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                    {
                        LoadFromModsRoot(gamePath);
                    }
                }
            }
        }

        private string _lastResolvedGamePath;
        // 用于调试/显示解析到的游戏路径
        public string LastResolvedGamePath
        {
            get => _lastResolvedGamePath;
            set { _lastResolvedGamePath = value; OnPropertyChanged(); }
        }

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

        private Mod _selectedMod;
        public Mod SelectedMod
        {
            get => _selectedMod;
            set
            {
                _selectedMod = value;
                OnPropertyChanged();
                ReadIniFile(showMissingMessage: false);
            }
        }
        private void OpenModFolder(object parameter)
        {
            if (SelectedMod == null)
                return;

            var path = SelectedMod.FilePath;

            if (string.IsNullOrWhiteSpace(path))
                return;


            if (File.Exists(path))
            {
                path = Path.GetDirectoryName(path);
            }


            if (!string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }

        public ICommand ToggleModCommand { get; }
        public ICommand ActivateModCommand { get; }
        // removed Apply/Clear commands (settings done via UI folder picker)
        public ICommand OpenModsRootCommand { get; }
        public ICommand RefreshModsCommand { get; }
        public ICommand ToggleShowOnlyWithModsCommand { get; }
        public ICommand AddPreviewCommand { get; }
        public ICommand DeletePreviewCommand { get; }
        public ICommand PrevPreviewCommand { get; }
        public ICommand NextPreviewCommand { get; }
        public ICommand DeleteModCommand { get; }
        public ICommand ReadIniCommand { get; }
        public ICommand OpenIniCommand { get; }
        public ICommand OpenModFolderCommand { get; }
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
            set { if (_showOnlyWithMods != value) { _showOnlyWithMods = value; OnPropertyChanged(); CharactersView?.Refresh(); } }
        }

        public ICollectionView CharactersView { get; private set; }

        /// <summary>
        /// 判断当前选中游戏是否已设置了 ModsRootPath。
        /// 直接根据 SelectedGame.ModsRootPath 是否有值来判断。
        /// </summary>
        public bool ModsRootPathIsUserSet
        {
            get => SelectedGame != null && !string.IsNullOrEmpty(SelectedGame.ModsRootPath);
            set { OnPropertyChanged(); }
        }
        // no test command

        public MainViewModel()
        {
            ToggleModCommand = new RelayCommand(
                p =>
                {
                    if (p is not Mod mod) return;
                    SelectedMod = mod;
                    ToggleMod(mod);
                },
                p => p is Mod);
            ActivateModCommand = new RelayCommand(p => ActivateMod(p as Mod), p => p is Mod);
            // commands for apply/clear removed; settings handled in MainWindow
            OpenModsRootCommand = new RelayCommand(p => OpenModsRoot(), p => !string.IsNullOrEmpty(ModsRootPath));
            RefreshModsCommand = new RelayCommand(p => RefreshMods(), p => !string.IsNullOrEmpty(ModsRootPath));
            ToggleShowOnlyWithModsCommand = new RelayCommand(p => { ShowOnlyWithMods = !ShowOnlyWithMods; }, p => true);
            AddPreviewCommand = new RelayCommand(p => AddPreview());
            DeletePreviewCommand = new RelayCommand(p => DeletePreview());
            PrevPreviewCommand = new RelayCommand(p => PrevPreview());
            NextPreviewCommand = new RelayCommand(p => NextPreview());
            DeleteModCommand = new RelayCommand(p => DeleteMod(), p => SelectedMod != null);
            ReadIniCommand = new RelayCommand(p => ReadIniFile(showMissingMessage: true), p => SelectedMod != null);
            OpenIniCommand = new RelayCommand(p => OpenIniFile(), p => SelectedMod != null);
            OpenModFolderCommand = new RelayCommand(OpenModFolder);

            // 初始化可选游戏列表，使用项目内 Resources 路径（相对于运行目录）
            var resourceRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "CharacterPic");

            Games.Add(new Game
            {
                Id = "GI",
                Name = "GI",
                Path = Path.Combine(resourceRoot, "GI")
            });

            Games.Add(new Game
            {
                Id = "WW",
                Name = "WW",
                Path = Path.Combine(resourceRoot, "WW")
            });

            LoadStateOrSample();

            // 初始化 CharactersView 并设置过滤
            CharactersView = CollectionViewSource.GetDefaultView(Characters);
            CharactersView.Filter = CharacterFilter;

            // 不再自动应用默认 ModsRootPath，改为仅使用用户自定义路径（持久化在 modstate.json）
        }

        private void RefreshMods()
        {
            var path = SelectedGame?.ModsRootPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            LoadFromModsRoot(path);
        }

        public void LoadCharacterIconsFromFolders(string[] folders)
        {
            if (folders == null) return;
            Characters.Clear();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var f in Directory.EnumerateFiles(folder, "*.png"))
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        var ch = new Character { Id = Guid.NewGuid().ToString(), Name = name, IconPath = f };
                        Characters.Add(ch);
                    }
                    catch { }
                }
            }

            SelectedCharacter = Characters.FirstOrDefault();
            SaveState();
        }

        public void LoadCharactersForGame(Game game)
        {
            if (game == null)
                return;

            var folder = game.Path;

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                Characters.Clear();
                SelectedCharacter = null;
                LastResolvedGamePath = folder;
                return;
            }

            LastResolvedGamePath = folder;
            LoadCharacterIconsFromFolders(new[] { folder });
            CharactersView?.Refresh();
        }



        private bool CharacterFilter(object o)
        {
            if (!(o is Models.Character c)) return true;
            if (!ShowOnlyWithMods) return true;
            // 如果当前游戏未设置 ModsRootPath 或目录不存在，则不进行过滤（显示所有角色）
            var path = SelectedGame?.ModsRootPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return true;
            return c.Mods != null && c.Mods.Count > 0;
        }

        // ApplyModsRoot and ClearModsRoot removed; use MainWindow folder picker to set ModsRootPath

        private void OpenModsRoot()
        {
            try
            {
                var path = SelectedGame?.ModsRootPath;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch { }
        }

        private void ToggleMod(Mod mod)
        {
            if (mod == null) return;
            try
            {
                var path = mod.FilePath;
                if (Directory.Exists(path))
                {
                    var parent = Path.GetDirectoryName(path);
                    var raw = Path.GetFileName(path);
                    string newName;
                    if (raw.StartsWith("DISABLED_"))
                        newName = raw.Substring("DISABLED_".Length);
                    else
                        newName = "DISABLED_" + raw;

                    var dest = Path.Combine(parent, newName);
                    if (Directory.Exists(dest) || File.Exists(dest))
                    {
                        // avoid collision
                        dest = Path.Combine(parent, newName + "_" + Guid.NewGuid().ToString("N"));
                    }
                    Directory.Move(path, dest);
                    UpdatePreviewPathsAfterMove(mod, path, dest, movedDirectory: true);
                    mod.FilePath = dest;
                    var display = Path.GetFileName(dest);
                    if (display.StartsWith("DISABLED_")) display = display.Substring("DISABLED_".Length);
                    mod.Name = display;
                    mod.Enabled = !Path.GetFileName(dest).StartsWith("DISABLED_");
                }
                else if (File.Exists(path))
                {
                    var parent = Path.GetDirectoryName(path);
                    var raw = Path.GetFileName(path);
                    string newName;
                    if (raw.StartsWith("DISABLED_"))
                        newName = raw.Substring("DISABLED_".Length);
                    else
                        newName = "DISABLED_" + raw;

                    var dest = Path.Combine(parent, newName);
                    if (File.Exists(dest) || Directory.Exists(dest))
                    {
                        dest = Path.Combine(parent, newName + "_" + Guid.NewGuid().ToString("N"));
                    }
                    File.Move(path, dest);
                    UpdatePreviewPathsAfterMove(mod, path, dest, movedDirectory: false);
                    mod.FilePath = dest;
                    var display = Path.GetFileName(dest);
                    if (display.StartsWith("DISABLED_")) display = display.Substring("DISABLED_".Length);
                    mod.Name = display;
                    mod.Enabled = !Path.GetFileName(dest).StartsWith("DISABLED_");
                }
                SaveState();
            }
            catch { }
        }

        private void ActivateMod(Mod mod)
        {
            if (mod == null || SelectedCharacter == null)
                return;

            foreach (var item in SelectedCharacter.Mods.Where(x => x.Enabled && x != mod).ToList())
                ToggleMod(item); // 实际改名为 DISABLED_ 前缀

            if (!mod.Enabled)
                ToggleMod(mod);  // 实际去除 DISABLED_ 前缀

            SelectedMod = mod;
            SaveState();
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
                        foreach (var savedMod in doc.Characters?
                                     .Where(character => character.Mods != null)
                                     .SelectMany(character => character.Mods)
                                     .Where(mod => !string.IsNullOrWhiteSpace(mod.FilePath)))
                        {
                            _sourcesByModPath[savedMod.FilePath] = savedMod.Source;
                        }

                        Games.Clear();
                        foreach (var g in doc.Games) Games.Add(g);
                        // 保持现有 Characters 不被持久化数据覆盖（角色来源于游戏资源）
                        Characters.Clear();
                        foreach (var c in doc.Characters) Characters.Add(c);
                        // 每个 Game 的 ModsRootPath 已随 Games 数组恢复，无需额外处理
                        // SelectedGame setter 会自动加载对应游戏的 mods
                        SelectedGame = Games.FirstOrDefault();
                        SelectedCharacter = Characters.FirstOrDefault();
                        SelectedMod = SelectedCharacter?.Mods.FirstOrDefault(m => m.Enabled) ?? SelectedCharacter?.Mods.FirstOrDefault();
                        return;
                    }
                }
                catch { /* ignore and fall back to sample */ }
            }

            // fallback sample data
            var game = new Game { Id = "g1", Name = "示例游戏", Path = "C:\\Games\\Example" };
            Games.Add(game);

            var character = new Character { Id = "c1", Name = "示例角色" };
            character.Mods.Add(new Mod { Id = "m1", Name = "Mod A", Enabled = true });
            character.Mods.Add(new Mod { Id = "m2", Name = "Mod B", Enabled = false });
            Characters.Add(character);

            SelectedGame = game;
            SelectedCharacter = character;
            SelectedMod = character.Mods.FirstOrDefault();

            SaveState();
        }

        public void LoadFromModsRoot(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;
            // 将路径存入当前选中游戏
            if (SelectedGame != null)
                SelectedGame.ModsRootPath = rootPath;
            OnPropertyChanged(nameof(ModsRootPath));

            // 不修改 Characters 列表（角色来源于所选游戏的头像资源）。
            // 先清空每个角色的 Mod 列表，然后按照 mods 根目录下的子文件夹匹配角色名来填充对应角色的 Mods。
            foreach (var ch in Characters)
            {
                ch.Mods.Clear();
            }

            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                var folderName = Path.GetFileName(dir);
                var character = Characters.FirstOrDefault(c => string.Equals(c.Name, folderName, StringComparison.OrdinalIgnoreCase));
                if (character == null)
                {
                    // 如果没有对应角色，则跳过（按要求视为无 mod）
                    continue;
                }

                // 扫描该角色文件夹下的 mod：目录或文件都视为一个 mod
                foreach (var modDir in Directory.GetDirectories(dir))
                {
                    var modName = Path.GetFileName(modDir);
                    var raw = modName;
                    var enabled = !raw.StartsWith("DISABLED_");
                    var displayName = enabled ? raw : raw.Substring("DISABLED_".Length);
                    var mod = new Mod
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = displayName,
                        FilePath = modDir,
                        Enabled = enabled
                    };
                    foreach (var p in FindPreviewsInDirectory(modDir))
                        mod.PreviewPaths.Add(p);
                    ApplySavedSource(mod);
                    character.Mods.Add(mod);
                }

                foreach (var file in Directory.GetFiles(dir))
                {
                    var modName = Path.GetFileName(file);
                    var info = new FileInfo(file);
                    var raw = modName;
                    var enabled = !raw.StartsWith("DISABLED_");
                    var displayName = enabled ? raw : raw.Substring("DISABLED_".Length);

                    string preview = null;
                    var imgExts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(imgExts, ext) >= 0)
                        preview = file;

                    var mod = new Mod
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = displayName,
                        FilePath = file,
                        Size = info.Length,
                        Enabled = enabled
                    };
                    if (!string.IsNullOrEmpty(preview))
                        mod.PreviewPaths.Add(preview);
                    ApplySavedSource(mod);
                    character.Mods.Add(mod);
                }
            }

            // 保持当前选中角色不变（如果之前为空则设为第一项）
            SelectedCharacter = SelectedCharacter ?? Characters.FirstOrDefault();
            SelectedMod = SelectedCharacter?.Mods.FirstOrDefault(m => m.Enabled) ?? SelectedCharacter?.Mods.FirstOrDefault();
            SaveState();
            // 刷新 CharactersView 以确保 UI 及时更新过滤和徽章
            CharactersView?.Refresh();
        }

        // 查找目录下的预览图列表：优先 preview_ 前缀，无则返回所有图片
        private List<string> FindPreviewsInDirectory(string dir)
        {
            var result = new List<string>();
            if (!Directory.Exists(dir)) return result;
            var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

            // 递归搜索当前 Mod 目录以及所有子文件夹
            var allFiles = Directory.EnumerateFiles(
                    dir,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(f =>
                    Array.IndexOf(
                        exts,
                        Path.GetExtension(f).ToLowerInvariant()) >= 0)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 第一优先级：
            // 所有目录中名字以 preview_ 开头的图片
            var previewFiles = allFiles
                .Where(f =>
                    Path.GetFileNameWithoutExtension(f)
                        .StartsWith(
                            "preview_",
                            StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (previewFiles.Count > 0)
            {
                result.AddRange(previewFiles);
            }
            else
            {
                // 如果没有 preview_ 图片，
                // 则使用所有子目录中的图片
                result.AddRange(allFiles);
            }

            return result;
        }

        // 获取 mod 所在的文件夹路径（FilePath 是目录则用目录本身，是文件则用其父目录）
        private string GetModFolder(Mod mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.FilePath)) return null;
            if (Directory.Exists(mod.FilePath)) return mod.FilePath;
            return Path.GetDirectoryName(mod.FilePath);
        }

        // 生成下一个 preview_x.png 路径（x = 现有最大序号 + 1）
        private string GetNextPreviewPath(string folder)
        {
            int max = 0;
            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.EnumerateFiles(folder, "preview_*"))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    var numStr = name.Substring("preview_".Length);
                    if (int.TryParse(numStr, out var n) && n > max)
                        max = n;
                }
            }
            return Path.Combine(folder, $"preview_{max + 1}.png");
        }

        private void AddPreview()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            var folder = GetModFolder(mod);
            if (string.IsNullOrEmpty(folder)) return;

            if (!Clipboard.ContainsImage())
            {
                MessageBox.Show("剪贴板中没有图片", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var bmp = Clipboard.GetImage();
            if (bmp == null) return;

            var newPath = GetNextPreviewPath(folder);
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(newPath))
                    encoder.Save(fs);
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

        private void DeletePreview()
        {
            var mod = SelectedMod;
            if (mod == null) return;
            var current = mod.CurrentPreviewPath;
            if (string.IsNullOrEmpty(current)) return;

            try { if (File.Exists(current)) File.Delete(current); } catch { }

            if (mod.PreviewPaths != null && mod.PreviewPaths.Count > 0)
            {
                int idx = mod.CurrentPreviewIndex;
                if (idx >= 0 && idx < mod.PreviewPaths.Count)
                    mod.PreviewPaths.RemoveAt(idx);
                mod.CurrentPreviewIndex = Math.Min(mod.CurrentPreviewIndex, mod.PreviewPaths.Count - 1);
            }
            SaveState();
        }

        private void PrevPreview()
        {
            var mod = SelectedMod;
            if (mod == null || mod.PreviewPaths == null || mod.PreviewPaths.Count <= 1) return;
            int n = mod.PreviewPaths.Count;
            mod.CurrentPreviewIndex = (mod.CurrentPreviewIndex - 1 + n) % n;
        }

        private void NextPreview()
        {
            var mod = SelectedMod;
            if (mod == null || mod.PreviewPaths == null || mod.PreviewPaths.Count <= 1) return;
            int n = mod.PreviewPaths.Count;
            mod.CurrentPreviewIndex = (mod.CurrentPreviewIndex + 1) % n;
        }

        // 导入文件到指定角色目录
        public void ImportFiles(string[] paths, Character target)
        {
            var path = SelectedGame?.ModsRootPath;
            if (paths == null || target == null || string.IsNullOrEmpty(path)) return;
            var targetDir = Path.Combine(path, target.Name);
            Directory.CreateDirectory(targetDir);

            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        var dest = Path.Combine(targetDir, Path.GetFileName(p));
                        if (!ConfirmOverwrite(dest))
                            continue;

                        CopyDirectory(p, dest);
                        var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(p), FilePath = dest };
                        foreach (var pp in FindPreviewsInDirectory(dest))
                            mod.PreviewPaths.Add(pp);
                        target.Mods.Add(mod);
                    }
                    else if (File.Exists(p))
                    {
                        var ext = Path.GetExtension(p).ToLowerInvariant();
                        if (ext == ".zip")
                        {
                            // 解压 zip 到角色目录下的一个子文件夹，名称为压缩包名（不含扩展名）
                            var modFolderName = Path.GetFileNameWithoutExtension(p);
                            var dest = Path.Combine(targetDir, modFolderName);
                            if (!ConfirmOverwrite(dest))
                                continue;

                            Directory.CreateDirectory(dest);
                            try
                            {
                                ZipFile.ExtractToDirectory(p, dest);
                            }
                            catch
                            {
                                // 如果解压失败，尝试直接复制文件作为备用
                                if (Directory.Exists(dest))
                                    Directory.Delete(dest, recursive: true);

                                var fallback = Path.Combine(targetDir, Path.GetFileName(p));
                                if (!ConfirmOverwrite(fallback))
                                    continue;

                                File.Copy(p, fallback);
                                var infoF = new FileInfo(fallback);
                                var modF = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(fallback), FilePath = fallback, Size = infoF.Length };
                                target.Mods.Add(modF);
                                continue;
                            }

                            var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(dest), FilePath = dest, Enabled = true };
                            foreach (var pp in FindPreviewsInDirectory(dest))
                                mod.PreviewPaths.Add(pp);
                            target.Mods.Add(mod);
                        }
                        else
                        {
                            var dest = Path.Combine(targetDir, Path.GetFileName(p));
                            if (!ConfirmOverwrite(dest))
                                continue;

                            File.Copy(p, dest);
                            var info = new FileInfo(dest);
                            var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = Path.GetFileName(dest), FilePath = dest, Size = info.Length };
                            target.Mods.Add(mod);
                        }
                    }
                }
                catch { }
            }

            SaveState();
        }

        private void DeleteMod()
        {
            var mod = SelectedMod;
            var character = SelectedCharacter;
            if (mod == null || character == null || string.IsNullOrWhiteSpace(mod.FilePath)) return;

            var result = MessageBox.Show(
                $"确定要删除 Mod“{mod.Name}”吗？此操作会删除磁盘上的文件，无法撤销。",
                "删除 Mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (Directory.Exists(mod.FilePath))
                    Directory.Delete(mod.FilePath, recursive: true);
                else if (File.Exists(mod.FilePath))
                    File.Delete(mod.FilePath);

                character.Mods.Remove(mod);
                SelectedMod = character.Mods.FirstOrDefault(m => m.Enabled) ?? character.Mods.FirstOrDefault();
                SaveState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除 Mod 失败：{ex.Message}", "删除 Mod", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CommitModRename(Mod mod)
        {
            var originalName = mod.OriginalNameDuringEdit;
            var requestedName = mod.Name?.Trim();
            mod.OriginalNameDuringEdit = null;

            if (string.IsNullOrWhiteSpace(originalName) || string.Equals(originalName, requestedName, StringComparison.Ordinal))
                return;

            if (string.IsNullOrWhiteSpace(requestedName) || requestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                mod.Name = originalName;
                MessageBox.Show("Mod 名称不能为空，也不能包含文件名非法字符。", "修改 Mod 名称", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var oldPath = mod.FilePath;
                var parent = Path.GetDirectoryName(oldPath);
                if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(oldPath))
                    throw new InvalidOperationException("找不到 Mod 的原始路径。");

                var oldRawName = Path.GetFileName(oldPath);
                var disabledPrefix = oldRawName.StartsWith("DISABLED_", StringComparison.OrdinalIgnoreCase) ? "DISABLED_" : string.Empty;
                var newPath = Path.Combine(parent, disabledPrefix + requestedName);

                if (File.Exists(newPath) || Directory.Exists(newPath))
                    throw new IOException("同名 Mod 已存在。");

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

        public void SaveModSource(Mod mod)
        {
            if (string.IsNullOrWhiteSpace(mod.FilePath)) return;

            _sourcesByModPath[mod.FilePath] = mod.Source;
            SaveState();
        }

        private void ApplySavedSource(Mod mod)
        {
            if (!string.IsNullOrWhiteSpace(mod.FilePath) && _sourcesByModPath.TryGetValue(mod.FilePath, out var source))
                mod.Source = source;
        }

        private void ReadIniFile(bool showMissingMessage)
        {
            var mod = SelectedMod;
            if (mod == null) return;

            var iniPath = FindIniFile(mod);
            if (iniPath == null)
            {
                mod.IniFilePath = null;
                mod.IniContent = null;
                mod.IniShortcuts.Clear();
                if (showMissingMessage)
                    MessageBox.Show("当前 Mod 中未找到符合条件的 INI 文件。", "读取 INI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                mod.IniFilePath = iniPath;
                mod.IniContent = File.ReadAllText(iniPath);
                LoadIniShortcuts(mod);
                SaveState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 INI 失败：{ex.Message}", "读取 INI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenIniFile()
        {
            var mod = SelectedMod;
            if (mod == null) return;

            var iniPath = FindIniFile(mod);
            if (iniPath == null)
            {
                MessageBox.Show("当前 Mod 中未找到 INI 文件。", "打开 INI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                mod.IniFilePath = iniPath;
                Process.Start(new ProcessStartInfo(iniPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开 INI 失败：{ex.Message}", "打开 INI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string? FindIniFile(Mod mod)
        {
            var folder = GetModFolder(mod);
            return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)
                ? null
                : Directory.EnumerateFiles(folder, "*.ini", SearchOption.AllDirectories)
                    .Where(path => !Path.GetFileName(path).Contains("disabled", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(path => new FileInfo(path).Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
        }

        private static void LoadIniShortcuts(Mod mod)
        {
            mod.IniShortcuts.Clear();
            if (string.IsNullOrWhiteSpace(mod.IniContent)) return;

            string? section = null;
            foreach (var line in mod.IniContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var text = line.Trim();
                if (text.StartsWith("[") && text.EndsWith("]"))
                {
                    section = text[1..^1];
                    continue;
                }

                if (section == null || !section.StartsWith("Key", StringComparison.OrdinalIgnoreCase))
                    continue;

                var separator = text.IndexOf('=');
                if (separator <= 0) continue;

                var key = text[..separator].Trim();
                var value = text[(separator + 1)..].Trim();
                if (!key.Equals("key", StringComparison.OrdinalIgnoreCase)) continue;

                mod.IniShortcuts.Add(new IniShortcut { Key = $"{section}: {value}", Value = 0, OptionIndex = 0 });
            }
        }

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

                if (!movedDirectory || !previewPath.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relativePath = Path.GetRelativePath(oldPath, previewPath);
                mod.PreviewPaths[index] = Path.Combine(newPath, relativePath);
            }
        }

        // 目标不存在时直接继续；存在时由用户决定是否用导入内容替换。
        private static bool ConfirmOverwrite(string destination)
        {
            if (!File.Exists(destination) && !Directory.Exists(destination))
                return true;

            var name = Path.GetFileName(destination);
            var result = MessageBox.Show(
                $"“{name}”已存在。是否覆盖？",
                "导入 Mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return false;

            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            else
                File.Delete(destination);

            return true;
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;
            Directory.CreateDirectory(destDir);
            foreach (var file in dir.GetFiles())
            {
                file.CopyTo(Path.Combine(destDir, file.Name), true);
            }
            foreach (var sub in dir.GetDirectories())
            {
                CopyDirectory(sub.FullName, Path.Combine(destDir, sub.Name));
            }
        }

        public void SaveState()
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                var snap = new StateSnapshot
                {
                    Games = Games.ToArray(),
                    Characters = Characters.ToArray()
                };
                var opt = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(StateFile, JsonSerializer.Serialize(snap, opt));
            }
            catch { }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// 持久化快照，ModsRootPath 已内嵌在每个 Game 对象中，不再需要全局字段。
        /// </summary>
        private class StateSnapshot
        {
            public Game[] Games { get; set; }
            public Character[] Characters { get; set; }
        }
    }
}
