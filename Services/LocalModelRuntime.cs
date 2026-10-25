using System.Diagnostics;
using System.Text;
using LLama;
using LLama.Common;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>进程内的 LLamaSharp 运行时：启动时加载 GGUF 权重并创建上下文，结束或卸载时释放。</summary>
    public sealed class LocalModelRuntime : IDisposable
    {
        private readonly SemaphoreSlim _inferenceGate = new(1, 1);

        private LLamaWeights? _weights;
        private LLamaContext? _context;
        private ModelParams? _parameters;
        private bool _disposed;

        public bool IsRunning => _weights != null;

        public string? LoadedPath { get; private set; }

        /// <summary>本次加载从 llama.cpp 日志里还原的后端信息；拿不到原生日志时为空。</summary>
        public BackendInfo? Backend { get; private set; }

        /// <summary>加载模型；加载失败会抛异常，由调用方提示用户。</summary>
        public Task StartAsync(string modelPath, int gpuLayerCount)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = 8192,
                    GpuLayerCount = gpuLayerCount
                };

                // 只保留本次加载的原生日志，避免解析到上一次的层卸载信息
                LlamaLogCollector.Clear();

                var weights = LLamaWeights.LoadFromFile(parameters);
                try
                {
                    var context = weights.CreateContext(parameters);
                    _weights = weights;
                    _context = context;
                    _parameters = parameters;
                    LoadedPath = modelPath;
                    Backend = BackendInfo.FromLogs(
                        LlamaLogCollector.Snapshot(),
                        (int)(parameters.ContextSize ?? 0));
                }
                catch
                {
                    weights.Dispose();
                    throw;
                }
            });
        }

        /// <summary>用当前模型跑一次短推理：套用 GGUF 自带的对话模板，适合短句类调用。</summary>
        public async Task<string> InferAsync(
            string systemPrompt,
            string userMessage,
            int maxTokens = 96,
            CancellationToken cancellationToken = default)
        {
            var metrics = await InferWithMetricsAsync(
                systemPrompt, userMessage, maxTokens, cancellationToken);
            return metrics.Text;
        }

        /// <summary>与 <see cref="InferAsync"/> 相同，但同时记录首 token 延迟、输出 token 数与生成速度。</summary>
        public async Task<InferenceMetrics> InferWithMetricsAsync(
            string systemPrompt,
            string userMessage,
            int maxTokens = 96,
            CancellationToken cancellationToken = default)
        {
            var weights = _weights ?? throw new InvalidOperationException("模型尚未启动");
            var parameters = _parameters ?? throw new InvalidOperationException("模型参数缺失");

            await _inferenceGate.WaitAsync(cancellationToken);
            try
            {
                var executor = new StatelessExecutor(weights, parameters)
                {
                    ApplyTemplate = true,
                    SystemMessage = systemPrompt
                };

                var inferenceParams = new InferenceParams
                {
                    MaxTokens = maxTokens,
                    AntiPrompts = new[] { "<|im_end|>", "<|endoftext|>" }
                };

                var builder = new StringBuilder();
                var stopwatch = Stopwatch.StartNew();
                var firstTokenMs = 0d;
                var tokens = 0;

                await foreach (var token in executor.InferAsync(
                                   userMessage, inferenceParams, cancellationToken))
                {
                    if (tokens == 0)
                    {
                        firstTokenMs = stopwatch.Elapsed.TotalMilliseconds;
                    }

                    tokens++;
                    builder.Append(token);
                }

                var totalSeconds = stopwatch.Elapsed.TotalSeconds;
                stopwatch.Stop();

                var text = builder.ToString().Trim();
                if (tokens == 0)
                {
                    return new InferenceMetrics(
                        text, 0, totalSeconds * 1000, 0, totalSeconds);
                }

                // 生成阶段 = 总耗时 - 首 token 前的提示词处理时间
                var generationSeconds = Math.Max(0.001, totalSeconds - firstTokenMs / 1000);
                return new InferenceMetrics(
                    text, tokens, firstTokenMs, generationSeconds, totalSeconds);
            }
            finally
            {
                _inferenceGate.Release();
            }
        }

        /// <summary>等待当前推理结束后再释放，避免在推理过程中销毁原生上下文。</summary>
        public async Task StopWhenIdleAsync()
        {
            await _inferenceGate.WaitAsync();
            try
            {
                Stop();
            }
            finally
            {
                _inferenceGate.Release();
            }
        }

        /// <summary>释放上下文与权重，下次启动重新加载。</summary>
        public void Stop()
        {
            _context?.Dispose();
            _context = null;

            _weights?.Dispose();
            _weights = null;
            _parameters = null;
            Backend = null;

            LoadedPath = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _inferenceGate.Dispose();
        }
    }
}
