namespace kfupet_local_chat.Models
{
    /// <summary>一次采样的资源占用快照；GPU 相关字段在没有 NVIDIA 显卡时为空。</summary>
    public sealed record PerformanceSample(
        double OffsetSeconds,
        double CpuPercent,
        double WorkingSetMb,
        double? GpuPercent,
        double? VramUsedMb,
        double? GpuTemperatureC = null,
        double? GpuPowerW = null,
        double? GpuClockMhz = null,
        double? GpuMaxClockMhz = null);

    /// <summary>一次「启动 → 停止」完整运行的记录，导出报告时以它为准。</summary>
    public sealed class RunRecord
    {
        public required DateTime StartedAt { get; init; }

        public required DateTime EndedAt { get; init; }

        public required HardwareInfo Hardware { get; init; }

        public required DeployedModel? Model { get; init; }

        public required InferenceDevice Device { get; init; }

        public required IReadOnlyList<PerformanceSample> Samples { get; init; }

        public required IReadOnlyList<string> Logs { get; init; }

        /// <summary>启动后自动跑的性能基准结果；中途停止时只保留已完成项。</summary>
        public required IReadOnlyList<BenchmarkResult> Benchmarks { get; init; }

        /// <summary>从 llama.cpp 日志还原的后端信息；拿不到原生日志时为空。</summary>
        public required BackendInfo? Backend { get; init; }

        public double DurationSeconds => Math.Max(0, (EndedAt - StartedAt).TotalSeconds);

        public bool HasGpuSamples => Samples.Any(s => s.GpuPercent.HasValue);

        /// <summary>GPU 频率明显低于最高频率、且仍在高负载运行的采样占比（估算降频情况）。</summary>
        public double? ThrottledRatio
        {
            get
            {
                var clocked = Samples
                    .Where(s => s.GpuClockMhz.HasValue && s.GpuMaxClockMhz is > 0)
                    .ToList();
                if (clocked.Count == 0)
                {
                    return null;
                }

                var throttled = clocked.Count(s =>
                    s.GpuPercent is > 50 && s.GpuClockMhz < s.GpuMaxClockMhz * 0.9);
                return throttled * 100d / clocked.Count;
            }
        }
    }
}
