using System.Windows;
using System.Windows.Controls;
using ModManager.Models;
using ModManager.ViewModels;

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

        private void EditGameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is MenuItem menuItem && menuItem.DataContext is Game game && !game.IsAddGamePlaceholder)
                vm.EditGame(game);
        }

        private void DeleteGameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is MenuItem menuItem && menuItem.DataContext is Game game && !game.IsAddGamePlaceholder)
                vm.DeleteGame(game);
        }

        private void ModDetailView_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
