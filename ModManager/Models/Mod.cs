using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfApp1.Models
{
    public class Mod : INotifyPropertyChanged
    {
        private bool _enabled;
        public string Id { get; set; }
        public string Name { get; set; }
        // 原始文件或文件夹路径
        public string FilePath { get; set; }
        // 预览图片路径（可为空）
        public string PreviewPath { get; set; }
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
