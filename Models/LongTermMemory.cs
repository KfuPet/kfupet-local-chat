namespace kfupet_local_chat.Models
{
    /// <summary>一条长期记忆：内容与重要性（0~5），持久化在 Config\memories.json。</summary>
    public sealed class LongTermMemory
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Content { get; set; } = string.Empty;

        /// <summary>重要性 0~5；3 及以上才值得长期保留。</summary>
        public int Importance { get; set; } = 3;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string ImportanceText => $"重要性 {Importance}";

        public string TimeText => CreatedAt.ToString("MM-dd HH:mm");
    }
}
