using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using kfupet_local_chat.Services;

namespace kfupet_local_chat.Views
{
    /// <summary>调试控制台：按主项目的模型调用点逐项接入；触摸反应已接入本地推理。</summary>
    public partial class ConsoleWindow : Window
    {
        /// <summary>按截图量的物理像素尺寸，运行时换算成 WPF 逻辑单位。</summary>
        private const double TargetPixelWidth = 1783;
        private const double TargetPixelHeight = 1032;

        private static readonly Brush OutputBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B));

        private readonly LocalModelRuntime _runtime;
        private bool _inferring;

        public ConsoleWindow(LocalModelRuntime runtime)
        {
            InitializeComponent();
            _runtime = runtime;
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

        /// <summary>触摸反应：用当前本地模型生成一句回应，并显示耗时。</summary>
        private async void OnTouchReactionClick(object sender, RoutedEventArgs e)
        {
            if (_inferring)
            {
                return;
            }

            if (!_runtime.IsRunning)
            {
                AppendOutput("触摸反应：本地模型尚未启动，请先在主窗口点击「运行本地」。");
                return;
            }

            var eventPrompt = TouchReactionPrompt.BuildEventPrompt(
                TouchReactionPrompt.PartHead, isPetting: true);
            if (string.IsNullOrEmpty(eventPrompt))
            {
                AppendOutput("触摸反应：事件提示为空，已跳过。");
                return;
            }

            _inferring = true;
            TouchReactionButton.IsEnabled = false;
            AppendOutput("触摸反应：已提交（headPet），正在生成…");

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await _runtime.InferAsync(
                    TouchReactionPrompt.BuildSystemPrompt(), eventPrompt);
                var line = TouchReactionPrompt.NormalizeLine(reply);
                stopwatch.Stop();

                AppendOutput(string.IsNullOrEmpty(line)
                    ? $"触摸反应：模型返回空内容（{stopwatch.ElapsedMilliseconds} ms）"
                    : $"触摸反应（headPet）：{line}（{stopwatch.ElapsedMilliseconds} ms）");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                AppendOutput($"触摸反应失败（{stopwatch.ElapsedMilliseconds} ms）：{ex.Message}");
            }
            finally
            {
                _inferring = false;
                TouchReactionButton.IsEnabled = true;
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
