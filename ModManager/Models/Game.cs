namespace ModManager.Models
{
    public class Game : System.ComponentModel.INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private string _path;
        private string _characterInfoPath;
        private string _modsRootPath;

        public string Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged();
            }
        }

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

        // 保留旧版 Path 字段，当前语义是角色头像目录。
        public string Path
        {
            get => _path;
            set
            {
                if (_path == value) return;
                _path = value;
                OnPropertyChanged();
            }
        }

        // 新增游戏时可指定角色信息文件；为空时使用应用数据目录。
        public string CharacterInfoPath
        {
            get => _characterInfoPath;
            set
            {
                if (_characterInfoPath == value) return;
                _characterInfoPath = value;
                OnPropertyChanged();
            }
        }

        // 每个游戏独立的 Mods 根目录路径（用户可配置）
        public string ModsRootPath
        {
            get => _modsRootPath;
            set
            {
                if (_modsRootPath == value) return;
                _modsRootPath = value;
                OnPropertyChanged();
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsAddGamePlaceholder { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
