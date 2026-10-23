using System.Windows.Controls;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Views
{
    /// <summary>本地推理引擎卡片：固定为 LLamaSharp，并提供硬件检测入口。</summary>
    public partial class EngineCard : UserControl
    {
        public EngineCard()
        {
            InitializeComponent();
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
