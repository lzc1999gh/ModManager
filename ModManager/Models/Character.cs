using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ModManager.Models
{
    public class Character : INotifyPropertyChanged
    {
        public string Id { get; set; }
        // 角色所属游戏，用于在状态文件中区分不同游戏的角色。
        public string GameId { get; set; }
        public string Name { get; set; }
        // 标记是否为列表末尾的“新增角色”占位项（不参与持久化、不视为真实角色）
        [JsonIgnore]
        public bool IsAddPlaceholder { get; set; }
        // 可选的头像路径，仅用于列表显示，不决定角色是否存在。
        private string _iconPath;
        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (_iconPath == value) return;
                _iconPath = value;
                OnPropertyChanged();
            }
        }
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
