using System.Windows.Controls;
using System.Windows;
using System;
using System.Linq;

using ModManager.ViewModels;
using ModManager.Models;

namespace ModManager.Views
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

    }
}
