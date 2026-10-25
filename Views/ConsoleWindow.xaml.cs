using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using kfupet_local_chat.Models;
using kfupet_local_chat.Services;

namespace kfupet_local_chat.Views
{
    /// <summary>调试控制台：按主项目的模型调用点逐项接入；触摸反应等 4 项与归档批量提炼已接入本地推理。</summary>
    public partial class ConsoleWindow : Window
    {
        private static readonly Brush OutputBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B));

        private readonly LocalModelRuntime _runtime;
        private readonly DataSourceCard? _dataSource;
        private readonly Action<ArchiveAnalysisResult>? _onAnalysisCompleted;
        private readonly CancellationTokenSource _analysisCts = new();
        private ChatWindow? _chatWindow;
        private bool _inferring;
        private bool _analyzingArchive;
        private Task? _analysisTask;

        public ConsoleWindow(
            LocalModelRuntime runtime,
            DataSourceCard? dataSource = null,
            Action<ArchiveAnalysisResult>? onAnalysisCompleted = null)
        {
            InitializeComponent();
            _runtime = runtime;
            _dataSource = dataSource;
            _onAnalysisCompleted = onAnalysisCompleted;
            Closed += (_, _) => _analysisCts.Cancel();
        }

        /// <summary>主对话：输入一句话就能与桌宠多轮聊天，每轮自动判断是否值得长期记住。</summary>
        private void OnOpenChatClick(object sender, RoutedEventArgs e)
        {
            ShowChatWindow();
        }

        /// <summary>实时核心信息提取已并入主对话，每轮回复后自动执行。</summary>
        private void OnRealtimeExtractClick(object sender, RoutedEventArgs e)
        {
            ShowChatWindow();
            AppendOutput("实时核心信息提取：已并入主对话，每轮回复后自动执行，结果见右侧长期记忆。");
        }

        private void ShowChatWindow()
        {
            if (_chatWindow is { IsVisible: true })
            {
                _chatWindow.Activate();
                return;
            }

            _chatWindow = new ChatWindow(_runtime) { Owner = this };
            _chatWindow.Closed += (_, _) => _chatWindow = null;
            _chatWindow.Show();
            AppendOutput("主对话：已打开，每轮回复后会自动判断是否值得长期记住。");
        }

        /// <summary>归档批量提炼：用主窗口数据源里选中的归档文件跑完整分析，结果显示在下方输出卡片。</summary>
        private void OnArchiveExtractClick(object sender, RoutedEventArgs e)
        {
            if (_analyzingArchive)
            {
                return;
            }

            _analysisTask = RunArchiveAnalysisAsync();
        }

        /// <summary>取消并等待正在进行的归档提炼，供主窗口全量清除前调用。</summary>
        public async Task CancelAnalysisAsync()
        {
            _analysisCts.Cancel();
            if (_analysisTask is { } task)
            {
                try
                {
                    await task;
                }
                catch
                {
                    // 任务内部已处理异常
                }
            }
        }

        private async Task RunArchiveAnalysisAsync()
        {
            IReadOnlyList<ArchiveMemoryEntry> entries =
                _dataSource?.Entries ?? Array.Empty<ArchiveMemoryEntry>();

            if (entries.Count == 0)
            {
                AppendOutput("归档批量提炼：请先在主窗口左侧「数据源」选择归档文件。");
                return;
            }

            if (!_runtime.IsRunning)
            {
                AppendOutput("归档批量提炼：本地模型尚未启动，请先在主窗口点击「运行本地」。");
                return;
            }

            var active = new ModelDeploymentService().LoadActive();
            if (!ModelCatalog.SupportsMemoryExtraction(active))
            {
                AppendOutput($"归档批量提炼：当前模型 {active?.ModelName ?? "未记录"} 不支持稳定的信息提取，" +
                             "请切换到 4B 或 8B。");
                return;
            }

            _analyzingArchive = true;
            ArchiveExtractButton.IsEnabled = false;
            ConsoleResultView.ShowBusy();
            AppendOutput($"归档批量提炼：开始，共 {entries.Count} 条（{active?.ModelName}）。");

            var progress = new Progress<string>(AppendOutput);
            try
            {
                var service = new ArchiveAnalysisService(_runtime, new MemoryStoreService());
                var result = await Task.Run(() =>
                    service.AnalyzeAsync(entries, progress, _analysisCts.Token));

                // 窗口已关闭（例如全量清除时）：不再更新界面，也不回传结果
                if (_analysisCts.IsCancellationRequested)
                {
                    return;
                }

                ConsoleResultView.ShowArchiveResult(result);
                _dataSource?.RefreshCounts();
                _onAnalysisCompleted?.Invoke(result);

                AppendOutput(result.Succeeded
                    ? $"归档批量提炼：完成，提炼 {result.ExtractedCount} 条，" +
                      $"写入长期记忆 {result.StoredCount} 条，耗时 {result.Seconds:0.0} s；" +
                      $"重建归档已另存：{result.RebuiltArchivePath}"
                    : $"归档批量提炼：{result.Message}");
            }
            catch (Exception ex)
            {
                ConsoleResultView.ShowError(ex.Message);
                AppendOutput($"归档批量提炼失败：{ex.Message}");
            }
            finally
            {
                _analyzingArchive = false;
                ArchiveExtractButton.IsEnabled = true;
                _analysisTask = null;
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
