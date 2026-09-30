using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ModManager.Views
{
    /// <summary>消息对话框的严重级别，决定图标符号与配色。</summary>
    public enum MessageDialogKind
    {
        Information,
        Warning,
        Error,
        Question
    }

    /// <summary>
    /// 统一风格的消息对话框：替代系统 MessageBox，
    /// 使启动提示、警告与确认弹窗与主界面共用同一套设计语言。
    /// </summary>
    public partial class MessageDialog : Window
    {
        private bool _isConfirm;

        private MessageDialog() => InitializeComponent();

        /// <summary>显示对话框；返回 true 表示用户选择了「确定」或「是」。</summary>
        public static bool Show(Window? owner, string title, string message, MessageDialogKind kind, bool confirm)
        {
            var dialog = new MessageDialog();
            dialog.Init(owner, title, message, kind, confirm);
            return dialog.ShowDialog() == true;
        }

        private void Init(Window? owner, string title, string message, MessageDialogKind kind, bool confirm)
        {
            Title = string.IsNullOrWhiteSpace(title) ? "提示" : title;
            TitleText.Text = Title;
            MessageText.Text = message ?? string.Empty;

            _isConfirm = confirm;
            CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
            OkButton.Content = confirm ? "是" : "确定";

            ApplyKind(kind);

            // Owner 尚未加载（启动早期）时退化为屏幕居中，避免设置 Owner 抛异常
            if (owner != null && owner.IsLoaded)
            {
                Owner = owner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void ApplyKind(MessageDialogKind kind)
        {
            IconInfo.Visibility = kind == MessageDialogKind.Information ? Visibility.Visible : Visibility.Collapsed;
            IconWarning.Visibility = kind == MessageDialogKind.Warning ? Visibility.Visible : Visibility.Collapsed;
            IconError.Visibility = kind == MessageDialogKind.Error ? Visibility.Visible : Visibility.Collapsed;
            IconQuestion.Visibility = kind == MessageDialogKind.Question ? Visibility.Visible : Visibility.Collapsed;

            IconBadge.Background = kind switch
            {
                MessageDialogKind.Information => (Brush)FindResource("ThemeInfoSoftBrush"),
                MessageDialogKind.Warning => (Brush)FindResource("ThemeWarnSoftBrush"),
                _ => (Brush)FindResource("ThemeAccentSoftBrush")
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // 单按钮弹窗无「否」可取消，Esc 视为确认关闭；确认弹窗 Esc 等同「否」
            if (e.Key == Key.Escape)
            {
                DialogResult = !_isConfirm;
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
