using System.Globalization;
using System.Net;
using System.Text;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>把一次完整运行记录导出为 Markdown 报告与带图表的 HTML。</summary>
    public static class ReportService
    {
        private static readonly string[] BarColors =
        {
            "#2563eb", "#7c3aed", "#059669", "#d97706", "#dc2626", "#0891b2"
        };

        public static string BuildMarkdown(RunRecord run, IReadOnlyList<RunHistoryEntry> history)
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
            builder.AppendLine("> 折线图、基准表与历史对比柱状图见压缩包内的 `charts.html`。");
            builder.AppendLine();

            AppendHardware(builder, hardware);
            AppendEngineAndModel(builder, run, deployed, device);
            AppendBackend(builder, run.Backend);
            AppendBenchmarks(builder, run.Benchmarks);
            AppendUsage(builder, run);
            AppendSamples(builder, run);
            AppendHistory(builder, history);
            AppendLogs(builder, run.Logs);

            return builder.ToString();
        }

        public static string BuildHtml(RunRecord run, IReadOnlyList<RunHistoryEntry> history)
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
            builder.AppendLine("h2{font-size:15px;margin:26px 0 12px}");
            builder.AppendLine(".meta{color:var(--sub);font-size:12px;line-height:1.8;margin-bottom:18px}");
            builder.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(460px,1fr));gap:16px}");
            builder.AppendLine(".card{background:#fff;border:1px solid var(--line);border-radius:12px;padding:14px 16px;box-shadow:0 2px 10px rgba(24,24,27,.06)}");
            builder.AppendLine(".card-head{display:flex;justify-content:space-between;align-items:center;font-size:13px;font-weight:600;margin-bottom:6px}");
            builder.AppendLine(".muted{color:var(--faint);font-size:11px;font-weight:400}");
            builder.AppendLine("svg{width:100%;height:auto;display:block}");
            builder.AppendLine(".empty{height:180px;display:flex;align-items:center;justify-content:center;color:var(--faint);font-size:12px}");
            builder.AppendLine("table{width:100%;border-collapse:collapse;font-size:12px}");
            builder.AppendLine("th,td{text-align:left;padding:7px 8px;border-bottom:1px solid var(--line);white-space:nowrap}");
            builder.AppendLine("th{color:var(--faint);font-weight:600}");
            builder.AppendLine("code{font-family:Consolas,'Courier New',monospace;font-size:11px;color:var(--sub)}");
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
            builder.AppendLine("<br>");
            builder.AppendLine($"后端：{WebUtility.HtmlEncode(run.Backend?.BackendName ?? "未采集")}");
            builder.AppendLine($" &nbsp;|&nbsp; 层卸载：{WebUtility.HtmlEncode(run.Backend?.LayersText ?? "未记录")}");
            builder.AppendLine($" &nbsp;|&nbsp; 上下文：{FormatContext(run.Backend)}");
            builder.AppendLine($" &nbsp;|&nbsp; SHA256：{HtmlFingerprint(deployed?.Sha256)}");
            if (run.ThrottledRatio is { } ratio)
            {
                var maxClock = MaxValue(run.Samples.Select(s => s.GpuMaxClockMhz));
                builder.AppendLine($"<br>降频估算：{ratio.ToString("0.0", CultureInfo.InvariantCulture)}%（频率低于最高频率 90% 且利用率 &gt; 50%）");
                if (maxClock is { } clock)
                {
                    builder.AppendLine($" &nbsp;|&nbsp; 最高频率上限：{clock.ToString("0", CultureInfo.InvariantCulture)} MHz");
                }
            }

            builder.AppendLine("</div>");

            builder.AppendLine("<h2>实时采样</h2>");
            builder.AppendLine("<div class=\"grid\">");
            builder.AppendLine(BuildChartCard("CPU 占用（占全机）", "%", "#2563eb",
                run.Samples.Select(s => (s.OffsetSeconds, (double?)s.CpuPercent)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("进程内存", "MB", "#7c3aed",
                run.Samples.Select(s => (s.OffsetSeconds, (double?)s.WorkingSetMb)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("GPU 利用率", "%", "#059669",
                run.Samples.Select(s => (s.OffsetSeconds, s.GpuPercent)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("显存占用", "MB", "#d97706",
                run.Samples.Select(s => (s.OffsetSeconds, s.VramUsedMb)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("GPU 温度", "°C", "#dc2626",
                run.Samples.Select(s => (s.OffsetSeconds, s.GpuTemperatureC)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("GPU 功耗", "W", "#0891b2",
                run.Samples.Select(s => (s.OffsetSeconds, s.GpuPowerW)).ToList(), "0.0"));
            builder.AppendLine(BuildChartCard("GPU 频率", "MHz", "#7c3aed",
                run.Samples.Select(s => (s.OffsetSeconds, s.GpuClockMhz)).ToList(), "0"));
            builder.AppendLine("</div>");

            builder.AppendLine("<h2>推理性能基准</h2>");
            builder.AppendLine(BuildBenchmarkCard(run.Benchmarks));

            builder.AppendLine("<h2>历史运行对比</h2>");
            builder.AppendLine(BuildHistoryCharts(history));

            builder.AppendLine("<details><summary>运行日志</summary><pre>");
            foreach (var line in run.Logs)
            {
                builder.AppendLine(WebUtility.HtmlEncode(line));
            }
            builder.AppendLine("</pre></details>");
            builder.AppendLine("</body></html>");
            return builder.ToString();
        }

        private static void AppendHardware(StringBuilder builder, HardwareInfo hardware)
        {
            builder.AppendLine("## 硬件");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 值 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| 处理器 | {hardware.CpuName}（{hardware.LogicalCores} 线程） |");
            builder.AppendLine($"| 内存 | {hardware.RamGb:0.0} GB |");
            builder.AppendLine($"| 显卡 | {hardware.GpuName} |");
            builder.AppendLine($"| 显存 | {(hardware.HasDedicatedGpu ? $"{hardware.VramGb:0.0} GB（独显）" : "核显 / 无独显")} |");
            builder.AppendLine();
        }

        private static void AppendEngineAndModel(
            StringBuilder builder,
            RunRecord run,
            DeployedModel? deployed,
            InferenceDevice device)
        {
            builder.AppendLine("## 引擎与模型");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 值 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| 依赖 | LLamaSharp 0.27.0（CPU + Vulkan 后端） |");
            builder.AppendLine($"| 实际后端 | {run.Backend?.BackendName ?? "未采集"} |");
            builder.AppendLine($"| 推理设备策略 | {DeviceText(device)} |");
            builder.AppendLine($"| 上下文长度 | {FormatContext(run.Backend)} |");
            builder.AppendLine($"| 模型目录 | {AppSettingsService.ResolveModelDirectory()} |");
            if (deployed == null)
            {
                builder.AppendLine("| 模型 | 未记录 |");
            }
            else
            {
                builder.AppendLine($"| 模型 | {deployed.ModelName} |");
                builder.AppendLine($"| 文件 | {deployed.FileName}（{deployed.SizeText}） |");
                builder.AppendLine($"| 参数量 | {ValueOrDash(deployed.Parameters)} |");
                builder.AppendLine($"| 量化 | {ValueOrDash(deployed.Quantization)} |");
                builder.AppendLine($"| SHA256 | {(string.IsNullOrEmpty(deployed.Sha256) ? "未记录（早期下载的模型没有指纹）" : deployed.Sha256)} |");
                builder.AppendLine($"| 路径 | {deployed.FilePath} |");
            }

            builder.AppendLine();
        }

        private static void AppendBackend(StringBuilder builder, BackendInfo? backend)
        {
            builder.AppendLine("## 推理后端指纹");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 值 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| llama.cpp 报告的后端 | {backend?.BackendName ?? "未采集"} |");
            builder.AppendLine($"| 实际使用的设备 | {ValueOrDash(backend?.DeviceName)} |");
            builder.AppendLine($"| 卸载到 GPU 的层数 | {backend?.LayersText ?? "未记录"} |");
            builder.AppendLine($"| 上下文长度 | {FormatContext(backend)} |");
            builder.AppendLine();

            if (backend is { LogLines.Count: > 0 })
            {
                builder.AppendLine("<details><summary>llama.cpp 相关日志（节选）</summary>");
                builder.AppendLine();
                builder.AppendLine("```text");
                foreach (var line in backend.LogLines)
                {
                    builder.AppendLine(line);
                }

                builder.AppendLine("```");
                builder.AppendLine("</details>");
                builder.AppendLine();
            }
        }

        private static void AppendBenchmarks(StringBuilder builder, IReadOnlyList<BenchmarkResult> benchmarks)
        {
            builder.AppendLine("## 推理性能基准");
            builder.AppendLine();
            if (benchmarks.Count == 0)
            {
                builder.AppendLine("本次运行没有完成任何基准（启动后立即停止，或模型未就绪）。");
                builder.AppendLine();
                return;
            }

            builder.AppendLine("| 场景 | 提示字符数 | 输出 tokens | 首 token 延迟 | 生成速度 | 总耗时 | 状态 |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var item in benchmarks)
            {
                builder.AppendLine(
                    $"| {item.Name} | {item.PromptChars} | {item.OutputTokens} | " +
                    $"{item.TimeToFirstTokenMs.ToString("0", CultureInfo.InvariantCulture)} ms | " +
                    $"{item.TokensPerSecond.ToString("0.0", CultureInfo.InvariantCulture)} tok/s | " +
                    $"{item.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s | " +
                    $"{(item.Completed ? "完成" : $"失败：{item.Error}")} |");
            }

            builder.AppendLine();
        }

        private static void AppendUsage(StringBuilder builder, RunRecord run)
        {
            builder.AppendLine("## 占用概览");
            builder.AppendLine();
            builder.AppendLine("| 指标 | 平均 | 峰值 | 最低 |");
            builder.AppendLine("| --- | --- | --- | --- |");
            AppendStatsRow(builder, "CPU（占全机）", run.Samples.Select(s => s.CpuPercent), "%", 2);
            AppendStatsRow(builder, "进程内存", run.Samples.Select(s => s.WorkingSetMb), " MB", 1);
            AppendStatsRow(builder, "GPU 利用率", run.Samples.Where(s => s.GpuPercent.HasValue).Select(s => s.GpuPercent!.Value), "%", 1);
            AppendStatsRow(builder, "显存占用", run.Samples.Where(s => s.VramUsedMb.HasValue).Select(s => s.VramUsedMb!.Value), " MB", 1);
            AppendStatsRow(builder, "GPU 温度", run.Samples.Where(s => s.GpuTemperatureC.HasValue).Select(s => s.GpuTemperatureC!.Value), " °C", 1);
            AppendStatsRow(builder, "GPU 功耗", run.Samples.Where(s => s.GpuPowerW.HasValue).Select(s => s.GpuPowerW!.Value), " W", 1);
            AppendStatsRow(builder, "GPU 频率", run.Samples.Where(s => s.GpuClockMhz.HasValue).Select(s => s.GpuClockMhz!.Value), " MHz", 0);
            builder.AppendLine();

            if (run.ThrottledRatio is { } ratio)
            {
                var maxClock = MaxValue(run.Samples.Select(s => s.GpuMaxClockMhz));
                var clockNote = maxClock is { } clock
                    ? $"，最高频率上限 {clock:0} MHz"
                    : string.Empty;
                builder.AppendLine(
                    $"> 降频估算：{ratio:0.0}% 的采样频率低于最高频率的 90%，同时 GPU 利用率高于 50%{clockNote}。");
                builder.AppendLine();
            }
        }

        private static void AppendSamples(StringBuilder builder, RunRecord run)
        {
            builder.AppendLine("## 采样明细");
            builder.AppendLine();
            builder.AppendLine("| 时间(s) | CPU(%) | 内存(MB) | GPU(%) | 显存(MB) | 温度(°C) | 功耗(W) | 频率(MHz) |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var sample in run.Samples)
            {
                builder.AppendLine(
                    $"| {sample.OffsetSeconds:0.0} | {sample.CpuPercent:0.0} | {sample.WorkingSetMb:0.0} | " +
                    $"{Dash(sample.GpuPercent, "0.0")} | {Dash(sample.VramUsedMb, "0.0")} | " +
                    $"{Dash(sample.GpuTemperatureC, "0.0")} | {Dash(sample.GpuPowerW, "0.0")} | " +
                    $"{Dash(sample.GpuClockMhz, "0")} |");
            }

            builder.AppendLine();
        }

        private static void AppendHistory(StringBuilder builder, IReadOnlyList<RunHistoryEntry> history)
        {
            builder.AppendLine("## 历史运行对比");
            builder.AppendLine();
            if (history.Count == 0)
            {
                builder.AppendLine("暂无历史记录。");
                builder.AppendLine();
                return;
            }

            builder.AppendLine($"共 {history.Count} 次完整运行（保留最近 20 次），" +
                               "速度与首 token 取长 Prompt 基准；下表按时间倒序展示最近 10 次：");
            builder.AppendLine();
            builder.AppendLine("| 时间 | 模型 | 设备 | 时长 | 平均 CPU | 内存峰值 | 显存峰值 | tok/s | 首 token |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var entry in history.Take(10))
            {
                builder.AppendLine(
                    $"| {entry.StartedAt:MM-dd HH:mm} | {entry.ModelLabel} | {entry.DeviceText} | " +
                    $"{FormatDuration(entry.DurationSeconds)} | " +
                    $"{entry.AvgCpuPercent.ToString("0.0", CultureInfo.InvariantCulture)}% | " +
                    $"{entry.PeakMemoryMb.ToString("0", CultureInfo.InvariantCulture)} MB | " +
                    $"{Dash(entry.PeakVramMb, "0")} MB | " +
                    $"{Dash(entry.TokensPerSecond, "0.0")} | " +
                    $"{Dash(entry.FirstTokenMs, "0")} ms |");
            }

            builder.AppendLine();
        }

        private static void AppendLogs(StringBuilder builder, IReadOnlyList<string> logs)
        {
            builder.AppendLine("## 运行日志");
            builder.AppendLine();
            builder.AppendLine("```text");
            foreach (var line in logs)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine("```");
        }

        private static string BuildBenchmarkCard(IReadOnlyList<BenchmarkResult> benchmarks)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<div class=\"card\">");
            builder.AppendLine("<div class=\"card-head\"><span>性能基准</span><span class=\"muted\">启动后自动执行</span></div>");

            if (benchmarks.Count == 0)
            {
                builder.AppendLine("<div class=\"empty\" style=\"height:120px\">本次运行没有完成基准</div>");
                builder.AppendLine("</div>");
                return builder.ToString();
            }

            builder.AppendLine("<table><thead><tr>");
            builder.AppendLine("<th>场景</th><th>提示字符</th><th>输出 tokens</th><th>首 token</th><th>生成速度</th><th>总耗时</th>");
            builder.AppendLine("</tr></thead><tbody>");
            foreach (var item in benchmarks)
            {
                builder.AppendLine("<tr>");
                builder.AppendLine($"<td>{WebUtility.HtmlEncode(item.Name)}</td>");
                builder.AppendLine($"<td>{item.PromptChars}</td>");
                builder.AppendLine($"<td>{item.OutputTokens}</td>");
                builder.AppendLine($"<td>{item.TimeToFirstTokenMs.ToString("0", CultureInfo.InvariantCulture)} ms</td>");
                builder.AppendLine($"<td>{item.TokensPerSecond.ToString("0.0", CultureInfo.InvariantCulture)} tok/s</td>");
                builder.AppendLine($"<td>{item.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s</td>");
                builder.AppendLine("</tr>");
            }

            builder.AppendLine("</tbody></table>");
            builder.AppendLine("</div>");
            return builder.ToString();
        }

        /// <summary>按模型分组的历史柱状图：取每个模型的平均值，方便横向比较。</summary>
        private static string BuildHistoryCharts(IReadOnlyList<RunHistoryEntry> history)
        {
            if (history.Count == 0)
            {
                return "<div class=\"card\"><div class=\"empty\">暂无历史记录</div></div>";
            }

            var groups = history
                .GroupBy(h => h.ModelLabel)
                .Select(g => new
                {
                    Label = g.Key,
                    AvgCpu = g.Average(h => h.AvgCpuPercent),
                    PeakMemory = g.Average(h => h.PeakMemoryMb),
                    PeakVram = Average(g.Select(h => h.PeakVramMb)),
                    TokensPerSecond = Average(g.Select(h => h.TokensPerSecond)),
                    FirstTokenMs = Average(g.Select(h => h.FirstTokenMs))
                })
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("<div class=\"grid\">");
            builder.AppendLine(BuildBarChartCard("平均 CPU（按模型）", "%", "#2563eb",
                groups.Select(g => (g.Label, g.AvgCpu)).ToList(), "0.0"));
            builder.AppendLine(BuildBarChartCard("峰值显存（按模型）", "MB", "#d97706",
                groups.Select(g => (g.Label, g.PeakVram ?? 0)).ToList(), "0"));
            builder.AppendLine(BuildBarChartCard("生成速度（按模型）", "tok/s", "#059669",
                groups.Where(g => g.TokensPerSecond.HasValue)
                    .Select(g => (g.Label, g.TokensPerSecond!.Value)).ToList(), "0.0"));
            builder.AppendLine(BuildBarChartCard("首 token 延迟（按模型）", "ms", "#7c3aed",
                groups.Where(g => g.FirstTokenMs.HasValue)
                    .Select(g => (g.Label, g.FirstTokenMs!.Value)).ToList(), "0"));
            builder.AppendLine("</div>");
            return builder.ToString();
        }

        private static string BuildBarChartCard(
            string title,
            string unit,
            string color,
            IReadOnlyList<(string Label, double Value)> bars,
            string format)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<div class=\"card\">");
            builder.AppendLine($"<div class=\"card-head\"><span>{WebUtility.HtmlEncode(title)}</span><span class=\"muted\">{WebUtility.HtmlEncode(unit)}</span></div>");

            if (bars.Count == 0)
            {
                builder.AppendLine("<div class=\"empty\" style=\"height:160px\">没有可用数据</div>");
                builder.AppendLine("</div>");
                return builder.ToString();
            }

            const int width = 560;
            const int height = 240;
            const int left = 48;
            const int right = 14;
            const int top = 18;
            const int bottom = 44;
            const double plotWidth = width - left - right;
            const double plotHeight = height - top - bottom;

            var yTop = NiceMax(bars.Max(b => b.Value));
            var slot = plotWidth / bars.Count;
            var barWidth = Math.Min(56, slot * 0.52);
            var culture = CultureInfo.InvariantCulture;

            builder.AppendLine($"<svg viewBox=\"0 0 {width} {height}\" role=\"img\">");
            for (var i = 0; i <= 4; i++)
            {
                var y = top + plotHeight * i / 4;
                var value = yTop * (4 - i) / 4;
                builder.AppendLine($"<line x1=\"{left}\" y1=\"{y:0.#}\" x2=\"{width - right}\" y2=\"{y:0.#}\" stroke=\"#ececf1\" stroke-width=\"1\"/>");
                builder.AppendLine($"<text x=\"{left - 8}\" y=\"{y + 4:0.#}\" text-anchor=\"end\" font-size=\"10\" fill=\"#8b8b94\">{value:0.#}</text>");
            }

            for (var i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                var centerX = left + slot * (i + 0.5);
                var barHeight = bar.Value <= 0 ? 0 : bar.Value / yTop * plotHeight;
                var barY = top + plotHeight - barHeight;
                var barColor = BarColors[i % BarColors.Length];

                builder.AppendLine($"<rect x=\"{(centerX - barWidth / 2).ToString("0.##", culture)}\" " +
                                   $"y=\"{barY.ToString("0.##", culture)}\" " +
                                   $"width=\"{barWidth.ToString("0.##", culture)}\" " +
                                   $"height=\"{barHeight.ToString("0.##", culture)}\" " +
                                   $"rx=\"4\" fill=\"{barColor}\"/>");
                builder.AppendLine($"<text x=\"{centerX.ToString("0.##", culture)}\" y=\"{(barY - 6).ToString("0.##", culture)}\" " +
                                   $"text-anchor=\"middle\" font-size=\"10\" fill=\"#52525b\">" +
                                   $"{bar.Value.ToString(format, culture)}{WebUtility.HtmlEncode(unit)}</text>");

                var label = bar.Label.Length > 20 ? bar.Label[..20] + "…" : bar.Label;
                builder.AppendLine($"<text x=\"{centerX.ToString("0.##", culture)}\" y=\"{height - 22}\" " +
                                   $"text-anchor=\"middle\" font-size=\"9\" fill=\"#8b8b94\">" +
                                   $"{WebUtility.HtmlEncode(label)}</text>");
            }

            builder.AppendLine($"<text x=\"8\" y=\"{top + 10}\" font-size=\"10\" fill=\"#8b8b94\">{WebUtility.HtmlEncode(unit)}</text>");
            builder.AppendLine("</svg>");
            builder.AppendLine("</div>");
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
            var culture = CultureInfo.InvariantCulture;
            return $"平均 {avg.ToString(format, culture)}{unit} · 峰值 {max.ToString(format, culture)}{unit}";
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

        private static double? Average(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count > 0 ? list.Average() : null;
        }

        private static double? MaxValue(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count > 0 ? list.Max() : null;
        }

        private static string Dash(double? value, string format)
        {
            return value.HasValue
                ? value.Value.ToString(format, CultureInfo.InvariantCulture)
                : "-";
        }

        private static string ValueOrDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string FormatContext(BackendInfo? backend)
        {
            return backend is { ContextSize: > 0 }
                ? $"{backend.ContextSize} tokens"
                : "未记录";
        }

        private static string HtmlFingerprint(string? sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256))
            {
                return "未记录";
            }

            var shortText = sha256.Length > 16 ? $"{sha256[..16]}…" : sha256;
            return $"<code title=\"{WebUtility.HtmlEncode(sha256)}\">{WebUtility.HtmlEncode(shortText)}</code>";
        }

        private static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分 {span.Seconds} 秒"
                : $"{span.Minutes} 分 {span.Seconds} 秒";
        }

        /// <summary>推理设备策略的展示文本，历史对比与报告共用。</summary>
        public static string DeviceText(InferenceDevice device)
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
