using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfApp1.Models
{
    public class Character : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name { get; set; }
        // 可选的头像路径，用于图标显示
        public string IconPath { get; set; }
        public ObservableCollection<Mod> Mods { get; set; } = new ObservableCollection<Mod>();

        public Character()
        {
            Mods.CollectionChanged += Mods_CollectionChanged;
        }

        private void Mods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ModCount));
        }

        public int ModCount => Mods?.Count ?? 0;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
