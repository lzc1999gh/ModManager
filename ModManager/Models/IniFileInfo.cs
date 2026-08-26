using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ModManager.Models
{
    public class IniFileInfo : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string RelativePath { get; set; }

        [JsonIgnore]
        public string FileName => Path.GetFileName(FilePath ?? RelativePath);

        [JsonIgnore]
        public bool HasToggleKey { get; set; }

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public string Content { get; set; }

        [JsonIgnore]
        public ObservableCollection<IniShortcut> Shortcuts { get; } = new ObservableCollection<IniShortcut>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
