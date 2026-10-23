namespace kfupet_local_chat.Models
{
    /// <summary>可供下载部署的本地模型条目，以及它在当前机器上的可用状态。</summary>
    public sealed class LocalModelOption
    {
        public required string Name { get; init; }

        public required string Parameters { get; init; }

        public required string Quantization { get; init; }

        public required long DownloadSizeBytes { get; init; }

        /// <summary>GGUF 文件名，下载后落盘用。</summary>
        public required string FileName { get; init; }

        /// <summary>按优先级排列的下载地址，失败时依次尝试。</summary>
        public required IReadOnlyList<string> DownloadUrls { get; init; }

        /// <summary>最低独显显存要求，0 表示不依赖独显。</summary>
        public required long MinVramBytes { get; init; }

        public required long MinRamBytes { get; init; }

        /// <summary>推荐显存，达到后标记为可流畅运行。</summary>
        public required long RecommendedVramBytes { get; init; }

        public required long RecommendedRamBytes { get; init; }

        /// <summary>显存不足时是否允许退回 CPU 推理。</summary>
        public required bool CpuFallbackAllowed { get; init; }

        public required string RequirementText { get; init; }

        public required string Note { get; init; }

        public string SizeText => $"{DownloadSizeBytes / 1024d / 1024d / 1024d:0.0} GB";

        /// <summary>当前机器上的运行状态，由 ModelCatalog 计算。</summary>
        public string StatusText { get; set; } = string.Empty;

        public bool CanRun { get; set; }

        public bool IsRecommended { get; set; }

        /// <summary>产品默认档（4B），与硬件无关。</summary>
        public bool IsDefault { get; init; }

        public bool ShowDefaultBadge => IsDefault;

        /// <summary>同时是默认档时只显示“默认”，避免两个标签并排。</summary>
        public bool ShowRecommendedBadge => IsRecommended && !IsDefault;
    }
}
