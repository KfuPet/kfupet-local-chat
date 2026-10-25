namespace kfupet_local_chat.Models
{
    /// <summary>一条归档记忆：原始对话（conversation）或总结条目（summary）。</summary>
    public sealed class ArchiveMemoryEntry
    {
        public string Type { get; set; } = "conversation";

        public DateTime Time { get; set; } = DateTime.Now;

        public string User { get; set; } = string.Empty;

        public string Assistant { get; set; } = string.Empty;

        /// <summary>总结正文（summary 类型使用）。</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>重要性 0~5（summary 类型使用）。</summary>
        public int Importance { get; set; }

        public bool IsSummary { get; set; }

        /// <summary>该轮是否已由实时通道提炼入库；为 true 时归档分析直接跳过。</summary>
        public bool RealtimeExtracted { get; set; }

        /// <summary>是不是一条待提炼的原始对话。</summary>
        public bool IsConversation => !IsSummary &&
                                      !string.Equals(Type, "summary", StringComparison.OrdinalIgnoreCase);
    }
}
