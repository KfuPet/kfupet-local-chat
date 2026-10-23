using System.Globalization;
using System.Net;
using System.Text;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>把一次完整运行记录导出为 Markdown 报告与带折线图的 HTML。</summary>
    public static class ReportService
    {
        public static string BuildMarkdown(RunRecord run)
        {
            var hardware = run.Hardware;
            var deployed = run.Model;
            var device = run.Device;
            var builder = new StringBuilder();
            builder.AppendLine("# KfuPet 本地模型运行报告");
            builder.AppendLine();
            builder.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- 程序版本：{typeof(ReportService).Assembly.GetName().Version?.ToString(3) ?? "未知"}");
            builder.AppendLine($"- 运行开始：{run.StartedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- 运行结束：{run.EndedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- 运行时长：{FormatDuration(run.DurationSeconds)}");
            builder.AppendLine();
            builder.AppendLine("> 折线图见压缩包内的 `charts.html`。");
            builder.AppendLine();

            builder.AppendLine("## 硬件");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 值 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| 处理器 | {hardware.CpuName}（{hardware.LogicalCores} 线程） |");
            builder.AppendLine($"| 内存 | {hardware.RamGb:0.0} GB |");
            builder.AppendLine($"| 显卡 | {hardware.GpuName} |");
            builder.AppendLine($"| 显存 | {(hardware.HasDedicatedGpu ? $"{hardware.VramGb:0.0} GB（独显）" : "核显 / 无独显")} |");
            builder.AppendLine();

            builder.AppendLine("## 引擎与模型");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 值 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| 后端 | LLamaSharp 0.27.0（CPU + Vulkan） |");
            builder.AppendLine($"| 推理设备策略 | {DeviceText(device)} |");
            builder.AppendLine($"| 模型目录 | {AppSettingsService.ResolveModelDirectory()} |");
            if (deployed == null)
            {
                builder.AppendLine("| 模型 | 未记录 |");
            }
            else
            {
                builder.AppendLine($"| 模型 | {deployed.ModelName} |");
                builder.AppendLine($"| 文件 | {deployed.FileName}（{deployed.SizeText}） |");
                builder.AppendLine($"| 路径 | {deployed.FilePath} |");
            }
            builder.AppendLine();

            builder.AppendLine("## 占用概览");
            builder.AppendLine();
            builder.AppendLine("| 指标 | 平均 | 峰值 | 最低 |");
            builder.AppendLine("| --- | --- | --- | --- |");
            AppendStatsRow(builder, "CPU（占全机）", run.Samples.Select(s => s.CpuPercent), "%", 2);
            AppendStatsRow(builder, "进程内存", run.Samples.Select(s => s.WorkingSetMb), " MB", 1);
            AppendStatsRow(builder, "GPU 利用率", run.Samples.Where(s => s.GpuPercent.HasValue).Select(s => s.GpuPercent!.Value), "%", 1);
            AppendStatsRow(builder, "显存占用", run.Samples.Where(s => s.VramUsedMb.HasValue).Select(s => s.VramUsedMb!.Value), " MB", 1);
            builder.AppendLine();

            builder.AppendLine("## 采样明细");
            builder.AppendLine();
            builder.AppendLine("| 时间(s) | CPU(%) | 内存(MB) | GPU(%) | 显存(MB) |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var sample in run.Samples)
            {
                builder.AppendLine(
                    $"| {sample.OffsetSeconds:0.0} | {sample.CpuPercent:0.0} | {sample.WorkingSetMb:0.0} | " +
                    $"{(sample.GpuPercent.HasValue ? sample.GpuPercent.Value.ToString("0.0") : "-")} | " +
                    $"{(sample.VramUsedMb.HasValue ? sample.VramUsedMb.Value.ToString("0.0") : "-")} |");
            }
            builder.AppendLine();

            builder.AppendLine("## 运行日志");
            builder.AppendLine();
            builder.AppendLine("```text");
            foreach (var line in run.Logs)
            {
                builder.AppendLine(line);
            }
            builder.AppendLine("```");

            return builder.ToString();
        }

        public static string BuildHtml(RunRecord run)
        {
            var hardware = run.Hardware;
            var deployed = run.Model;
            var device = run.Device;
            var builder = new StringBuilder();
            builder.AppendLine("<!DOCTYPE html>");
            builder.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            builder.AppendLine("<title>KfuPet 本地模型运行图表</title>");
            builder.AppendLine("<style>");
            builder.AppendLine(":root{--ink:#18181b;--sub:#52525b;--faint:#8b8b94;--line:#e4e4e7;--soft:#f4f4f5;--accent:#2563eb}");
            builder.AppendLine("*{box-sizing:border-box}");
            builder.AppendLine("body{margin:0;padding:28px;background:#f8f9fb;color:var(--ink);font-family:'Microsoft YaHei UI','Segoe UI',sans-serif}");
            builder.AppendLine("h1{font-size:20px;margin:0 0 6px}");
            builder.AppendLine(".meta{color:var(--sub);font-size:12px;line-height:1.8;margin-bottom:18px}");
            builder.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(460px,1fr));gap:16px}");
            builder.AppendLine(".card{background:#fff;border:1px solid var(--line);border-radius:12px;padding:14px 16px;box-shadow:0 2px 10px rgba(24,24,27,.06)}");
            builder.AppendLine(".card-head{display:flex;justify-content:space-between;align-items:center;font-size:13px;font-weight:600;margin-bottom:6px}");
            builder.AppendLine(".muted{color:var(--faint);font-size:11px;font-weight:400}");
            builder.AppendLine("svg{width:100%;height:auto;display:block}");
            builder.AppendLine(".empty{height:180px;display:flex;align-items:center;justify-content:center;color:var(--faint);font-size:12px}");
            builder.AppendLine("details{margin-top:18px;background:#fff;border:1px solid var(--line);border-radius:12px;padding:12px 16px}");
            builder.AppendLine("summary{cursor:pointer;font-size:13px;font-weight:600}");
            builder.AppendLine("pre{max-height:420px;overflow:auto;font-size:11px;line-height:1.7;color:var(--sub);white-space:pre-wrap}");
            builder.AppendLine("</style></head><body>");
            builder.AppendLine("<h1>KfuPet 本地模型运行图表</h1>");

            builder.AppendLine("<div class=\"meta\">");
            builder.AppendLine($"运行时间：{run.StartedAt:yyyy-MM-dd HH:mm:ss} ~ {run.EndedAt:HH:mm:ss}（{FormatDuration(run.DurationSeconds)}）<br>");
            builder.AppendLine($"模型：{WebUtility.HtmlEncode(deployed?.ModelName ?? "未记录")}");
            builder.AppendLine($" &nbsp;|&nbsp; 显卡：{WebUtility.HtmlEncode(hardware.GpuName)}");
            builder.AppendLine($" &nbsp;|&nbsp; 设备策略：{DeviceText(device)}");
            builder.AppendLine("</div>");

            builder.AppendLine("<div class=\"grid\">");
            builder.AppendLine(BuildChartCard("CPU 占用（占全机）", "%", "#2563eb",
                run.Samples.Select(s => (s.OffsetSeconds, (double?)s.CpuPercent)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("进程内存", "MB", "#7c3aed",
                run.Samples.Select(s => (s.OffsetSeconds, (double?)s.WorkingSetMb)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("GPU 利用率", "%", "#059669",
                run.Samples.Select(s => (s.OffsetSeconds, s.GpuPercent)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("显存占用", "MB", "#d97706",
                run.Samples.Select(s => (s.OffsetSeconds, s.VramUsedMb)).ToList(), "0.0"));
            builder.AppendLine("</div>");

            builder.AppendLine("<details><summary>运行日志</summary><pre>");
            foreach (var line in run.Logs)
            {
                builder.AppendLine(WebUtility.HtmlEncode(line));
            }
            builder.AppendLine("</pre></details>");
            builder.AppendLine("</body></html>");
            return builder.ToString();
        }

        private static string BuildChartCard(
            string title,
            string unit,
            string color,
            IReadOnlyList<(double X, double? Y)> points,
            string format)
        {
            var valid = points.Where(p => p.Y.HasValue).Select(p => (p.X, p.Y!.Value)).ToList();
            var summary = BuildSummaryText(valid, unit, format);

            var builder = new StringBuilder();
            builder.AppendLine("<div class=\"card\">");
            builder.AppendLine($"<div class=\"card-head\"><span>{WebUtility.HtmlEncode(title)}</span><span class=\"muted\">{summary}</span></div>");

            if (valid.Count == 0)
            {
                builder.AppendLine("<div class=\"empty\">没有可用数据（未检测到 NVIDIA 显卡）</div>");
                builder.AppendLine("</div>");
                return builder.ToString();
            }

            const int width = 560;
            const int height = 220;
            const int left = 54;
            const int right = 14;
            const int top = 14;
            const int bottom = 26;
            const double plotWidth = width - left - right;
            const double plotHeight = height - top - bottom;

            var maxX = Math.Max(1, valid.Max(p => p.X));
            var yTop = NiceMax(valid.Max(p => p.Value));
            var culture = CultureInfo.InvariantCulture;

            builder.AppendLine($"<svg viewBox=\"0 0 {width} {height}\" role=\"img\">");
            for (var i = 0; i <= 4; i++)
            {
                var y = top + plotHeight * i / 4;
                var value = yTop * (4 - i) / 4;
                builder.AppendLine($"<line x1=\"{left}\" y1=\"{y:0.#}\" x2=\"{width - right}\" y2=\"{y:0.#}\" stroke=\"#ececf1\" stroke-width=\"1\"/>");
                builder.AppendLine($"<text x=\"{left - 8}\" y=\"{y + 4:0.#}\" text-anchor=\"end\" font-size=\"10\" fill=\"#8b8b94\">{value:0.#}</text>");
            }

            var polyline = string.Join(" ", valid.Select(p =>
            {
                var x = left + p.X / maxX * plotWidth;
                var y = top + plotHeight - p.Value / yTop * plotHeight;
                return $"{x.ToString("0.##", culture)},{y.ToString("0.##", culture)}";
            }));
            builder.AppendLine($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"2.5\" stroke-linejoin=\"round\" points=\"{polyline}\"/>");

            builder.AppendLine($"<text x=\"{left}\" y=\"{height - 8}\" font-size=\"10\" fill=\"#8b8b94\">0s</text>");
            builder.AppendLine($"<text x=\"{width - right}\" y=\"{height - 8}\" text-anchor=\"end\" font-size=\"10\" fill=\"#8b8b94\">{maxX:0} s</text>");
            builder.AppendLine($"<text x=\"8\" y=\"{top + 10}\" font-size=\"10\" fill=\"#8b8b94\">{WebUtility.HtmlEncode(unit)}</text>");
            builder.AppendLine("</svg>");
            builder.AppendLine("</div>");
            return builder.ToString();
        }

        private static string BuildSummaryText(List<(double X, double Value)> values, string unit, string format)
        {
            var avg = values.Average(p => p.Value);
            var max = values.Max(p => p.Value);
            return $"平均 {avg.ToString(format, CultureInfo.InvariantCulture)}{unit} · 峰值 {max.ToString(format, CultureInfo.InvariantCulture)}{unit}";
        }

        private static double NiceMax(double value)
        {
            if (value <= 0)
            {
                return 1;
            }

            var exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
            var fraction = value / exponent;
            var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
            return nice * exponent;
        }

        private static void AppendStatsRow(
            StringBuilder builder,
            string label,
            IEnumerable<double> values,
            string unit,
            int decimals)
        {
            var list = values.ToList();
            if (list.Count == 0)
            {
                builder.AppendLine($"| {label} | - | - | - |");
                return;
            }

            builder.AppendLine(
                $"| {label} | {list.Average().ToString($"F{decimals}")}{unit} | " +
                $"{list.Max().ToString($"F{decimals}")}{unit} | {list.Min().ToString($"F{decimals}")}{unit} |");
        }

        private static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分 {span.Seconds} 秒"
                : $"{span.Minutes} 分 {span.Seconds} 秒";
        }

        private static string DeviceText(InferenceDevice device)
        {
            return device switch
            {
                InferenceDevice.Gpu => "GPU 加速",
                InferenceDevice.Cpu => "纯 CPU",
                _ => "自动（按显存判断）"
            };
        }
    }
}
