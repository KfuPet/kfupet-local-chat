using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using kfupet_local_chat.Services;
using kfupet_local_chat.Views;

namespace kfupet_local_chat
{
    /// <summary>
    /// 试点台主窗口：只负责布局拼装、入场动画与按钮事件的统一分发，具体功能由 Views 下的面板承载。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;

            // 子面板里的按钮不逐个订阅事件，统一在这里按 Tag 分发；
            // 面板内部已处理（并标记 Handled）的按钮不再进入这里
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnActionClicked));
        }

        /// <summary>入场动画：窗口淡入，各面板依次上浮。</summary>
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            var panels = new FrameworkElement[]
            {
                DataSourceView, EngineView, ScopeView, ActionView, ResultView, LogView
            };

            for (var i = 0; i < panels.Length; i++)
            {
                var panel = panels[i];
                var translate = new TranslateTransform(0, 14);
                panel.RenderTransform = translate;
                panel.Opacity = 0;

                var delay = TimeSpan.FromMilliseconds(60 + i * 55);
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var rise = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                panel.BeginAnimation(OpacityProperty, fade);
                translate.BeginAnimation(TranslateTransform.YProperty, rise);
            }

            LogView.Append("试点台已启动：界面骨架就绪，推理与回放逻辑尚未接入。");
        }

        /// <summary>按钮事件分发：冒泡到窗口时用 OriginalSource 找回真正的按钮，再按 Tag 处理。</summary>
        private void OnActionClicked(object sender, RoutedEventArgs e)
        {
            if (FindButton(e.OriginalSource) is not { } element)
            {
                return;
            }

            var action = element.Tag?.ToString() ?? "未命名操作";
            if (action == "清空结果")
            {
                ResultView.ResetResult();
                LogView.Append("结果区已清空。");
                return;
            }

            if (action == "下载模型")
            {
                DetectAndShowModels();
                return;
            }

            LogView.Append($"{action}：空壳入口，逻辑待接入。");
        }

        /// <summary>从事件源向上找到承载 Tag 的按钮，模板化按钮的原始事件源通常是内部文本或边框。</summary>
        private static Button? FindButton(object? source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is Button button)
                {
                    return button;
                }

                current = current is Visual or Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        /// <summary>检测本机硬件，按显存与内存给出可部署模型列表。</summary>
        private void DetectAndShowModels()
        {
            try
            {
                var hardware = HardwareProbe.Detect();
                EngineView.ShowHardware(hardware);
                LogView.Append($"硬件检测完成：{hardware.GpuName} / 显存 {hardware.VramGb:0.0} GB / " +
                               $"内存 {hardware.RamGb:0.0} GB / {hardware.LogicalCores} 线程");

                var options = ModelCatalog.Recommend(hardware);
                var dialog = new DeployWindow(hardware, options) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    LogView.Append($"[部署] 已确认 {dialog.ConfirmedModelName}（下载与加载逻辑待接入）");
                }
                else
                {
                    LogView.Append("[部署] 已取消模型选择。");
                }
            }
            catch (Exception ex)
            {
                LogView.Append($"[部署] 硬件检测失败：{ex.Message}");
            }
        }

        private void DataSourceView_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void EngineView_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
