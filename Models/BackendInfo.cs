using System.Text.RegularExpressions;

namespace kfupet_local_chat.Models
{
    /// <summary>从 llama.cpp 原生日志还原的真实推理后端：实际后端、设备、层卸载与上下文长度。</summary>
    public sealed class BackendInfo
    {
        private static readonly Regex OffloadRegex = new(
            @"offloaded\s+(\d+)\s*/\s*(\d+)\s+layers",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UsingDeviceRegex = new(
            @"using device\s+(\w+)\s*\(([^)]+)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex VulkanDeviceRegex = new(
            @"ggml_vulkan:\s*0\s*=\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CudaDeviceRegex = new(
            @"Device\s+0:\s*(.+?)(?:,|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] InterestingKeywords =
        {
            "offload", "ggml_vulkan", "ggml_cuda", "vulkan backend", "cuda backend",
            "using device", "buffer size", "n_ctx", "llama_model_load_from_file_impl"
        };

        public string BackendName { get; init; } = "未采集";

        public string? DeviceName { get; init; }

        public int? OffloadedLayers { get; init; }

        public int? TotalLayers { get; init; }

        public int ContextSize { get; init; }

        /// <summary>筛选后的相关日志行，导出报告时附在文末便于核对。</summary>
        public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();

        public string LayersText => OffloadedLayers is { } offloaded && TotalLayers is { } total
            ? $"{offloaded}/{total} 层"
            : "未记录";

        /// <summary>没有原生日志时报告里显示的占位值。</summary>
        public static BackendInfo Unavailable(int contextSize)
        {
            return new BackendInfo { BackendName = "未采集", ContextSize = contextSize };
        }

        /// <summary>解析日志：识别 Vulkan / CUDA / CPU 后端、设备名与卸载层数。</summary>
        public static BackendInfo FromLogs(IReadOnlyList<string> lines, int contextSize)
        {
            if (lines.Count == 0)
            {
                return Unavailable(contextSize);
            }

            var backendName = "CPU";
            int? offloadedLayers = null;
            int? totalLayers = null;
            var interesting = new List<string>();

            // 先找 llama.cpp 最终选中的设备，例如 “using device Vulkan1 (NVIDIA ...)”
            string? deviceName = null;
            foreach (var line in lines)
            {
                var usingDevice = UsingDeviceRegex.Match(line);
                if (usingDevice.Success)
                {
                    deviceName = usingDevice.Groups[2].Value.Trim();
                    break;
                }
            }

            foreach (var line in lines)
            {
                var isVulkan = line.Contains("ggml_vulkan", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("vulkan backend", StringComparison.OrdinalIgnoreCase);
                var isCuda = line.Contains("ggml_cuda", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("cuda backend", StringComparison.OrdinalIgnoreCase);

                if (isVulkan)
                {
                    backendName = "Vulkan";
                }

                if (isCuda)
                {
                    backendName = "CUDA";
                }

                // 找不到“using device”时退回第一块枚举到的设备
                if (deviceName == null)
                {
                    deviceName = MatchFirst(VulkanDeviceRegex, line) ?? MatchFirst(CudaDeviceRegex, line);
                }

                var offload = OffloadRegex.Match(line);
                if (offload.Success &&
                    int.TryParse(offload.Groups[1].Value, out var off) &&
                    int.TryParse(offload.Groups[2].Value, out var total))
                {
                    offloadedLayers = off;
                    totalLayers = total;
                }

                if (IsInteresting(line))
                {
                    interesting.Add(line.Length > 200 ? line[..200] : line);
                }
            }

            return new BackendInfo
            {
                BackendName = backendName,
                DeviceName = deviceName,
                OffloadedLayers = offloadedLayers,
                TotalLayers = totalLayers,
                ContextSize = contextSize,
                LogLines = interesting.Take(24).ToList()
            };
        }

        private static string? MatchFirst(Regex regex, string line)
        {
            var match = regex.Match(line);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static bool IsInteresting(string line)
        {
            // 逐层分配 / KV 缓存逐层打印的行太多，报告里不保留
            if (line.Contains("assigned to device", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("dev = ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return InterestingKeywords.Any(keyword =>
                line.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }
}
