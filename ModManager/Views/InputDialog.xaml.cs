using System.Windows;

namespace ModManager.Views
{
    /// <summary>
    /// 简单的单行文本输入对话框，用于获取用户输入的文本（如新增角色时的角色名）。
    /// </summary>
    public partial class InputDialog : Window
    {
        /// <summary>
        /// 用户输入的结果文本（点击确定后有效，已去除首尾空白）。
        /// </summary>
        public string ResultText => InputTextBox.Text?.Trim() ?? string.Empty;

        /// <summary>
        /// 创建输入对话框。
        /// </summary>
        /// <param name="title">窗口标题</param>
        /// <param name="prompt">输入框上方的提示文本</param>
        public InputDialog(string title, string prompt, string initialText = null)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            InputTextBox.Text = initialText ?? string.Empty;
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        /// <summary>
        /// 确定按钮点击：以 true 关闭对话框，表示用户已确认输入。
        /// </summary>
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        /// <summary>
        /// 取消按钮点击：以 false 关闭对话框，表示用户取消输入。
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
