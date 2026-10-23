using System.Windows.Controls;

namespace kfupet_local_chat.Views
{
    /// <summary>操作条：运行 / 停止本地模型由同一个按钮切换，另含导出与清空。</summary>
    public partial class ActionBar : UserControl
    {
        public ActionBar()
        {
            InitializeComponent();
        }

        /// <summary>完整跑完一次（启动后停止）才允许导出报告。</summary>
        public void SetExportEnabled(bool enabled)
        {
            ExportButton.IsEnabled = enabled;
        }

        /// <summary>刷新运行按钮：未部署时禁用，运行中显示“停止本地”，加载 / 停止 / 卸载过程中显示进行态。</summary>
        public void UpdateRunState(bool hasModel, bool running, bool busy, string? busyText = null)
        {
            if (busy)
            {
                RunLocalButton.IsEnabled = false;
                RunLocalButton.Content = busyText ?? (running ? "正在停止…" : "正在加载…");
                RunLocalButton.Tag = running ? "停止本地" : "运行本地";
                return;
            }

            RunLocalButton.IsEnabled = hasModel;
            RunLocalButton.Content = running ? "停止本地" : "运行本地";
            RunLocalButton.Tag = running ? "停止本地" : "运行本地";
        }
    }
}
