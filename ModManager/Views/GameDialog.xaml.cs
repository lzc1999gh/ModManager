using Ookii.Dialogs.Wpf;
using Microsoft.Win32;
using System.Windows;

namespace ModManager.Views
{
    public partial class GameDialog : Window
    {
        public string GameId => GameIdTextBox.Text?.Trim() ?? string.Empty;
        public string GameName => GameNameTextBox.Text?.Trim() ?? string.Empty;
        public string ModsRootPath => ModsRootTextBox.Text?.Trim() ?? string.Empty;
        public string CharacterPicPath => CharacterPicTextBox.Text?.Trim() ?? string.Empty;
        public string D3dxUserIniPath => D3dxUserIniTextBox.Text?.Trim() ?? string.Empty;

        public GameDialog()
        {
            InitializeComponent();
            GameIdTextBox.Focus();
        }

        public GameDialog(ModManager.Models.Game game) : this()
        {
            Title = "修改游戏";
            GameIdTextBox.Text = game?.Id ?? string.Empty;
            GameNameTextBox.Text = game?.Name ?? string.Empty;
            ModsRootTextBox.Text = game?.ModsRootPath ?? string.Empty;
            CharacterPicTextBox.Text = game?.Path ?? string.Empty;
            D3dxUserIniTextBox.Text = game?.D3dxUserIniPath ?? string.Empty;
        }

        private void BrowseModsRoot_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "请选择该游戏的 Mods 根目录",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog(this) == true) ModsRootTextBox.Text = dialog.SelectedPath;
        }

        private void BrowseCharacterPic_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "请选择角色头像目录（可以留空）",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog(this) == true) CharacterPicTextBox.Text = dialog.SelectedPath;
        }

        private void BrowseD3dxUserIni_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "请选择 d3dx_user.ini",
                Filter = "INI 文件 (*.ini)|*.ini|所有文件 (*.*)|*.*",
                CheckFileExists = false,
                FileName = "d3dx_user.ini"
            };
            if (dialog.ShowDialog(this) == true) D3dxUserIniTextBox.Text = dialog.FileName;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
