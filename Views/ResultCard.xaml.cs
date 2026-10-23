using System.Windows.Controls;

namespace kfupet_local_chat.Views
{
    /// <summary>本地模型输出卡片：展示结果与指标，目前为占位状态。</summary>
    public partial class ResultCard : UserControl
    {
        public ResultCard()
        {
            InitializeComponent();
        }

        /// <summary>恢复到未运行状态。</summary>
        public void ResetResult()
        {
            StatusText.Text = "尚未运行";
            EmptyHint.Text = "输出将显示在这里";
            JsonRateText.Text = "—";
            ItemCountText.Text = "—";
            ElapsedText.Text = "—";
        }
    }
}
