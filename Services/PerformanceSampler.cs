using System.Diagnostics;
using System.Globalization;
using System.IO;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>
    /// 运行期间每秒采样一次：进程 CPU、进程内存，以及 NVIDIA 的利用率、显存、温度、功耗与频率。
    /// </summary>
    public sealed class PerformanceSampler
    {
        private readonly object _sync = new();
        private readonly List<PerformanceSample> _samples = new();
        private readonly string? _nvidiaSmiPath;

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private DateTime _startedAt;

        public PerformanceSampler()
        {
            var candidate = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            _nvidiaSmiPath = File.Exists(candidate) ? candidate : null;
        }

        public void Start()
        {
            if (_cts != null)
            {
                return;
            }

            _startedAt = DateTime.Now;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>停止采样并返回全部样本；未启动过时返回空列表。</summary>
        public async Task<IReadOnlyList<PerformanceSample>> StopAsync()
        {
            if (_cts == null)
            {
                return Array.Empty<PerformanceSample>();
            }

            _cts.Cancel();
            if (_loop != null)
            {
                try
                {
                    await _loop;
                }
                catch
                {
                    // 采样线程的异常不能影响停止模型的主流程
                }
            }

            _cts.Dispose();
            _cts = null;
            _loop = null;

            lock (_sync)
            {
                return _samples.ToList();
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            var lastCpu = process.TotalProcessorTime;
            var lastWall = DateTime.Now;

            // 立即记一个基线样本，避免开头一段空白
            AddSample(0, 0, process);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (true)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(token))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                process.Refresh();
                var cpu = process.TotalProcessorTime;
                var wall = DateTime.Now;

                var wallMs = Math.Max(1, (wall - lastWall).TotalMilliseconds);
                var cpuPercent = Math.Clamp(
                    (cpu - lastCpu).TotalMilliseconds / wallMs / Environment.ProcessorCount * 100,
                    0,
                    100);

                lastCpu = cpu;
                lastWall = wall;

                AddSample((wall - _startedAt).TotalSeconds, cpuPercent, process);
            }
        }

        private void AddSample(double offsetSeconds, double cpuPercent, Process process)
        {
            var memoryMb = process.WorkingSet64 / 1024d / 1024d;
            var gpu = ReadGpu();

            lock (_sync)
            {
                _samples.Add(new PerformanceSample(
                    offsetSeconds,
                    cpuPercent,
                    memoryMb,
                    gpu.Utilization,
                    gpu.MemoryUsedMb,
                    gpu.TemperatureC,
                    gpu.PowerW,
                    gpu.ClockMhz,
                    gpu.MaxClockMhz));
            }
        }

        /// <summary>
        /// 通过 nvidia-smi 读取第一块 NVIDIA 显卡的利用率、显存、温度、功耗与频率；
        /// 单个字段为 [N/A] 或解析失败时该字段留空，不影响其余字段。
        /// </summary>
        private GpuReading ReadGpu()
        {
            if (_nvidiaSmiPath == null)
            {
                return default;
            }

            try
            {
                const string query =
                    "utilization.gpu,memory.used,temperature.gpu,power.draw,clocks.gr,clocks.max.gr";

                using var process = Process.Start(new ProcessStartInfo(
                    _nvidiaSmiPath,
                    $"--query-gpu={query} --format=csv,noheader,nounits")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    return default;
                }

                var line = process.StandardOutput.ReadLine();
                process.WaitForExit(2000);

                if (string.IsNullOrWhiteSpace(line))
                {
                    return default;
                }

                var parts = line.Split(',');
                return new GpuReading(
                    Parse(parts, 0),
                    Parse(parts, 1),
                    Parse(parts, 2),
                    Parse(parts, 3),
                    Parse(parts, 4),
                    Parse(parts, 5));
            }
            catch
            {
                return default;
            }
        }

        private static double? Parse(string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return null;
            }

            var text = parts[index].Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private readonly record struct GpuReading(
            double? Utilization,
            double? MemoryUsedMb,
            double? TemperatureC,
            double? PowerW,
            double? ClockMhz,
            double? MaxClockMhz);
    }
}
