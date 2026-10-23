namespace kfupet_local_chat.Models
{
    /// <summary>已部署到本机的模型记录，持久化在 Config\deployed.json。</summary>
    public sealed class DeployedModel
    {
        public required string ModelName { get; init; }

        public required string FileName { get; init; }

        public required string FilePath { get; init; }

        public required long SizeBytes { get; init; }

        public required DateTime DeployedAt { get; init; }

        public string SizeText => $"{SizeBytes / 1024d / 1024d / 1024d:0.00} GB";

        /// <summary>模型选择下拉框里显示的文本。</summary>
        public string DisplayText => $"{ModelName}（{SizeText}）";
    }
}
