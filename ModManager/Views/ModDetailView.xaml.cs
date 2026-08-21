using System.Windows.Controls;
using System.Windows.Input;
using ModManager.ViewModels;

namespace ModManager.Views
{
    public partial class ModDetailView : System.Windows.Controls.UserControl
    {
        public ModDetailView()
        {
            InitializeComponent();
        }

        private void ModNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || DataContext is not MainViewModel vm || vm.SelectedMod == null)
                return;

            vm.SelectedMod.OriginalNameDuringEdit = vm.SelectedMod.Name;
            vm.SelectedMod.IsEditingName = true;
            e.Handled = true;
        }

        private void ModNameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitNameEdit();
        }

        private void ModNameEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            CommitNameEdit();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void SourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SelectedMod != null)
                vm.SaveModSource(vm.SelectedMod);
        }

        private void CommitNameEdit()
        {
            if (DataContext is not MainViewModel vm || vm.SelectedMod == null)
                return;

            vm.SelectedMod.IsEditingName = false;
            vm.CommitModRename(vm.SelectedMod);
        }
    }
}
