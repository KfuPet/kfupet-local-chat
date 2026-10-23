using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace kfupet_local_chat.Views
{
    /// <summary>调试控制台：按主项目的模型调用点逐项接入，目前全部为空壳。</summary>
    public partial class ConsoleWindow : Window
    {
        /// <summary>按截图量的物理像素尺寸，运行时换算成 WPF 逻辑单位。</summary>
        private const double TargetPixelWidth = 1783;
        private const double TargetPixelHeight = 1032;

        private static readonly Brush OutputBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B));

        public ConsoleWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
        }

        /// <summary>把截图里的物理像素换算成当前 DPI 下的逻辑尺寸，并限制在工作区内。</summary>
        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Width = TargetPixelWidth / dpi.DpiScaleX;
            Height = TargetPixelHeight / dpi.DpiScaleY;

            var area = SystemParameters.WorkArea;
            if (Width > area.Width)
            {
                Width = area.Width;
            }

            if (Height > area.Height)
            {
                Height = area.Height;
            }
        }

        /// <summary>所有调用点共用的空壳入口：只回显调用了哪一项。</summary>
        private void OnShellCallClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var name = button.Content?.ToString() ?? "未命名调用";
            AppendOutput($"{name}：空壳入口，逻辑待接入。");
        }

        private void OnClearOutputClick(object sender, RoutedEventArgs e)
        {
            OutputList.Items.Clear();
            PlaceholderText.Visibility = System.Windows.Visibility.Visible;
        }

        private void AppendOutput(string message)
        {
            var item = new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = $"{DateTime.Now:HH:mm:ss}  {message}",
                    FontSize = 12,
                    Foreground = OutputBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            PlaceholderText.Visibility = System.Windows.Visibility.Collapsed;
            OutputList.Items.Add(item);
            OutputList.ScrollIntoView(item);
        }
    }
}
