using System.Windows;

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

    }
}
