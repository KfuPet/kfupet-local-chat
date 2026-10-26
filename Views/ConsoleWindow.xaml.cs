using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using kfupet_local_chat.Models;
using kfupet_local_chat.Services;

namespace kfupet_local_chat.Views
{
    /// <summary>调试控制台：按主项目的模型调用点逐项接入，全部调用点均走真实本地推理。</summary>
    public partial class ConsoleWindow : Window
    {
        private static readonly Brush OutputBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B));

        /// <summary>没有归档数据时“单条重评分”使用的固定演示内容，与主对话测试数据保持一致。</summary>
        private const string DemoScoreContent = "主人喜欢喝不加糖的美式咖啡，每天下午都要来一杯。";

        /// <summary>归档里低重要性条目不足 2 条时“多条总结”使用的固定演示片段。</summary>
        private static readonly string[] DemoSummaryFragments =
        {
            "主人今天出门忘带伞，回来时淋了点雨。",
            "晚饭点了一家新开的外卖，味道一般。",
            "今天开会开到很晚，回家已经快十点。"
        };

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

            if (_inferring)
            {
                AppendOutput("归档批量提炼：其他调用点正在生成，请等它结束后再试。");
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

        /// <summary>单条重评分：对归档里的总结条目逐条重新评分（对应 ScoreContentAsync）。</summary>
        private async void OnScoreContentClick(object sender, RoutedEventArgs e)
        {
            if (!CanStartMemoryCall("单条重评分"))
            {
                return;
            }

            var active = new ModelDeploymentService().LoadActive();
            var contents = (_dataSource?.Entries ?? Array.Empty<ArchiveMemoryEntry>())
                .Where(entry => !entry.IsConversation && !string.IsNullOrWhiteSpace(entry.Content))
                .Select(entry => entry.Content.Trim())
                .ToList();

            var usingDemo = contents.Count == 0;
            if (usingDemo)
            {
                contents.Add(DemoScoreContent);
            }

            _inferring = true;
            SetInferenceButtonsEnabled(false);

            var service = new ArchiveAnalysisService(_runtime, new MemoryStoreService());
            var stopwatch = Stopwatch.StartNew();
            AppendOutput($"单条重评分：开始，共 {contents.Count} 条（{active?.ModelName ?? "当前模型"}）" +
                         (usingDemo ? "；归档里没有总结条目，使用固定演示数据" : string.Empty));

            try
            {
                for (var i = 0; i < contents.Count; i++)
                {
                    var itemWatch = Stopwatch.StartNew();
                    var score = await Task.Run(
                        () => service.ScoreContentAsync(contents[i], _analysisCts.Token));
                    itemWatch.Stop();

                    var verdict = score >= MemoryExtractionPrompt.MinImportance
                        ? "达到阈值，可写入长期记忆"
                        : "低于阈值，丢弃";
                    AppendOutput($"单条重评分 {i + 1}/{contents.Count}：重要性 {score}/5" +
                                 $"（{itemWatch.Elapsed.TotalSeconds:0.0} s）· {verdict}：{contents[i]}");
                }

                stopwatch.Stop();
                AppendOutput($"单条重评分：完成，总耗时 {stopwatch.Elapsed.TotalSeconds:0.0} s");
            }
            catch (OperationCanceledException)
            {
                AppendOutput("单条重评分：已取消。");
            }
            catch (Exception ex)
            {
                AppendOutput($"单条重评分失败：{ex.Message}");
            }
            finally
            {
                _inferring = false;
                SetInferenceButtonsEnabled(true);
            }
        }

        /// <summary>多条总结：把归档里低于阈值的零散信息合成一句（对应 SummarizeAsync）。</summary>
        private async void OnSummarizeClick(object sender, RoutedEventArgs e)
        {
            if (!CanStartMemoryCall("多条总结"))
            {
                return;
            }

            var active = new ModelDeploymentService().LoadActive();
            if (!ModelCatalog.SupportsMemorySummarization(active))
            {
                AppendOutput($"多条总结：当前模型 {active?.ModelName ?? "未记录"} 无法稳定完成总结，" +
                             "请切换到 4B 或 8B。");
                return;
            }

            var contents = (_dataSource?.Entries ?? Array.Empty<ArchiveMemoryEntry>())
                .Where(entry => !entry.IsConversation &&
                                !string.IsNullOrWhiteSpace(entry.Content) &&
                                entry.Importance < MemoryExtractionPrompt.MinImportance)
                .Select(entry => entry.Content.Trim())
                .ToList();

            var usingDemo = contents.Count < 2;
            if (usingDemo)
            {
                contents = DemoSummaryFragments.ToList();
            }

            _inferring = true;
            SetInferenceButtonsEnabled(false);

            var service = new ArchiveAnalysisService(_runtime, new MemoryStoreService());
            var stopwatch = Stopwatch.StartNew();
            AppendOutput($"多条总结：开始，共 {contents.Count} 条零散信息（{active?.ModelName ?? "当前模型"}）" +
                         (usingDemo ? "；归档中低重要性条目不足 2 条，使用固定演示数据" : string.Empty));

            try
            {
                var summary = await Task.Run(
                    () => service.SummarizeAsync(contents, _analysisCts.Token));
                stopwatch.Stop();

                AppendOutput(string.IsNullOrWhiteSpace(summary)
                    ? $"多条总结：模型没有返回有效总结（{stopwatch.Elapsed.TotalSeconds:0.0} s）"
                    : $"多条总结：{summary}（{stopwatch.Elapsed.TotalSeconds:0.0} s）");
            }
            catch (OperationCanceledException)
            {
                AppendOutput("多条总结：已取消。");
            }
            catch (Exception ex)
            {
                AppendOutput($"多条总结失败：{ex.Message}");
            }
            finally
            {
                _inferring = false;
                SetInferenceButtonsEnabled(true);
            }
        }

        /// <summary>记忆类调用点共用的前置检查：归档提炼中、其他推理进行中、模型未启动都直接提示。</summary>
        private bool CanStartMemoryCall(string label)
        {
            if (_analyzingArchive)
            {
                AppendOutput($"{label}：归档提炼正在进行，请等它结束后再试。");
                return false;
            }

            if (_inferring)
            {
                return false;
            }

            if (!_runtime.IsRunning)
            {
                AppendOutput($"{label}：本地模型尚未启动，请先在主窗口点击「运行本地」。");
                return false;
            }

            return true;
        }

        private void SetInferenceButtonsEnabled(bool enabled)
        {
            TouchReactionButton.IsEnabled = enabled;
            SnapBackButton.IsEnabled = enabled;
            ProactiveButton.IsEnabled = enabled;
            NotificationButton.IsEnabled = enabled;
            ScoreButton.IsEnabled = enabled;
            SummarizeButton.IsEnabled = enabled;
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
