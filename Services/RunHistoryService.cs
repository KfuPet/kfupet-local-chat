using System.IO;
using System.Text.Json;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>一次完整运行的摘要，持久化在 Config\runhistory.json，用于跨模型、跨次对比。</summary>
    public sealed class RunHistoryEntry
    {
        public DateTime StartedAt { get; set; }

        public string ModelName { get; set; } = "未记录";

        public string? Parameters { get; set; }

        public string? Quantization { get; set; }

        public string DeviceText { get; set; } = "未记录";

        public double DurationSeconds { get; set; }

        public double AvgCpuPercent { get; set; }

        public double PeakMemoryMb { get; set; }

        public double? AvgGpuPercent { get; set; }

        public double? PeakVramMb { get; set; }

        public double? TokensPerSecond { get; set; }

        public double? FirstTokenMs { get; set; }

        public double? PeakTemperatureC { get; set; }

        public double? PeakPowerW { get; set; }

        /// <summary>模型名称 + 参数量，报告分组时使用。</summary>
        public string ModelLabel => string.IsNullOrWhiteSpace(Parameters)
            ? ModelName
            : $"{ModelName}（{Parameters}）";

        /// <summary>把一次运行压缩成摘要：数值取有效采样的平均值 / 峰值，速度取长 Prompt 基准。</summary>
        public static RunHistoryEntry FromRun(RunRecord run)
        {
            var gpuValues = run.Samples
                .Where(s => s.GpuPercent.HasValue)
                .Select(s => s.GpuPercent!.Value)
                .ToList();

            // 统一用长 Prompt 基准做横向对比；只跑了短句时退回任意已完成项
            var representative = run.Benchmarks.FirstOrDefault(b =>
                                      b.Completed && b.Name == PerformanceBenchmark.LongName)
                                  ?? run.Benchmarks.FirstOrDefault(b => b.Completed);

            return new RunHistoryEntry
            {
                StartedAt = run.StartedAt,
                ModelName = run.Model?.ModelName ?? "未记录",
                Parameters = run.Model?.Parameters,
                Quantization = run.Model?.Quantization,
                DeviceText = ReportService.DeviceText(run.Device),
                DurationSeconds = run.DurationSeconds,
                AvgCpuPercent = run.Samples.Count > 0 ? run.Samples.Average(s => s.CpuPercent) : 0,
                PeakMemoryMb = run.Samples.Count > 0 ? run.Samples.Max(s => s.WorkingSetMb) : 0,
                AvgGpuPercent = gpuValues.Count > 0 ? gpuValues.Average() : null,
                PeakVramMb = Peak(run.Samples.Select(s => s.VramUsedMb)),
                TokensPerSecond = representative?.TokensPerSecond,
                FirstTokenMs = representative?.TimeToFirstTokenMs,
                PeakTemperatureC = Peak(run.Samples.Select(s => s.GpuTemperatureC)),
                PeakPowerW = Peak(run.Samples.Select(s => s.GpuPowerW))
            };
        }

        private static double? Peak(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count > 0 ? list.Max() : null;
        }
    }

    /// <summary>读写运行历史：只保留最近 20 次，供报告做模型对比。</summary>
    public static class RunHistoryService
    {
        private const int MaxEntries = 20;

        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static IReadOnlyList<RunHistoryEntry> Load()
        {
            lock (Sync)
            {
                return Read().OrderByDescending(e => e.StartedAt).ToList();
            }
        }

        /// <summary>追加一次摘要，超出容量时丢弃最旧的记录。</summary>
        public static void Append(RunHistoryEntry entry)
        {
            lock (Sync)
            {
                var entries = Read();
                entries.Add(entry);
                entries = entries
                    .OrderByDescending(e => e.StartedAt)
                    .Take(MaxEntries)
                    .OrderBy(e => e.StartedAt)
                    .ToList();
                Write(entries);
            }
        }

        /// <summary>删除全部运行历史；全量清除时使用。</summary>
        public static void Clear()
        {
            lock (Sync)
            {
                try
                {
                    if (File.Exists(AppPaths.RunHistoryPath))
                    {
                        File.Delete(AppPaths.RunHistoryPath);
                    }
                }
                catch
                {
                    // 删除失败时下次读取仍会拿到旧数据
                }
            }
        }

        private static List<RunHistoryEntry> Read()
        {
            try
            {
                if (!File.Exists(AppPaths.RunHistoryPath))
                {
                    return new List<RunHistoryEntry>();
                }

                var entries = JsonSerializer.Deserialize<List<RunHistoryEntry>>(
                    File.ReadAllText(AppPaths.RunHistoryPath), JsonOptions);
                return entries ?? new List<RunHistoryEntry>();
            }
            catch
            {
                // 历史文件损坏时按空处理，不影响本次导出
                return new List<RunHistoryEntry>();
            }
        }

        private static void Write(List<RunHistoryEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                File.WriteAllText(AppPaths.RunHistoryPath,
                    JsonSerializer.Serialize(entries, JsonOptions));
            }
            catch
            {
                // 历史写入失败不影响导出（当前运行仍会出现在报告里）
            }
        }
    }
}
