using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using kfupet_local_chat.Models;
using kfupet_local_chat.Services;
using kfupet_local_chat.Views;

namespace kfupet_local_chat
{
    /// <summary>
    /// 试点台主窗口：负责布局拼装、入场动画、按钮分发，以及本地模型的下载 / 启动 / 结束 / 卸载。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly LocalModelRuntime _runtime = new();
        private readonly ModelDeploymentService _deployment = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            Closed += (_, _) => _runtime.Dispose();

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

            LogView.Append("试点台已启动：本地模型可按需下载、启动与卸载。");
            RefreshDeployedState();
        }

        /// <summary>按钮事件分发：冒泡到窗口时用 OriginalSource 找回真正的按钮，再按 Tag 处理。</summary>
        private async void OnActionClicked(object sender, RoutedEventArgs e)
        {
            if (FindButton(e.OriginalSource) is not { } element)
            {
                return;
            }

            var action = element.Tag?.ToString() ?? "未命名操作";
            switch (action)
            {
                case "清空结果":
                    ResultView.ResetResult();
                    LogView.Append("结果区已清空。");
                    return;
                case "下载模型":
                    DetectAndShowModels();
                    return;
                case "运行本地":
                    await StartModelAsync();
                    return;
                case "停止本地":
                    await StopModelAsync();
                    return;
                case "卸载模型":
                    await UninstallModelAsync();
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
                if (dialog.ShowDialog() == true && dialog.DeployedModel is { } deployed)
                {
                    EngineView.ShowDeployedModel(deployed);
                    ActionView.UpdateRunState(true, false, false);
                    LogView.Append($"[部署] {deployed.ModelName} 已下载并部署：{deployed.FilePath}");
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

        /// <summary>启动时检查是否已有部署记录，有则切换到管理状态，但不自动加载模型。</summary>
        private void RefreshDeployedState()
        {
            var deployed = _deployment.Load();
            if (deployed == null)
            {
                EngineView.ShowDownloadMode();
                ActionView.UpdateRunState(false, false, false);
                return;
            }

            EngineView.ShowDeployedModel(deployed);
            ActionView.UpdateRunState(true, false, false);
            LogView.Append($"[本地模型] 检测到已部署模型：{deployed.ModelName}（{deployed.SizeText}），未启动。");
        }

        /// <summary>用 LLamaSharp 加载模型权重与上下文；有独显时把层全部卸载到 GPU。</summary>
        private async Task StartModelAsync()
        {
            if (_runtime.IsRunning)
            {
                return;
            }

            var deployed = _deployment.Load();
            if (deployed == null)
            {
                EngineView.ShowDownloadMode();
                ActionView.UpdateRunState(false, false, false);
                LogView.Append("[本地模型] 尚未部署模型，无法启动。");
                return;
            }

            var hardware = HardwareProbe.Detect();
            var modelGb = deployed.SizeBytes / 1024d / 1024d / 1024d;
            var autoGpu = hardware.HasDedicatedGpu && hardware.VramGb >= modelGb * 1.2;
            var device = EngineView.SelectedDevice;

            var useGpu = device switch
            {
                InferenceDevice.Gpu => true,
                InferenceDevice.Cpu => false,
                _ => autoGpu
            };

            if (device == InferenceDevice.Gpu && !hardware.HasDedicatedGpu)
            {
                useGpu = false;
                LogView.Append("[本地模型] 未检测到独显，已按 CPU 运行。");
            }

            var gpuLayers = useGpu ? 999 : 0;

            EngineView.ShowHardware(hardware);
            EngineView.UpdateRuntime(false, true, "正在加载模型…");
            ActionView.UpdateRunState(true, false, true, "正在加载…");
            LogView.Append($"[本地模型] 正在加载 {deployed.ModelName}" +
                           $"（{(useGpu ? "GPU 加速" : "CPU 推理")}）…");

            try
            {
                await _runtime.StartAsync(deployed.FilePath, gpuLayers);
                EngineView.UpdateRuntime(true, false, useGpu ? "已启动 · GPU" : "已启动 · CPU");
                ActionView.UpdateRunState(true, true, false);
                var deviceText = useGpu ? $"GPU（{hardware.GpuName}）" : "CPU";
                LogView.Append($"[本地模型] 已启动：{deployed.ModelName} · {deviceText}");
            }
            catch (Exception ex)
            {
                EngineView.UpdateRuntime(false, false, "启动失败");
                ActionView.UpdateRunState(true, false, false);
                LogView.Append($"[本地模型] 启动失败：{ex.Message}");
                MessageBox.Show(this, $"模型启动失败：\n{ex.Message}", "启动模型",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>释放模型占用的显存与内存。</summary>
        private async Task StopModelAsync()
        {
            if (!_runtime.IsRunning)
            {
                EngineView.UpdateRuntime(false, false, "已停止");
                ActionView.UpdateRunState(true, false, false);
                return;
            }

            EngineView.UpdateRuntime(true, true, "正在结束…");
            ActionView.UpdateRunState(true, true, true, "正在停止…");
            await Task.Run(() => _runtime.Stop());
            EngineView.UpdateRuntime(false, false, "已停止");
            ActionView.UpdateRunState(true, false, false);
            LogView.Append("[本地模型] 已结束，占用已释放。");
        }

        /// <summary>卸载模型：先结束运行，再删除模型文件与部署记录。</summary>
        private async Task UninstallModelAsync()
        {
            var deployed = _deployment.Load();
            if (deployed == null)
            {
                EngineView.ShowDownloadMode();
                ActionView.UpdateRunState(false, false, false);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"确认卸载 {deployed.ModelName}？\n\n将删除本地模型文件（{deployed.SizeText}），" +
                "下次使用需要重新下载。",
                "卸载模型",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var wasRunning = _runtime.IsRunning;
            EngineView.UpdateRuntime(wasRunning, true, "正在卸载…");
            ActionView.UpdateRunState(true, wasRunning, true, "正在卸载…");

            try
            {
                if (wasRunning)
                {
                    await Task.Run(() => _runtime.Stop());
                }

                _deployment.DeleteFiles(deployed);
                EngineView.ShowDownloadMode();
                ActionView.UpdateRunState(false, false, false);
                LogView.Append($"[本地模型] 已卸载：{deployed.ModelName}");
            }
            catch (Exception ex)
            {
                EngineView.ShowDeployedModel(deployed);
                EngineView.UpdateRuntime(_runtime.IsRunning, false, _runtime.IsRunning ? "已启动" : "已停止");
                ActionView.UpdateRunState(true, _runtime.IsRunning, false);
                LogView.Append($"[本地模型] 卸载失败：{ex.Message}");
                MessageBox.Show(this, $"卸载失败：\n{ex.Message}", "卸载模型",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
