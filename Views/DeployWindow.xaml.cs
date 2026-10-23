using System.Windows;
using System.Windows.Controls;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Views
{
    /// <summary>模型选择窗口：展示本机配置与三档模型；配置不足的模型在“下一步”时二次确认。</summary>
    public partial class DeployWindow : Window
    {
        private readonly HardwareInfo _hardware;

        public DeployWindow(HardwareInfo hardware, IReadOnlyList<LocalModelOption> options)
        {
            InitializeComponent();
            _hardware = hardware;

            HwGpuText.Text = hardware.GpuName;
            HwVramText.Text = hardware.HasDedicatedGpu
                ? $"独显显存 {hardware.VramGb:0.0} GB"
                : "核显 / 无独显，CPU 推理";
            HwRamText.Text = $"{hardware.RamGb:0.0} GB";
            HwCpuText.Text = $"{hardware.LogicalCores} 线程";

            ModelList.ItemsSource = options;
            ModelList.SelectedItem = options.FirstOrDefault(o => o.IsRecommended)
                                     ?? options.FirstOrDefault(o => o.CanRun)
                                     ?? options.FirstOrDefault();
            UpdateSelectionText();
        }

        /// <summary>用户确认后选中的模型名，供调用方记录。</summary>
        public string? ConfirmedModelName { get; private set; }

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

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>下一步：配置不足先提示是否继续，确认后再进入部署确认（当前为界面空壳）。</summary>
        private void OnNextClick(object sender, RoutedEventArgs e)
        {
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
                $"确认部署 {option.Name}？\n\n下载体积约 {option.SizeText}\n\n当前仅为界面确认，下载与加载逻辑尚未接入。",
                "确认部署",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            ConfirmedModelName = option.Name;
            DialogResult = true;
        }
    }
}
