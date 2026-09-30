using System.Windows;
using ModManager.Views;

namespace ModManager.Services
{
    /// <summary>
    /// 对话框抽象：把 ViewModel 从具体弹窗实现中解耦，便于替换与测试。
    /// </summary>
    public interface IDialogService
    {
        void ShowInformation(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);

        /// <summary>是/否确认，返回是否选择了“是”。</summary>
        bool Confirm(string message, string title);
    }

    /// <summary>
    /// 默认实现：使用项目自绘的 <see cref="MessageDialog"/>（与主界面同一套设计语言），
    /// 弹窗归属主窗口。
    /// </summary>
    public class DialogService : IDialogService
    {
        private static Window? Owner => Application.Current?.MainWindow;

        public void ShowInformation(string message, string title) =>
            MessageDialog.Show(Owner, title, message, MessageDialogKind.Information, confirm: false);

        public void ShowWarning(string message, string title) =>
            MessageDialog.Show(Owner, title, message, MessageDialogKind.Warning, confirm: false);

        public void ShowError(string message, string title) =>
            MessageDialog.Show(Owner, title, message, MessageDialogKind.Error, confirm: false);

        public bool Confirm(string message, string title) =>
            MessageDialog.Show(Owner, title, message, MessageDialogKind.Question, confirm: true);
    }
}
