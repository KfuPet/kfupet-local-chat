using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using kfupet_local_chat.Services;

namespace kfupet_local_chat.Views
{
    /// <summary>
    /// 主对话窗口：一轮问答后立即判断这一轮是否有值得长期记住的信息，
    /// 并把达标内容写入长期记忆，后续对话按重要性注入上下文。
    /// </summary>
    public partial class ChatWindow : Window
    {
        private readonly LocalModelRuntime _runtime;
        private readonly ChatSessionService _chat;
        private readonly MemoryStoreService _memories = new();
        private readonly CancellationTokenSource _cts = new();

        private bool _sending;
        private TextBlock? _typingNote;

        public ChatWindow(LocalModelRuntime runtime)
        {
            InitializeComponent();
            _runtime = runtime;
            _chat = new ChatSessionService(runtime, _memories);

            RefreshModelStatus();
            RefreshMemories();
            AppendSystemNote("和摇摇打个招呼吧。每轮回复之后，它会判断这一轮有没有值得长期记住的信息。");

            Loaded += (_, _) => InputBox.Focus();
            Activated += (_, _) => RefreshModelStatus();
            Closing += (_, _) => _cts.Cancel();
        }

        private void OnSendClick(object sender, RoutedEventArgs e)
        {
            _ = SendAsync();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift)
            {
                return;
            }

            e.Handled = true;
            _ = SendAsync();
        }

        /// <summary>发送一句话：拿到回复后立刻在后台做本轮的记忆提取。</summary>
        private async Task SendAsync()
        {
            if (_sending)
            {
                return;
            }

            var text = InputBox.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            RefreshModelStatus();
            if (!_runtime.IsRunning)
            {
                AppendSystemNote("本地模型尚未启动，请先在主窗口点击「运行本地」。");
                return;
            }

            _sending = true;
            InputBox.Clear();
            AppendUserBubble(text);
            ShowTyping();

            var watch = Stopwatch.StartNew();
            try
            {
                var turn = await Task.Run(() => _chat.SendAsync(text, _cts.Token));
                watch.Stop();
                HideTyping();
                AppendAssistantBubble(turn.Assistant, watch.Elapsed.TotalSeconds);

                // 与主项目一致：不等整个会话结束，这一轮回复完就立即判断
                _ = RunExtractionAsync(turn);
            }
            catch (OperationCanceledException)
            {
                HideTyping();
            }
            catch (Exception ex)
            {
                HideTyping();
                AppendSystemNote($"对话失败：{ex.Message}");
            }
            finally
            {
                _sending = false;
                RefreshModelStatus();
                InputBox.Focus();
            }
        }

        /// <summary>本轮记忆提取：达标条目写入长期记忆，结果以系统提示形式出现在对话流里。</summary>
        private async Task RunExtractionAsync(ChatTurn turn)
        {
            // 1.7B 等档位不支持稳定提取，直接跳过，连请求都不发
            if (!_chat.CanExtractMemory(out var skipReason))
            {
                AppendSystemNote($"记忆 · {skipReason}");
                return;
            }

            var note = AppendSystemNote("记忆 · 正在判断本轮是否值得长期记住…");
            try
            {
                var result = await Task.Run(() => _chat.ExtractMemoryAsync(turn, _cts.Token));
                RemoveNote(note);

                if (result.Skipped)
                {
                    AppendSystemNote($"记忆 · {result.SkipReason}");
                    return;
                }

                if (!result.Parsed)
                {
                    AppendSystemNote("记忆 · 本轮提取没有返回可解析的 JSON，已跳过。");
                    return;
                }

                if (result.Stored.Count == 0)
                {
                    AppendSystemNote(result.Core.Count == 0
                        ? "记忆 · 本轮没有值得长期记住的信息。"
                        : "记忆 · 本轮信息重要性不足 3，未写入长期记忆。");
                    return;
                }

                foreach (var memory in result.Stored)
                {
                    AppendSystemNote($"记忆 · 已记住：{memory.Content}（重要性 {memory.Importance}）");
                }

                RefreshMemories();
            }
            catch (OperationCanceledException)
            {
                RemoveNote(note);
            }
            catch (Exception ex)
            {
                RemoveNote(note);
                AppendSystemNote($"记忆 · 提取失败：{ex.Message}");
            }
        }

        private void OnClearMemoriesClick(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(this, "确认清空全部长期记忆？", "清空记忆",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            _memories.Clear();
            RefreshMemories();
            AppendSystemNote("长期记忆已清空。");
        }

        private void AppendUserBubble(string text)
        {
            var bubble = new Border
            {
                Background = (Brush)FindResource("AccentBrush"),
                CornerRadius = new CornerRadius(12, 12, 4, 12),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 4),
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    LineHeight = 20,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.White
                }
            };

            AddElement(bubble);
        }

        private void AppendAssistantBubble(string text, double seconds)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = $"摇摇 · {seconds:0.0}s",
                FontSize = 10,
                Foreground = (Brush)FindResource("FaintBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("SubBrush")
            });

            var bubble = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("LineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4, 12, 12, 12),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 4),
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = content
            };

            AddElement(bubble);
        }

        private TextBlock AppendSystemNote(string text)
        {
            var note = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (Brush)FindResource("FaintBrush"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 620,
                Margin = new Thickness(0, 6, 0, 2)
            };

            AddElement(note);
            return note;
        }

        /// <summary>追加元素并做一次淡入上浮，保持与主界面一致的动效。</summary>
        private void AddElement(FrameworkElement element)
        {
            ChatPanel.Children.Add(element);

            element.Opacity = 0;
            var transform = new TranslateTransform(0, 8);
            element.RenderTransform = transform;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var rise = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            element.BeginAnimation(OpacityProperty, fade);
            transform.BeginAnimation(TranslateTransform.YProperty, rise);

            ScrollToEnd();
        }

        private void ShowTyping()
        {
            HideTyping();
            _typingNote = AppendSystemNote("摇摇正在输入…");
            _typingNote.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700))
                {
                    BeginTime = TimeSpan.FromMilliseconds(240),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }

        private void HideTyping()
        {
            if (_typingNote != null)
            {
                RemoveNote(_typingNote);
                _typingNote = null;
            }
        }

        private void RemoveNote(TextBlock note)
        {
            ChatPanel.Children.Remove(note);
        }

        private void ScrollToEnd()
        {
            Dispatcher.BeginInvoke(new Action(() => TranscriptScroll.ScrollToEnd()),
                DispatcherPriority.Background);
        }

        private void RefreshModelStatus()
        {
            if (!_runtime.IsRunning)
            {
                ModelStatusText.Text = "模型未启动";
                ModelStatusText.Foreground = (Brush)FindResource("FaintBrush");
                return;
            }

            var supported = _chat.CanExtractMemory(out _);
            ModelStatusText.Text = supported
                ? $"已连接 · {_chat.ActiveModelName} · 每轮自动提炼记忆"
                : $"已连接 · {_chat.ActiveModelName} · 不支持记忆提取";
            ModelStatusText.Foreground = supported
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("WarnBrush");
        }

        private void RefreshMemories()
        {
            var memories = _memories.Load();
            MemoryList.ItemsSource = memories;
            MemoryCountText.Text = $"{memories.Count} 条";
            MemoryEmptyText.Visibility = memories.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
