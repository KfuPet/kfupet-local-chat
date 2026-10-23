using System.Text;
using LLama;
using LLama.Common;

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

                var weights = LLamaWeights.LoadFromFile(parameters);
                try
                {
                    var context = weights.CreateContext(parameters);
                    _weights = weights;
                    _context = context;
                    _parameters = parameters;
                    LoadedPath = modelPath;
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
                await foreach (var token in executor.InferAsync(
                                   userMessage, inferenceParams, cancellationToken))
                {
                    builder.Append(token);
                }

                return builder.ToString().Trim();
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
