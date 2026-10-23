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

        /// <summary>GGUF 文件的 SHA256（下载时增量计算）；早期记录为空。</summary>
        public string? Sha256 { get; init; }

        /// <summary>参数量，例如 4B；早期记录为空。</summary>
        public string? Parameters { get; init; }

        /// <summary>量化方式，例如 Q4_K_M；早期记录为空。</summary>
        public string? Quantization { get; init; }

        public string SizeText => $"{SizeBytes / 1024d / 1024d / 1024d:0.00} GB";

        /// <summary>模型选择下拉框里显示的文本。</summary>
        public string DisplayText => $"{ModelName}（{SizeText}）";
    }
}
