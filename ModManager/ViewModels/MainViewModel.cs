using ModManager.Models;
using System;
using System.Collections.Generic;
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
            set { _selectedCharacter = value; OnPropertyChanged(); }
        }

        private Mod _selectedMod;
        public Mod SelectedMod
        {
            get => _selectedMod;
            set { _selectedMod = value; OnPropertyChanged(); }
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
            ToggleModCommand = new RelayCommand(p => ToggleMod(p as Mod), p => p is Mod);
            ActivateModCommand = new RelayCommand(p => ActivateMod(p as Mod), p => p is Mod);
            // commands for apply/clear removed; settings handled in MainWindow
            OpenModsRootCommand = new RelayCommand(p => OpenModsRoot(), p => !string.IsNullOrEmpty(ModsRootPath));
            RefreshModsCommand = new RelayCommand(p => RefreshMods(), p => !string.IsNullOrEmpty(ModsRootPath));
            ToggleShowOnlyWithModsCommand = new RelayCommand(p => { ShowOnlyWithMods = !ShowOnlyWithMods; }, p => true);
            AddPreviewCommand = new RelayCommand(p => AddPreview());
            DeletePreviewCommand = new RelayCommand(p => DeletePreview());
            PrevPreviewCommand = new RelayCommand(p => PrevPreview());
            NextPreviewCommand = new RelayCommand(p => NextPreview());

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

        private string GetProjectRoot()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, "AppContext.BaseDirectory/Resources");
                    if (File.Exists(candidate))
                        return dir.FullName; // 返回包含 .csproj 的目录（项目根）
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
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

            var previewFiles = Directory.EnumerateFiles(dir)
                .Where(f => Path.GetFileName(f).StartsWith("preview_", StringComparison.OrdinalIgnoreCase))
                .Where(f => Array.IndexOf(exts, Path.GetExtension(f).ToLowerInvariant()) >= 0)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            result.AddRange(previewFiles);

            if (result.Count == 0)
            {
                var allImages = Directory.EnumerateFiles(dir)
                    .Where(f => Array.IndexOf(exts, Path.GetExtension(f).ToLowerInvariant()) >= 0)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                result.AddRange(allImages);
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
                        // 简单复制目录（不处理冲突）
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
                            if (Directory.Exists(dest) || File.Exists(dest))
                            {
                                dest = Path.Combine(targetDir, modFolderName + "_" + Guid.NewGuid().ToString("N"));
                            }
                            Directory.CreateDirectory(dest);
                            try
                            {
                                ZipFile.ExtractToDirectory(p, dest);
                            }
                            catch
                            {
                                // 如果解压失败，尝试直接复制文件作为备用
                                var fallback = Path.Combine(targetDir, Path.GetFileName(p));
                                File.Copy(p, fallback, overwrite: true);
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
                            File.Copy(p, dest, overwrite: true);
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
