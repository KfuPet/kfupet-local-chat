using System.Windows;
using System.Windows.Controls;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Views
{
    /// <summary>本地模型输出卡片：展示归档提炼的信息点与指标。</summary>
    public partial class ResultCard : UserControl
    {
        public ResultCard()
        {
            InitializeComponent();
        }

        /// <summary>开始提炼时先清空旧结果，避免和上一次的数据混在一起。</summary>
        public void ShowBusy()
        {
            StatusText.Text = "正在归档提炼…";
            JsonRateText.Text = "—";
            ItemCountText.Text = "—";
            ElapsedText.Text = "—";
            ResultList.ItemsSource = null;
            ResultScroll.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyHint.Text = "正在调用本地模型，请稍候…";
        }

        /// <summary>展示一次归档提炼的结果：信息点列表 + JSON 合法率 / 条数 / 耗时。</summary>
        public void ShowArchiveResult(ArchiveAnalysisResult result)
        {
            if (!result.Succeeded)
            {
                ShowError(result.Message ?? "归档提炼失败");
                return;
            }

            StatusText.Text = $"归档提炼完成 · 写入 {result.StoredCount} 条";
            JsonRateText.Text = result.ConversationCount > 0
                ? (result.ExtractionParsed ? "100%" : "0%")
                : "—";
            ItemCountText.Text = result.ExtractedCount.ToString();
            ElapsedText.Text = $"{result.Seconds:0.0} s";

            var items = result.Items.ToList();
            ResultList.ItemsSource = items;
            ResultScroll.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            EmptyHint.Text = "本轮没有提炼出信息点";
        }

        public void ShowError(string message)
        {
            StatusText.Text = "归档提炼失败";
            JsonRateText.Text = "—";
            ItemCountText.Text = "—";
            ElapsedText.Text = "—";
            ResultList.ItemsSource = null;
            ResultScroll.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyHint.Text = message;
        }

        /// <summary>恢复到未运行状态。</summary>
        public void ResetResult()
        {
            StatusText.Text = "尚未运行";
            JsonRateText.Text = "—";
            ItemCountText.Text = "—";
            ElapsedText.Text = "—";
            ResultList.ItemsSource = null;
            ResultScroll.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyHint.Text = "输出将显示在这里";
        }
    }
}
