namespace ModManager.Models
{
    public class Game : System.ComponentModel.INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _characterPicPath = "CharacterPic";
        private string _characterInfoPath = "CharacterInfo.json";
        private string _gameIconPath = "GameIcon.svg";
        private string _modsRootPath = string.Empty;
        private string _d3dxUserIniPath = string.Empty;
        private string _iconPath = string.Empty;
        private bool _isVectorIcon = true;

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

        // 角色头像目录。应用内目录保存相对路径，外部目录可保存绝对路径。
        public string CharacterPicPath
        {
            get => _characterPicPath;
            set
            {
                if (_characterPicPath == value) return;
                _characterPicPath = value;
                OnPropertyChanged();
            }
        }

        // 当前游戏目录中的角色信息文件相对路径。
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

        // 当前游戏目录中的游戏图标相对路径。
        public string GameIconPath
        {
            get => _gameIconPath;
            set
            {
                if (_gameIconPath == value) return;
                _gameIconPath = value;
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

        // Each game may use a different XXMI/3DMigoto user configuration.
        public string D3dxUserIniPath
        {
            get => _d3dxUserIniPath;
            set
            {
                if (_d3dxUserIniPath == value) return;
                _d3dxUserIniPath = value;
                OnPropertyChanged();
            }
        }

        // 游戏选择栏使用的运行时图标 URI，不写入游戏配置文件。
        [System.Text.Json.Serialization.JsonIgnore]
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

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsVectorIcon
        {
            get => _isVectorIcon;
            set
            {
                if (_isVectorIcon == value) return;
                _isVectorIcon = value;
                OnPropertyChanged();
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsAddGamePlaceholder { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
