using System.Windows.Controls;
using System.Windows;
using System;
using System.Linq;

using WpfApp1.ViewModels;
using WpfApp1.Models;

namespace WpfApp1.Views
{
    public partial class ModListView : System.Windows.Controls.UserControl
    {
        public ModListView()
        {
            InitializeComponent();
        }

        private void ListBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
            else
                e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void ListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;
            var target = vm.SelectedCharacter;
            if (target == null) return;
            vm.ImportFiles(files, target);
        }

        private void ListBoxItem_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var lbi = sender as System.Windows.Controls.ListBoxItem;
            if (lbi == null) return;
            var mod = lbi.DataContext as Mod;
            if (mod == null) return;
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;
            // 优化：单击即选中并切换启用状态（单次点击完成切换）
            vm.SelectedMod = mod;
            vm.ToggleModCommand.Execute(mod);
            e.Handled = true;
        }
    }
}
