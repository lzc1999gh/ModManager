using Ookii.Dialogs.Wpf;
using System.Windows;

namespace ModManager.Views
{
    public partial class GameDialog : Window
    {
        public string GameId => GameIdTextBox.Text?.Trim() ?? string.Empty;
        public string GameName => GameNameTextBox.Text?.Trim() ?? string.Empty;
        public string ModsRootPath => ModsRootTextBox.Text?.Trim() ?? string.Empty;
        public string CharacterPicPath => CharacterPicTextBox.Text?.Trim() ?? string.Empty;

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

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
