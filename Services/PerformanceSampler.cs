using System.Diagnostics;
using System.Globalization;
using System.IO;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>运行期间每秒采样一次：进程 CPU、进程内存，以及 NVIDIA 的 GPU / 显存占用。</summary>
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
            var (gpuPercent, vramMb) = ReadGpu();

            lock (_sync)
            {
                _samples.Add(new PerformanceSample(offsetSeconds, cpuPercent, memoryMb, gpuPercent, vramMb));
            }
        }

        /// <summary>通过 nvidia-smi 读取第一块 NVIDIA 显卡的利用率和已用显存；没有该工具时返回空值。</summary>
        private (double? GpuPercent, double? VramMb) ReadGpu()
        {
            if (_nvidiaSmiPath == null)
            {
                return (null, null);
            }

            try
            {
                using var process = Process.Start(new ProcessStartInfo(
                    _nvidiaSmiPath,
                    "--query-gpu=utilization.gpu,memory.used --format=csv,noheader,nounits")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    return (null, null);
                }

                var line = process.StandardOutput.ReadLine();
                process.WaitForExit(2000);

                if (string.IsNullOrWhiteSpace(line))
                {
                    return (null, null);
                }

                var parts = line.Split(',');
                if (parts.Length < 2 ||
                    !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var utilization) ||
                    !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var used))
                {
                    return (null, null);
                }

                return (utilization, used);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
