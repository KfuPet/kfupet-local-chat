namespace kfupet_local_chat.Models
{
    /// <summary>一次推理的耗时指标：首 token 延迟、生成阶段耗时与总耗时。</summary>
    public sealed record InferenceMetrics(
        string Text,
        int OutputTokens,
        double TimeToFirstTokenMs,
        double GenerationSeconds,
        double TotalSeconds)
    {
        public double TokensPerSecond => GenerationSeconds > 0.0001
            ? OutputTokens / GenerationSeconds
            : 0;
    }

    /// <summary>报告里的一条性能基准结果。</summary>
    public sealed record BenchmarkResult(
        string Name,
        int PromptChars,
        int OutputTokens,
        double TimeToFirstTokenMs,
        double TokensPerSecond,
        double TotalSeconds,
        bool Completed,
        string? Error = null);
}
