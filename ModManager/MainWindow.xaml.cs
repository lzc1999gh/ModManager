using System.IO;
using System.Windows;
using ModManager.ViewModels;
using Ookii.Dialogs.Wpf;

namespace ModManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // 绑定 MainViewModel
            this.DataContext = new ViewModels.MainViewModel();
        }

        private void BtnSetModsRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog
            {
                Description = "请选择该游戏的 Mods 根目录",
                UseDescriptionForTitle = true
            };
            var result = dlg.ShowDialog(this);
            if (result == true)
            {
                if (this.DataContext is MainViewModel vm && vm.SelectedGame != null)
                {
                    vm.SelectedGame.ModsRootPath = dlg.SelectedPath;
                    vm.ModsRootPathIsUserSet = true;
                    vm.SaveState();
                    if (Directory.Exists(vm.SelectedGame.ModsRootPath))
                        vm.LoadFromModsRoot(vm.SelectedGame.ModsRootPath);
                }
            }
        }
    }
}