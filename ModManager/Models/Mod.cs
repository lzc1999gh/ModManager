using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

        private string? _previewPath;
        public string? PreviewPath
        {
            get => _previewPath;
            set
            {
                if (_previewPath == value) return;
                _previewPath = value;
                OnPropertyChanged();
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
