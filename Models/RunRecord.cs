namespace kfupet_local_chat.Models
{
    /// <summary>一次采样的资源占用快照。</summary>
    public sealed record PerformanceSample(
        double OffsetSeconds,
        double CpuPercent,
        double WorkingSetMb,
        double? GpuPercent,
        double? VramUsedMb);

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

        public double DurationSeconds => Math.Max(0, (EndedAt - StartedAt).TotalSeconds);

        public bool HasGpuSamples => Samples.Any(s => s.GpuPercent.HasValue);
    }
}
