using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ModManager.Models
{
    public class Character : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        // 角色所属游戏，用于在状态文件中区分不同游戏的角色。
        public string GameId { get; set; } = string.Empty;
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
            }
        }
        // 标记是否为列表末尾的“新增角色”占位项（不参与持久化、不视为真实角色）
        [JsonIgnore]
        public bool IsAddPlaceholder { get; set; }

        // 隐藏状态：随 state.json 持久化。隐藏只影响列表显示，
        // 不删除角色信息、不移动 Mod 目录、不影响 persist 状态。
        private bool _isHidden;
        public bool IsHidden
        {
            get => _isHidden;
            set
            {
                if (_isHidden == value) return;
                _isHidden = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HiddenActionText));
            }
        }

        // 右键菜单的菜单项文案，随隐藏状态切换（计算属性，不写入 state.json）。
        [JsonIgnore]
        public string HiddenActionText => IsHidden ? "取消隐藏" : "隐藏角色";
        // 可选的头像路径，仅用于列表显示，不决定角色是否存在。
        private string? _iconPath;
        public string? IconPath
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

        private void Mods_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ModCount));
        }

        public int ModCount => Mods?.Count ?? 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
