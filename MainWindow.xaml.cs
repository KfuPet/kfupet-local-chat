using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
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
        private static readonly Brush EngineIdleBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly Brush EngineRunningBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        private static readonly Brush EngineEmptyBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
        private static readonly Brush EngineFailedBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

        private readonly LocalModelRuntime _runtime = new();
        private readonly ModelDeploymentService _deployment = new();

        private ConsoleWindow? _consoleWindow;
        private PerformanceSampler? _sampler;
        private RunRecord? _lastRun;
        private DateTime _runStartedAt;
        private HardwareInfo? _runHardware;
        private DeployedModel? _runDeployed;
        private InferenceDevice _runDevice;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            EngineView.ModelSwitchRequested += OnModelSwitchRequested;
            Closed += (_, _) =>
            {
                _runtime.Dispose();
                _ = _sampler?.StopAsync();
            };

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
                DataSourceView, EngineView, ActionView, ResultView, LogView
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
            RefreshModelLibrary(log: true);
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
                case "启动控制台":
                    ShowConsole();
                    return;
                case "导出报告":
                    ExportReport();
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
            if (_runtime.IsRunning)
            {
                LogView.Append("[部署] 本地模型正在运行，请先停止后再下载其他模型。");
                MessageBox.Show(this, "请先停止正在运行的本地模型，再下载其他模型。", "下载模型",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                    EngineView.ShowModels(_deployment.LoadAll(), deployed);
                    ActionView.UpdateRunState(true, false, false);
                    SetEngineStatus("本地引擎：未加载", EngineIdleBrush, running: false);
                    LogView.Append($"[部署] {deployed.ModelName} 已下载并切换为当前模型：{deployed.FilePath}");
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

        /// <summary>刷新已下载模型列表与当前模型；没有模型时显示下载入口。</summary>
        private void RefreshModelLibrary(bool log = false)
        {
            var models = _deployment.LoadAll();
            var active = _deployment.LoadActive();
            if (active == null)
            {
                EngineView.ShowDownloadMode();
                ActionView.UpdateRunState(false, false, false);
                SetEngineStatus("本地引擎：未部署", EngineEmptyBrush, running: false);
                return;
            }

            EngineView.ShowModels(models, active, _runtime.IsRunning);
            ActionView.UpdateRunState(true, _runtime.IsRunning, false);

            if (_runtime.IsRunning)
            {
                SetEngineStatus("本地引擎：已加载", EngineRunningBrush, running: true);
            }
            else
            {
                SetEngineStatus("本地引擎：未加载", EngineIdleBrush, running: false);
            }

            if (log)
            {
                LogView.Append($"[本地模型] 当前模型：{active.ModelName}（{active.SizeText}），" +
                               $"共 {models.Count} 个已下载，{(_runtime.IsRunning ? "运行中" : "未启动")}。");
            }
        }

        /// <summary>切换当前使用的模型；运行中不允许切换，避免文件被占用。</summary>
        private void OnModelSwitchRequested(string fileName)
        {
            if (_runtime.IsRunning)
            {
                LogView.Append("[本地模型] 模型运行中，切换前请先停止。");
                MessageBox.Show(this, "请先停止正在运行的本地模型，再切换模型。", "切换模型",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshModelLibrary();
                return;
            }

            var models = _deployment.LoadAll();
            var target = models.FirstOrDefault(m =>
                string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                RefreshModelLibrary();
                return;
            }

            _deployment.SetActive(target.FileName);
            RefreshModelLibrary();
            LogView.Append($"[本地模型] 已切换为：{target.ModelName}（{target.SizeText}）");
        }

        /// <summary>更新右上角引擎状态：文字、指示灯颜色；运行中停止呼吸动画。</summary>
        private void SetEngineStatus(string text, Brush dotBrush, bool running)
        {
            StatusText.Text = text;
            StatusDot.Fill = dotBrush;

            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1;

            if (!running)
            {
                StatusDot.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(1000))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    });
            }
        }

        /// <summary>用 LLamaSharp 加载模型权重与上下文；有独显时把层全部卸载到 GPU。</summary>
        private async Task StartModelAsync()
        {
            if (_runtime.IsRunning)
            {
                return;
            }

            var deployed = _deployment.LoadActive();
            if (deployed == null)
            {
                EngineView.ShowDownloadMode();
                ActionView.UpdateRunState(false, false, false);
                SetEngineStatus("本地引擎：未部署", EngineEmptyBrush, running: false);
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

            _runStartedAt = DateTime.Now;
            _runHardware = hardware;
            _runDeployed = deployed;
            _runDevice = device;
            _sampler = new PerformanceSampler();
            _sampler.Start();

            EngineView.ShowHardware(hardware);
            EngineView.UpdateRuntime(false, true, "正在加载模型…");
            ActionView.UpdateRunState(true, false, true, "正在加载…");
            SetEngineStatus("本地引擎：正在加载…", EngineIdleBrush, running: false);
            LogView.Append($"[本地模型] 正在加载 {deployed.ModelName}" +
                           $"（{(useGpu ? "GPU 加速" : "CPU 推理")}）…");

            try
            {
                await _runtime.StartAsync(deployed.FilePath, gpuLayers);
                EngineView.UpdateRuntime(true, false, useGpu ? "已启动 · GPU" : "已启动 · CPU");
                ActionView.UpdateRunState(true, true, false);
                var deviceText = useGpu ? $"GPU（{hardware.GpuName}）" : "CPU";
                SetEngineStatus(useGpu ? "本地引擎：已加载 · GPU" : "本地引擎：已加载 · CPU",
                    EngineRunningBrush, running: true);
                LogView.Append($"[本地模型] 已启动：{deployed.ModelName} · {deviceText}");
            }
            catch (Exception ex)
            {
                if (_sampler != null)
                {
                    await _sampler.StopAsync();
                    _sampler = null;
                }

                EngineView.UpdateRuntime(false, false, "启动失败");
                ActionView.UpdateRunState(true, false, false);
                SetEngineStatus("本地引擎：加载失败", EngineFailedBrush, running: false);
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
                SetEngineStatus("本地引擎：未加载", EngineIdleBrush, running: false);
                return;
            }

            EngineView.UpdateRuntime(true, true, "正在结束…");
            ActionView.UpdateRunState(true, true, true, "正在停止…");
            SetEngineStatus("本地引擎：正在停止…", EngineIdleBrush, running: false);
            await Task.Run(() => _runtime.Stop());

            var samples = _sampler != null
                ? await _sampler.StopAsync()
                : Array.Empty<PerformanceSample>();
            _sampler = null;

            EngineView.UpdateRuntime(false, false, "已停止");
            ActionView.UpdateRunState(true, false, false);
            SetEngineStatus("本地引擎：未加载", EngineIdleBrush, running: false);
            LogView.Append("[本地模型] 已结束，占用已释放。");
            CompleteRun(samples);
        }

        /// <summary>卸载模型：先结束运行，再删除模型文件与部署记录。</summary>
        private async Task UninstallModelAsync()
        {
            var deployed = _deployment.LoadActive();
            if (deployed == null)
            {
                EngineView.ShowDownloadMode();
                ActionView.UpdateRunState(false, false, false);
                SetEngineStatus("本地引擎：未部署", EngineEmptyBrush, running: false);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"确认卸载 {deployed.ModelName}？\n\n将删除它的本地模型文件（{deployed.SizeText}），" +
                "其他已下载模型会保留。",
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
            SetEngineStatus("本地引擎：正在卸载…", EngineIdleBrush, running: false);

            try
            {
                if (wasRunning)
                {
                    await Task.Run(() => _runtime.Stop());

                    var samples = _sampler != null
                        ? await _sampler.StopAsync()
                        : Array.Empty<PerformanceSample>();
                    _sampler = null;
                    CompleteRun(samples);
                }

                _deployment.DeleteFiles(deployed);
                RefreshModelLibrary();

                var remaining = _deployment.LoadActive();
                if (remaining == null)
                {
                    LogView.Append($"[本地模型] 已卸载：{deployed.ModelName}，当前没有可用模型。");
                }
                else
                {
                    LogView.Append($"[本地模型] 已卸载：{deployed.ModelName}，已切换到 {remaining.ModelName}。");
                }
            }
            catch (Exception ex)
            {
                var models = _deployment.LoadAll();
                var active = _deployment.LoadActive();
                if (active != null)
                {
                    EngineView.ShowModels(models, active);
                    EngineView.UpdateRuntime(_runtime.IsRunning, false, _runtime.IsRunning ? "已启动" : "已停止");
                    ActionView.UpdateRunState(true, _runtime.IsRunning, false);
                    SetEngineStatus(_runtime.IsRunning ? "本地引擎：已加载" : "本地引擎：未加载",
                        _runtime.IsRunning ? EngineRunningBrush : EngineIdleBrush, _runtime.IsRunning);
                }
                else
                {
                    EngineView.ShowDownloadMode();
                    ActionView.UpdateRunState(false, false, false);
                    SetEngineStatus("本地引擎：未部署", EngineEmptyBrush, running: false);
                }

                LogView.Append($"[本地模型] 卸载失败：{ex.Message}");
                MessageBox.Show(this, $"卸载失败：\n{ex.Message}", "卸载模型",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>打开调试控制台；已打开时直接激活原窗口。</summary>
        private void ShowConsole()
        {
            if (_consoleWindow is { IsVisible: true })
            {
                _consoleWindow.Activate();
                return;
            }

            _consoleWindow = new ConsoleWindow(_runtime) { Owner = this };
            _consoleWindow.Closed += (_, _) => _consoleWindow = null;
            _consoleWindow.Show();
            LogView.Append("[控制台] 已打开启动控制台。");
        }

        /// <summary>记录一次完整运行；只有存在记录时导出按钮才可用。</summary>
        private void CompleteRun(IReadOnlyList<PerformanceSample> samples)
        {
            if (samples.Count == 0 || _runHardware == null)
            {
                return;
            }

            _lastRun = new RunRecord
            {
                StartedAt = _runStartedAt,
                EndedAt = DateTime.Now,
                Hardware = _runHardware,
                Model = _runDeployed,
                Device = _runDevice,
                Samples = samples,
                Logs = LogView.GetLines().ToList()
            };

            ActionView.SetExportEnabled(true);
            LogView.Append($"[报告] 本次运行已记录（采样 {samples.Count} 次），可导出报告。");
        }

        /// <summary>导出最近一次完整运行的报告：report.md + charts.html 打包为 zip。</summary>
        private void ExportReport()
        {
            if (_lastRun is not { } run)
            {
                LogView.Append("[报告] 请先完整运行一次（启动后停止）再导出。");
                return;
            }

            try
            {
                var markdown = ReportService.BuildMarkdown(run);
                var html = ReportService.BuildHtml(run);

                var dialog = new SaveFileDialog
                {
                    Title = "导出运行报告",
                    Filter = "ZIP 压缩包 (*.zip)|*.zip",
                    FilterIndex = 1,
                    DefaultExt = ".zip",
                    AddExtension = true,
                    FileName = $"KfuPet本地模型报告_{run.StartedAt:yyyyMMdd_HHmmss}.zip"
                };

                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (Directory.Exists(documents))
                {
                    dialog.InitialDirectory = documents;
                }

                if (dialog.ShowDialog(this) != true)
                {
                    LogView.Append("[报告] 已取消导出。");
                    return;
                }

                using (var archive = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create))
                {
                    WriteZipEntry(archive, "report.md", markdown);
                    WriteZipEntry(archive, "charts.html", html);
                }

                LogView.Append($"[报告] 已导出：{dialog.FileName}");
                MessageBox.Show(this, $"报告已导出到：\n{dialog.FileName}\n\n包含 report.md 与 charts.html。",
                    "导出报告", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogView.Append($"[报告] 导出失败：{ex.Message}");
                MessageBox.Show(this, $"导出失败：\n{ex.Message}", "导出报告",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void WriteZipEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private void DataSourceView_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void EngineView_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
