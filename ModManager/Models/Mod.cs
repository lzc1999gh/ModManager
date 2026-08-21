using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ModManager.Models
{
    public class Mod : INotifyPropertyChanged
    {
        private bool _enabled;
        public Mod()
        {
            _previewPaths.CollectionChanged += PreviewPaths_CollectionChanged;
        }

        public string Id { get; set; }
        private string? _name;
        public string? Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        private string? _filePath;
        public string? FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath == value) return;
                _filePath = value;
                OnPropertyChanged();
            }
        }

        // 预览图路径列表（一个 mod 可有多张预览图）
        private ObservableCollection<string> _previewPaths = new ObservableCollection<string>();
        public ObservableCollection<string> PreviewPaths
        {
            get => _previewPaths;
            set
            {
                if (_previewPaths == value) return;
                _previewPaths.CollectionChanged -= PreviewPaths_CollectionChanged;
                _previewPaths = value ?? new ObservableCollection<string>();
                _previewPaths.CollectionChanged += PreviewPaths_CollectionChanged;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPreviewPath));
            }
        }

        private string? _source;
        public string? Source
        {
            get => _source;
            set
            {
                if (_source == value) return;
                _source = value;
                OnPropertyChanged();
            }
        }

        private bool _isEditingName;
        [JsonIgnore]
        public bool IsEditingName
        {
            get => _isEditingName;
            set
            {
                if (_isEditingName == value) return;
                _isEditingName = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public string? OriginalNameDuringEdit { get; set; }

        private string? _iniFilePath;
        public string? IniFilePath
        {
            get => _iniFilePath;
            set
            {
                if (_iniFilePath == value) return;
                _iniFilePath = value;
                OnPropertyChanged();
            }
        }

        private string? _iniContent;
        [JsonIgnore]
        public string? IniContent
        {
            get => _iniContent;
            set
            {
                if (_iniContent == value) return;
                _iniContent = value;
                OnPropertyChanged();
            }
        }

        private void PreviewPaths_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CurrentPreviewPath));
        }

        private int _currentPreviewIndex;
        public int CurrentPreviewIndex
        {
            get => _currentPreviewIndex;
            set
            {
                int newVal;
                if (PreviewPaths == null || PreviewPaths.Count == 0)
                    newVal = 0;
                else
                    newVal = Math.Max(0, Math.Min(value, PreviewPaths.Count - 1));
                if (_currentPreviewIndex == newVal) return;
                _currentPreviewIndex = newVal;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPreviewPath));
            }
        }

        // 当前显示的预览图路径（只读，供 View 绑定）
        public string? CurrentPreviewPath =>
            PreviewPaths != null && PreviewPaths.Count > 0
                && CurrentPreviewIndex >= 0 && CurrentPreviewIndex < PreviewPaths.Count
                ? PreviewPaths[CurrentPreviewIndex]
                : null;

        public ObservableCollection<IniShortcut> IniShortcuts { get; set; } = new ObservableCollection<IniShortcut>();
        // 可选：文件大小（字节）
        public long Size { get; set; }
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
