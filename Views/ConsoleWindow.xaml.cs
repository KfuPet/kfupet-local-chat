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

        /// <summary>触摸反应：用当前本地模型生成一句回应。</summary>
        private async void OnTouchReactionClick(object sender, RoutedEventArgs e)
        {
            await RunInferenceAsync(
                "触摸反应（headPet）",
                TouchReactionPrompt.BuildEventPrompt(TouchReactionPrompt.PartHead, isPetting: true));
        }

        /// <summary>拖动越界回正。</summary>
        private async void OnSnapBackClick(object sender, RoutedEventArgs e)
        {
            await RunInferenceAsync("拖动越界回正", TouchReactionPrompt.BuildSnapBackEventPrompt());
        }

        /// <summary>主动搭话：用固定的“独处 30 分钟”语境演示。</summary>
        private async void OnProactiveClick(object sender, RoutedEventArgs e)
        {
            var contextNote = $"现在是 {DateTime.Now:HH:mm}，主人已经 30 分钟没有和你互动了。";
            await RunInferenceAsync(
                "主动搭话（idleChat）",
                TouchReactionPrompt.BuildProactiveEventPrompt("idleChat", contextNote));
        }

        /// <summary>更新通知正文：用固定版本信息演示。</summary>
        private async void OnUpdateNotificationClick(object sender, RoutedEventArgs e)
        {
            await RunInferenceAsync(
                "更新通知正文",
                UpdateNotificationPrompt.BuildEventPrompt(),
                UpdateNotificationPrompt.NormalizeLine);
        }

        /// <summary>已接入的调用点共用的推理入口：校验模型状态、禁用按钮并输出耗时。</summary>
        private async Task RunInferenceAsync(
            string label,
            string eventPrompt,
            Func<string, string>? normalize = null)
        {
            if (_inferring)
            {
                return;
            }

            if (!_runtime.IsRunning)
            {
                AppendOutput($"{label}：本地模型尚未启动，请先在主窗口点击「运行本地」。");
                return;
            }

            if (string.IsNullOrEmpty(eventPrompt))
            {
                AppendOutput($"{label}：事件提示为空，已跳过。");
                return;
            }

            _inferring = true;
            SetInferenceButtonsEnabled(false);
            AppendOutput($"{label}：已提交，正在生成…");

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await _runtime.InferAsync(
                    TouchReactionPrompt.BuildSystemPrompt(), eventPrompt);
                var line = (normalize ?? TouchReactionPrompt.NormalizeLine)(reply);
                stopwatch.Stop();

                AppendOutput(string.IsNullOrEmpty(line)
                    ? $"{label}：模型返回空内容（{stopwatch.ElapsedMilliseconds} ms）"
                    : $"{label}：{line}（{stopwatch.ElapsedMilliseconds} ms）");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                AppendOutput($"{label} 失败（{stopwatch.ElapsedMilliseconds} ms）：{ex.Message}");
            }
            finally
            {
                _inferring = false;
                SetInferenceButtonsEnabled(true);
            }
        }

        private void SetInferenceButtonsEnabled(bool enabled)
        {
            TouchReactionButton.IsEnabled = enabled;
            SnapBackButton.IsEnabled = enabled;
            ProactiveButton.IsEnabled = enabled;
            NotificationButton.IsEnabled = enabled;
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
