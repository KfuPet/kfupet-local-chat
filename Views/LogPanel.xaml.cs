using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace kfupet_local_chat.Views
{
    /// <summary>运行日志面板：只负责显示与清空，不承载业务逻辑。</summary>
    public partial class LogPanel : UserControl
    {
        private static readonly Brush LogBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B));

        public LogPanel()
        {
            InitializeComponent();
        }

        /// <summary>追加一条带时间戳的日志，新行淡入并自动滚到可见位置。</summary>
        public void Append(string message)
        {
            var item = new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = $"{DateTime.Now:HH:mm:ss}  {message}",
                    FontSize = 11.5,
                    Foreground = LogBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            LogList.Items.Add(item);
            item.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            LogList.ScrollIntoView(item);
        }

        public void Clear()
        {
            LogList.Items.Clear();
        }

        /// <summary>读取当前全部日志文本，供导出报告使用。</summary>
        public IReadOnlyList<string> GetLines()
        {
            return LogList.Items
                .OfType<ListBoxItem>()
                .Select(item => (item.Content as TextBlock)?.Text ?? item.Content?.ToString() ?? string.Empty)
                .ToList();
        }

        /// <summary>清空按钮由面板自己处理，并阻止事件继续冒泡到主窗口的统一分发。</summary>
        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            Clear();
            Append("日志已清空。");
            e.Handled = true;
        }
    }
}
