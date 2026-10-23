using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using kfupet_local_chat.Models;
using kfupet_local_chat.Services;

namespace kfupet_local_chat.Views
{
    /// <summary>本地推理引擎卡片：下载入口、推理设备选择，以及部署后的卸载管理。</summary>
    public partial class EngineCard : UserControl
    {
        private static readonly Brush StoppedDotBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
        private static readonly Brush RunningDotBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

        private bool _updatingModels;

        public EngineCard()
        {
            InitializeComponent();
            DeviceCombo.SelectedIndex = (int)AppSettingsService.Current.Device;
        }

        /// <summary>用户切换了要使用的模型；由主窗口决定是否允许切换。</summary>
        public event Action<string>? ModelSwitchRequested;

        /// <summary>用户选择的推理设备。</summary>
        public InferenceDevice SelectedDevice => DeviceCombo.SelectedIndex switch
        {
            1 => InferenceDevice.Gpu,
            2 => InferenceDevice.Cpu,
            _ => InferenceDevice.Auto
        };

        private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
        {
            AppSettingsService.Current.Device = SelectedDevice;
            AppSettingsService.Save();
        }

        /// <summary>还没部署模型：只隐藏管理区，下载按钮始终保留。</summary>
        public void ShowDownloadMode()
        {
            ManagePanel.Visibility = System.Windows.Visibility.Collapsed;
        }

        /// <summary>刷新已下载模型列表，并选中当前使用的模型。</summary>
        public void ShowModels(IReadOnlyList<DeployedModel> models, DeployedModel active)
        {
            ManagePanel.Visibility = System.Windows.Visibility.Visible;
            _updatingModels = true;
            ModelCombo.ItemsSource = models;
            ModelCombo.SelectedItem = models.FirstOrDefault(m => m.FileName == active.FileName);
            _updatingModels = false;

            ModelSizeText.Text = active.SizeText;
            UpdateRuntime(false, false, "已停止");
        }

        private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingModels)
            {
                return;
            }

            if (ModelCombo.SelectedItem is DeployedModel model)
            {
                ModelSwitchRequested?.Invoke(model.FileName);
            }
        }

        /// <summary>下拉框收起时吞掉滚轮，避免鼠标经过就切换选项；展开时保留滚轮浏览。</summary>
        private void OnComboPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ComboBox { IsDropDownOpen: true })
            {
                return;
            }

            e.Handled = true;
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
