using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Views
{
    /// <summary>本地推理引擎卡片：下载入口，以及部署后的启动 / 结束 / 卸载管理。</summary>
    public partial class EngineCard : UserControl
    {
        private static readonly Brush StoppedDotBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
        private static readonly Brush RunningDotBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

        public EngineCard()
        {
            InitializeComponent();
        }

        /// <summary>还没部署模型：只显示下载入口。</summary>
        public void ShowDownloadMode()
        {
            DownloadButton.Visibility = System.Windows.Visibility.Visible;
            ManagePanel.Visibility = System.Windows.Visibility.Collapsed;
        }

        /// <summary>已部署模型：切换到启动 / 结束 / 卸载。</summary>
        public void ShowDeployedModel(DeployedModel model)
        {
            DownloadButton.Visibility = System.Windows.Visibility.Collapsed;
            ManagePanel.Visibility = System.Windows.Visibility.Visible;
            ModelNameText.Text = model.ModelName;
            ModelSizeText.Text = model.SizeText;
            UpdateRuntime(false, false, "已停止");
        }

        /// <summary>刷新运行状态；启动 / 停止由操作栏统一控制，这里只更新状态与卸载可用性。</summary>
        public void UpdateRuntime(bool running, bool busy, string status)
        {
            RuntimeStatusText.Text = status;
            StateDot.Fill = running ? RunningDotBrush : StoppedDotBrush;
            UninstallButton.IsEnabled = !busy;
        }

        /// <summary>把检测结果写进卡片，供用户确认推荐依据。</summary>
        public void ShowHardware(HardwareInfo hardware)
        {
            HwGpuText.Text = hardware.GpuName;
            HwVramText.Text = hardware.HasDedicatedGpu
                ? $"独显显存 {hardware.VramGb:0.0} GB"
                : "核显 / 未识别到独显，将使用 CPU";
            HwSysText.Text = $"内存 {hardware.RamGb:0.0} GB · {hardware.LogicalCores} 线程";
        }
    }
}
