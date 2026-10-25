namespace kfupet_local_chat.Models
{
    /// <summary>归档提炼结果中的一条信息点。</summary>
    public sealed record ArchiveResultItem(string Content, string Badge, string Status);

    /// <summary>一次归档提炼的汇总结果，用于结果卡片与日志。</summary>
    public sealed class ArchiveAnalysisResult
    {
        public bool Succeeded { get; init; }

        public string? Message { get; init; }

        public int TotalEntries { get; init; }

        /// <summary>参与批量提炼的原始对话条数（已剔除实时通道处理过的）。</summary>
        public int ConversationCount { get; init; }

        /// <summary>因实时通道已入库而跳过的对话条数。</summary>
        public int SkippedRealtimeCount { get; init; }

        public int SummaryCount { get; init; }

        /// <summary>提炼出的信息点总数（含未达重要性阈值的）。</summary>
        public int ExtractedCount { get; init; }

        /// <summary>重要性达标的条数。</summary>
        public int PromotedCount { get; init; }

        /// <summary>真正新写入长期记忆的条数。</summary>
        public int StoredCount { get; init; }

        public int LowCount { get; init; }

        public string? SummaryText { get; init; }

        /// <summary>对话批量提炼是否返回了可解析的 JSON。</summary>
        public bool ExtractionParsed { get; init; }

        public double Seconds { get; init; }

        public IReadOnlyList<ArchiveResultItem> Items { get; init; } = Array.Empty<ArchiveResultItem>();

        public static ArchiveAnalysisResult Failed(string message)
        {
            return new ArchiveAnalysisResult { Succeeded = false, Message = message };
        }
    }
}
