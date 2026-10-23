using LLama;
using LLama.Common;

namespace kfupet_local_chat.Services
{
    /// <summary>进程内的 LLamaSharp 运行时：启动时加载 GGUF 权重并创建上下文，结束或卸载时释放。</summary>
    public sealed class LocalModelRuntime : IDisposable
    {
        private LLamaWeights? _weights;
        private LLamaContext? _context;

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
                    LoadedPath = modelPath;
                }
                catch
                {
                    weights.Dispose();
                    throw;
                }
            });
        }

        /// <summary>释放上下文与权重，下次启动重新加载。</summary>
        public void Stop()
        {
            _context?.Dispose();
            _context = null;

            _weights?.Dispose();
            _weights = null;

            LoadedPath = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
