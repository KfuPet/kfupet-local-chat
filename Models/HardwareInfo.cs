namespace kfupet_local_chat.Models
{
    /// <summary>本机硬件摘要：由 HardwareProbe 读取，用于模型推荐。</summary>
    public sealed record HardwareInfo(
        string CpuName,
        int LogicalCores,
        long TotalRamBytes,
        string GpuName,
        long DedicatedVideoMemoryBytes)
    {
        public double RamGb => TotalRamBytes / 1024d / 1024d / 1024d;

        public double VramGb => DedicatedVideoMemoryBytes / 1024d / 1024d / 1024d;

        /// <summary>是否为核显或未识别到独显。</summary>
        public bool HasDedicatedGpu => DedicatedVideoMemoryBytes > 0;
    }
}
