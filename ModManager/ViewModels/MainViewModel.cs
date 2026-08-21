using ModManager.Models;
using ModManager.Services;
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

namespace ModManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManager");
        private static readonly string StateFile = Path.Combine(StateDirectory, "modstate.json");
        private readonly GimiPersistService _gimiPersistService;
        private readonly Dictionary<string, string?> _sourcesByModPath = new(StringComparer.OrdinalIgnoreCase);

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
                if (_selectedGame == value) return;
                _selectedGame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModsRootPath));
                OnPropertyChanged(nameof(ModsRootPathIsUserSet));
                LoadCharactersForGame(_selectedGame);
                var gamePath = _selectedGame?.ModsRootPath;
                if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                {
                    LoadFromModsRoot(gamePath);
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
                ReadIniFile(showMissingMessage: false);
            }
        }

        // =========================================================
        // Commands
        // =========================================================
        public ICommand ToggleModCommand { get; }
        public ICommand OpenModsRootCommand { get; }
        public ICommand RefreshModsCommand { get; }
        public ICommand AddPreviewCommand { get; }
        public ICommand DeletePreviewCommand { get; }
        public ICommand PrevPreviewCommand { get; }
        public ICommand NextPreviewCommand { get; }
        public ICommand DeleteModCommand { get; }
        public ICommand ReadIniCommand { get; }
        public ICommand OpenIniCommand { get; }
        public ICommand OpenModFolderCommand { get; }

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

        public bool ModsRootPathIsUserSet
        {
            get => SelectedGame != null && !string.IsNullOrEmpty(SelectedGame.ModsRootPath);
            set => OnPropertyChanged();
        }

        // =========================================================
        // Constructor
        // =========================================================
        public MainViewModel()
        {
            _gimiPersistService = new GimiPersistService(@"H:\XXMI Launcher\GIMI\d3dx_user.ini");

            ToggleModCommand = new RelayCommand(p =>
            {
                if (p is not Mod mod) return;
                SelectedMod = mod;
                ToggleMod(mod);
            }, p => p is Mod);

            OpenModsRootCommand = new RelayCommand(p => OpenModsRoot(), p => !string.IsNullOrEmpty(ModsRootPath));
            RefreshModsCommand = new RelayCommand(p => RefreshMods(), p => !string.IsNullOrEmpty(ModsRootPath));

            AddPreviewCommand = new RelayCommand(p => AddPreview());
            DeletePreviewCommand = new RelayCommand(p => DeletePreview());
            PrevPreviewCommand = new RelayCommand(p => PrevPreview());
            NextPreviewCommand = new RelayCommand(p => NextPreview());

            DeleteModCommand = new RelayCommand(p => DeleteMod(), p => SelectedMod != null);
            ReadIniCommand = new RelayCommand(p => ReadIniFile(showMissingMessage: true), p => SelectedMod != null);
            OpenIniCommand = new RelayCommand(p => OpenIniFile(), p => SelectedMod != null);
            OpenModFolderCommand = new RelayCommand(OpenModFolder, p => SelectedMod != null);

            var resourceRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "CharacterPic");
            Games.Add(new Game { Id = "GI", Name = "GI", Path = Path.Combine(resourceRoot, "GI") });
            Games.Add(new Game { Id = "WW", Name = "WW", Path = Path.Combine(resourceRoot, "WW") });

            LoadStateOrSample();

            CharactersView = CollectionViewSource.GetDefaultView(Characters);
            CharactersView.Filter = CharacterFilter;
        }

        // =========================================================
        // Mod Folder
        // =========================================================
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
            var path = SelectedGame?.ModsRootPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            LoadFromModsRoot(path);
        }

        // =========================================================
        // Character Icons
        // =========================================================
        public void LoadCharacterIconsFromFolders(string[] folders)
        {
            if (folders == null) return;
            Characters.Clear();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var filePath in Directory.EnumerateFiles(folder, "*.png"))
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(filePath);
                        var character = new Character { Id = Guid.NewGuid().ToString(), Name = name, IconPath = filePath };
                        Characters.Add(character);
                    }
                    catch { }
                }
            }
            SelectedCharacter = Characters.FirstOrDefault();
            SaveState();
        }

        // =========================================================
        // Load Characters
        // =========================================================
        public void LoadCharactersForGame(Game game)
        {
            if (game == null) return;
            var folder = game.Path;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                Characters.Clear();
                SelectedCharacter = null;
                return;
            }
            LoadCharacterIconsFromFolders(new[] { folder });
            CharactersView?.Refresh();
        }

        // =========================================================
        // Character Filter
        // =========================================================
        private bool CharacterFilter(object o)
        {
            if (o is not Models.Character character) return true;
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
            try
            {
                var path = mod.FilePath;
                // Enabled -> Disabled：先读取并保存当前 Persist，再禁用 Mod
                if (mod.Enabled)
                {
                    Debug.WriteLine($"[GIMI Persist] Toggle OFF: saving {mod.Name}");
                    _gimiPersistService.SaveCurrentPersist(mod);
                }
                if (Directory.Exists(path))
                {
                    MoveMod(mod, path, isDirectory: true);
                }
                else if (File.Exists(path))
                {
                    MoveMod(mod, path, isDirectory: false);
                }
                // Disabled -> Enabled：Mod 路径恢复后，再根据 canonical Mod Path 恢复保存的状态
                if (mod.Enabled)
                {
                    Debug.WriteLine($"[GIMI Persist] Toggle ON: restoring {mod.Name}");
                    _gimiPersistService.RestorePersist(mod);
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
                        Games.Clear();
                        foreach (var savedGame in doc.Games) Games.Add(savedGame);
                        Characters.Clear();
                        foreach (var savedCharacter in doc.Characters) Characters.Add(savedCharacter);
                        SelectedGame = Games.FirstOrDefault();
                        SelectedCharacter = Characters.FirstOrDefault();
                        SelectedMod = SelectedCharacter?.Mods.FirstOrDefault(m => m.Enabled) ?? SelectedCharacter?.Mods.FirstOrDefault();
                        return;
                    }
                }
                catch { }
            }

            var sampleGame = new Game { Id = "g1", Name = "示例游戏", Path = "C:\\Games\\Example" };
            Games.Add(sampleGame);
            var sampleCharacter = new Character { Id = "c1", Name = "示例角色" };
            sampleCharacter.Mods.Add(new Mod { Id = "m1", Name = "Mod A", Enabled = true });
            sampleCharacter.Mods.Add(new Mod { Id = "m2", Name = "Mod B", Enabled = false });
            Characters.Add(sampleCharacter);
            SelectedGame = sampleGame;
            SelectedCharacter = sampleCharacter;
            SelectedMod = sampleCharacter.Mods.FirstOrDefault();
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
                    foreach (var previewPath in FindPreviewsInDirectory(modDir)) mod.PreviewPaths.Add(previewPath);
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
                    string preview = null;
                    var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                    var extension = Path.GetExtension(modFile).ToLowerInvariant();
                    if (Array.IndexOf(imageExtensions, extension) >= 0) preview = modFile;
                    var mod = new Mod { Id = Guid.NewGuid().ToString(), Name = displayName, FilePath = modFile, Size = info.Length, Enabled = enabled };
                    if (!string.IsNullOrEmpty(preview)) mod.PreviewPaths.Add(preview);
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
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
            var allFiles = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(filePath => Array.IndexOf(extensions, Path.GetExtension(filePath).ToLowerInvariant()) >= 0)
                .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var previewFiles = allFiles
                .Where(filePath => Path.GetFileNameWithoutExtension(filePath).StartsWith("preview_", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (previewFiles.Count > 0) result.AddRange(previewFiles);
            else result.AddRange(allFiles);
            return result;
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
        private string GetNextPreviewPath(string folder)
        {
            int max = 0;
            if (Directory.Exists(folder))
            {
                foreach (var filePath in Directory.EnumerateFiles(folder, "preview_*"))
                {
                    var name = Path.GetFileNameWithoutExtension(filePath);
                    if (!name.StartsWith("preview_", StringComparison.OrdinalIgnoreCase)) continue;
                    var numberText = name.Substring("preview_".Length);
                    if (int.TryParse(numberText, out var number) && number > max) max = number;
                }
            }
            return Path.Combine(folder, $"preview_{max + 1}.png");
        }

        // =========================================================
        // Add Preview
        // =========================================================
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
            var bitmap = Clipboard.GetImage();
            if (bitmap == null) return;
            var newPath = GetNextPreviewPath(folder);
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
            var rootPath = SelectedGame?.ModsRootPath;
            if (paths == null || target == null || string.IsNullOrEmpty(rootPath)) return;
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
            var result = MessageBox.Show($"确定要删除 Mod“{mod.Name}”吗？此操作会删除磁盘上的文件，无法撤销。", "删除 Mod", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                if (Directory.Exists(mod.FilePath)) Directory.Delete(mod.FilePath, recursive: true);
                else if (File.Exists(mod.FilePath)) File.Delete(mod.FilePath);
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
                _gimiPersistService.MovePersistState(oldPersistPath, newPath);
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
                if (showMissingMessage) MessageBox.Show("当前 Mod 中未找到符合条件的 INI 文件。", "读取 INI", MessageBoxButton.OK, MessageBoxImage.Information);
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

        // =========================================================
        // Open INI
        // =========================================================
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

        // =========================================================
        // Find INI
        // =========================================================
        private string? FindIniFile(Mod mod)
        {
            var folder = GetModFolder(mod);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            return Directory.EnumerateFiles(folder, "*.ini", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).Contains("disabled", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => new FileInfo(path).Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        // =========================================================
        // INI Shortcuts
        // =========================================================
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
                if (section == null || !section.StartsWith("Key", StringComparison.OrdinalIgnoreCase)) continue;
                var separator = text.IndexOf('=');
                if (separator <= 0) continue;
                var key = text[..separator].Trim();
                var value = text[(separator + 1)..].Trim();
                if (!key.Equals("key", StringComparison.OrdinalIgnoreCase)) continue;
                mod.IniShortcuts.Add(new IniShortcut { Key = $"{section}: {value}", Value = 0, OptionIndex = 0 });
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
                var snapshot = new StateSnapshot { Games = Games.ToArray(), Characters = Characters.ToArray() };
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
