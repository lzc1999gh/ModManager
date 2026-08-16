using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ModManager.Models
{
    public class Mod : INotifyPropertyChanged
    {
        private bool _enabled;
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
                _previewPaths = value ?? new ObservableCollection<string>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPreviewPath));
                OnPropertyChanged(nameof(PreviewPath));
            }
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
                OnPropertyChanged(nameof(PreviewPath));
            }
        }

        // 当前显示的预览图路径（只读，供 View 绑定）
        public string? CurrentPreviewPath =>
            PreviewPaths != null && PreviewPaths.Count > 0
                && CurrentPreviewIndex >= 0 && CurrentPreviewIndex < PreviewPaths.Count
                ? PreviewPaths[CurrentPreviewIndex]
                : null;

        // 兼容属性：旧代码/旧序列化数据可能直接赋值 PreviewPath，通过 setter 加入列表
        [JsonIgnore]
        public string? PreviewPath
        {
            get => CurrentPreviewPath;
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (PreviewPaths == null) PreviewPaths = new ObservableCollection<string>();
                if (!PreviewPaths.Contains(value))
                    PreviewPaths.Add(value);
                CurrentPreviewIndex = PreviewPaths.IndexOf(value);
            }
        }
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
