using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using kfupet_local_chat.Models;
using kfupet_local_chat.Services;

namespace kfupet_local_chat.Views
{
    /// <summary>模型选择与下载窗口：选择三档模型，下载完成后写入部署记录并关闭。</summary>
    public partial class DeployWindow : Window
    {
        private readonly HardwareInfo _hardware;
        private readonly ModelDeploymentService _deployment = new();

        private CancellationTokenSource? _downloadCts;
        private bool _downloading;
        private string _modelDirectory = AppPaths.ModelsDirectory;

        public DeployWindow(HardwareInfo hardware, IReadOnlyList<LocalModelOption> options)
        {
            InitializeComponent();
            _hardware = hardware;
            _modelDirectory = AppSettingsService.ResolveModelDirectory();

            HwGpuText.Text = hardware.GpuName;
            HwVramText.Text = hardware.HasDedicatedGpu
                ? $"独显显存 {hardware.VramGb:0.0} GB"
                : "核显 / 无独显，CPU 推理";
            HwRamText.Text = $"{hardware.RamGb:0.0} GB";
            HwCpuText.Text = $"{hardware.LogicalCores} 线程";
            UpdateStoragePath();

            ModelList.ItemsSource = options;
            ModelList.SelectedItem = options.FirstOrDefault(o => o.IsRecommended)
                                     ?? options.FirstOrDefault(o => o.CanRun)
                                     ?? options.FirstOrDefault();
            UpdateSelectionText();

            Closing += OnWindowClosing;
        }

        /// <summary>下载并部署成功后返回的模型记录。</summary>
        public DeployedModel? DeployedModel { get; private set; }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionText();
        }

        private void UpdateSelectionText()
        {
            if (ModelList.SelectedItem is LocalModelOption option)
            {
                SelectionText.Text = $"已选择：{option.Name} · 下载约 {option.SizeText}";
            }
            else
            {
                SelectionText.Text = "尚未选择模型";
            }
        }

        private void UpdateStoragePath()
        {
            StoragePathText.Text = _modelDirectory;
            StoragePathText.ToolTip = _modelDirectory;
        }

        /// <summary>选择模型存放目录，并持久化到设置里。</summary>
        private void OnChooseFolderClick(object sender, RoutedEventArgs e)
        {
            if (_downloading)
            {
                return;
            }

            var dialog = new OpenFolderDialog
            {
                Title = "选择模型存放目录",
                InitialDirectory = Directory.Exists(_modelDirectory)
                    ? _modelDirectory
                    : AppPaths.ModelsDirectory
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _modelDirectory = dialog.FolderName;
            AppSettingsService.Current.ModelDirectory = _modelDirectory;
            AppSettingsService.Save();
            UpdateStoragePath();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_downloading)
            {
                _downloadCts?.Cancel();
                return;
            }

            DialogResult = false;
        }

        /// <summary>下一步：配置不足先提示，再确认，然后真正开始下载。</summary>
        private async void OnNextClick(object sender, RoutedEventArgs e)
        {
            if (_downloading)
            {
                return;
            }

            if (ModelList.SelectedItem is not LocalModelOption option)
            {
                MessageBox.Show(this, "请先选择一个模型。", "下一步",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!option.CanRun)
            {
                var vramText = _hardware.HasDedicatedGpu
                    ? $"{_hardware.VramGb:0.0} GB"
                    : "无独显";

                var proceed = MessageBox.Show(
                    this,
                    $"当前选中的 {option.Name} 超出本机配置，可能无法加载或运行极慢。\n\n" +
                    $"模型要求：{option.RequirementText}\n" +
                    $"本机配置：显存 {vramText} · 内存 {_hardware.RamGb:0.0} GB\n\n" +
                    "仍要继续吗？",
                    "配置不足",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (proceed != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var confirm = MessageBox.Show(
                this,
                $"确认下载并部署 {option.Name}？\n\n下载体积约 {option.SizeText}，保存到本机后即可启动。",
                "确认部署",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            await DownloadAndDeployAsync(option);
        }

        private async Task DownloadAndDeployAsync(LocalModelOption option)
        {
            _downloading = true;
            _downloadCts = new CancellationTokenSource();

            DownloadPanel.Visibility = Visibility.Visible;
            NextButton.IsEnabled = false;
            CancelButton.Content = "取消下载";
            ProgressTitleText.Text = $"正在下载 {option.Name}";
            DownloadProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            ProgressDetailText.Text = "正在连接下载地址…";
            ProgressSpeedText.Text = string.Empty;

            var targetPath = _deployment.GetTargetPath(option, _modelDirectory);
            var progress = new Progress<DownloadProgress>(value =>
            {
                DownloadProgressBar.Value = value.Percent;
                ProgressPercentText.Text = $"{value.Percent:0}%";
                ProgressDetailText.Text = $"已下载 {value.ReceivedText} / {value.TotalText}";
                ProgressSpeedText.Text = value.SpeedText;
            });

            try
            {
                await ModelDownloadService.DownloadAsync(
                    option, targetPath, progress, _downloadCts.Token);

                var deployed = new DeployedModel
                {
                    ModelName = option.Name,
                    FileName = option.FileName,
                    FilePath = targetPath,
                    SizeBytes = new FileInfo(targetPath).Length,
                    DeployedAt = DateTime.Now
                };

                _deployment.Save(deployed);
                DeployedModel = deployed;

                // 先清掉下载状态，否则 Closing 会拦截由 DialogResult 触发的关闭
                _downloading = false;
                _downloadCts?.Dispose();
                _downloadCts = null;

                DownloadPanel.Visibility = Visibility.Collapsed;
                DialogResult = true;
            }
            catch (OperationCanceledException)
            {
                ResetDownloadUi();
                MessageBox.Show(this, "下载已取消，临时文件已清理。", "取消下载",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ResetDownloadUi();
                MessageBox.Show(this, $"模型下载失败：\n{ex.Message}\n\n可以稍后重试。", "下载失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _downloading = false;
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        private void ResetDownloadUi()
        {
            DownloadPanel.Visibility = Visibility.Collapsed;
            NextButton.IsEnabled = true;
            CancelButton.Content = "取消";
        }

        /// <summary>下载过程中禁止关闭窗口，先取消下载再退出。</summary>
        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_downloading)
            {
                e.Cancel = true;
                _downloadCts?.Cancel();
            }
        }
    }
}
