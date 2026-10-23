using System.Text;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>生成 Markdown 格式的运行报告：硬件、引擎、已部署模型与最近日志。</summary>
    public static class ReportService
    {
        public static string Build(
            HardwareInfo hardware,
            DeployedModel? deployed,
            InferenceDevice device,
            bool running,
            string engineStatus,
            IReadOnlyList<string> logs)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# KfuPet 本地模型运行报告");
            builder.AppendLine();
            builder.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- 程序版本：{typeof(ReportService).Assembly.GetName().Version?.ToString(3) ?? "未知"}");
            builder.AppendLine();

            builder.AppendLine("## 硬件");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 值 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| 处理器 | {hardware.CpuName}（{hardware.LogicalCores} 线程） |");
            builder.AppendLine($"| 内存 | {hardware.RamGb:0.0} GB |");
            builder.AppendLine($"| 显卡 | {hardware.GpuName} |");
            builder.AppendLine($"| 显存 | {VramText(hardware)} |");
            builder.AppendLine();

            builder.AppendLine("## 引擎");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 值 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| 后端 | LLamaSharp 0.27.0（CPU + Vulkan） |");
            builder.AppendLine($"| 推理设备策略 | {DeviceText(device)} |");
            builder.AppendLine($"| 当前状态 | {engineStatus.Replace("本地引擎：", string.Empty)} |");
            builder.AppendLine($"| 模型目录 | {AppSettingsService.ResolveModelDirectory()} |");
            builder.AppendLine();

            builder.AppendLine("## 已部署模型");
            builder.AppendLine();
            if (deployed == null)
            {
                builder.AppendLine("尚未部署模型。");
            }
            else
            {
                builder.AppendLine("| 项目 | 值 |");
                builder.AppendLine("| --- | --- |");
                builder.AppendLine($"| 模型 | {deployed.ModelName} |");
                builder.AppendLine($"| 文件 | {deployed.FileName} |");
                builder.AppendLine($"| 大小 | {deployed.SizeText} |");
                builder.AppendLine($"| 路径 | {deployed.FilePath} |");
                builder.AppendLine($"| 部署时间 | {deployed.DeployedAt:yyyy-MM-dd HH:mm:ss} |");
                builder.AppendLine($"| 运行状态 | {(running ? "已加载" : "未加载")} |");
            }
            builder.AppendLine();

            builder.AppendLine("## 运行日志");
            builder.AppendLine();
            builder.AppendLine("```text");
            foreach (var line in logs)
            {
                builder.AppendLine(line);
            }
            builder.AppendLine("```");

            return builder.ToString();
        }

        private static string VramText(HardwareInfo hardware)
        {
            return hardware.HasDedicatedGpu
                ? $"{hardware.VramGb:0.0} GB（独显）"
                : "核显 / 无独显";
        }

        private static string DeviceText(InferenceDevice device)
        {
            return device switch
            {
                InferenceDevice.Gpu => "GPU 加速",
                InferenceDevice.Cpu => "纯 CPU",
                _ => "自动（按显存判断）"
            };
        }
    }
}
