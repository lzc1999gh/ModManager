using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ModManager.ViewModels;
using Ookii.Dialogs.Wpf;
using System.IO;

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

        private void BtnSelectModsRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog
            {
                Description = "请选择 Mods 根目录（包含各角色子文件夹）",
                UseDescriptionForTitle = true
            };
            var result = dlg.ShowDialog(this);
            if (result == true)
            {
                if (this.DataContext is MainViewModel vm)
                {
                    vm.LoadFromModsRoot(dlg.SelectedPath);
                }
            }
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

        private void ModPreviewView_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void ModDetailView_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}