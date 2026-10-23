using LLama.Native;

namespace kfupet_local_chat.Services
{
    /// <summary>
    /// 收集 llama.cpp 的原生日志。原生库只会加载一次，日志回调必须在任何 LLamaSharp 调用之前注册。
    /// </summary>
    public static class LlamaLogCollector
    {
        private const int MaxLines = 4000;

        private static readonly object Sync = new();
        private static readonly List<string> Lines = new();
        private static bool _configured;

        /// <summary>注册日志回调；版本不支持时静默降级，报告里会显示“未采集”。</summary>
        public static void Configure()
        {
            lock (Sync)
            {
                if (_configured)
                {
                    return;
                }

                _configured = true;
            }

            try
            {
                NativeLibraryConfig.All.WithLogCallback((_, message) => Append(message));
            }
            catch
            {
                // 老版本没有日志回调时忽略，不影响模型加载
            }
        }

        /// <summary>每次加载模型前清空，保证解析到的是本次加载的日志。</summary>
        public static void Clear()
        {
            lock (Sync)
            {
                Lines.Clear();
            }
        }

        public static IReadOnlyList<string> Snapshot()
        {
            lock (Sync)
            {
                return Lines.ToList();
            }
        }

        private static void Append(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = message.TrimEnd();
            lock (Sync)
            {
                Lines.Add(line);
                if (Lines.Count > MaxLines)
                {
                    Lines.RemoveRange(0, Lines.Count - MaxLines);
                }
            }
        }
    }
}
