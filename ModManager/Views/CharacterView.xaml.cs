using System.Windows;
using System.Windows.Controls;
using ModManager.Models;
using ModManager.ViewModels;

namespace ModManager.Views
{
    public partial class CharacterView : System.Windows.Controls.UserControl
    {
        public CharacterView()
        {
            InitializeComponent();
        }

        private void ChangeCharacterIcon_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu
                && contextMenu.PlacementTarget is FrameworkElement target
                && target.DataContext is Character character)
            {
                vm.ChangeCharacterIcon(character);
            }
        }

        private void RenameCharacter_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu
                && contextMenu.PlacementTarget is FrameworkElement target
                && target.DataContext is Character character)
            {
                vm.RenameCharacter(character);
            }
        }
    }
}
