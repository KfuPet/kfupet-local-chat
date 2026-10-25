using System.Diagnostics;
using System.Text;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>
    /// 主对话的会话服务：注入长期记忆 → 生成回复 → 记录本轮 → 立即做一次核心信息提取。
    /// 与主项目一致，提取是「每轮结束后」做的事，不等整个会话结束。
    /// </summary>
    public sealed class ChatSessionService
    {
        /// <summary>注入下一轮对话的长期记忆条数上限。</summary>
        private const int MemoryInjectCount = 8;

        /// <summary>拼进上下文的最近对话轮数。</summary>
        private const int HistoryTurnCount = 6;

        private const int ReplyMaxTokens = 96;

        private const int ExtractMaxTokens = 192;

        private readonly LocalModelRuntime _runtime;
        private readonly MemoryStoreService _memories;
        private readonly ModelDeploymentService _deployment = new();
        private readonly List<ChatTurn> _turns = new();

        public ChatSessionService(LocalModelRuntime runtime, MemoryStoreService memories)
        {
            _runtime = runtime;
            _memories = memories;
        }

        public IReadOnlyList<ChatTurn> Turns => _turns;

        /// <summary>当前部署的模型名称，用于界面上显示。</summary>
        public string ActiveModelName => _deployment.LoadActive()?.ModelName ?? "未记录";

        /// <summary>
        /// 当前模型是否支持信息提取。按模型目录里的能力标记判断：
        /// 1.7B 输出 JSON 不稳定，只用于对话，不发起记忆提炼。
        /// </summary>
        public bool CanExtractMemory(out string reason)
        {
            var active = _deployment.LoadActive();
            if (active == null)
            {
                reason = "当前没有已部署的模型。";
                return false;
            }

            if (!ModelCatalog.SupportsMemoryExtraction(active))
            {
                reason = $"当前使用 {active.ModelName}，该档位不支持稳定的信息提取，本轮已跳过；" +
                         "需要记忆请切换到 4B 或 8B。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>发送一句话并拿到桌宠回复；成功后这一轮会进入上下文。</summary>
        public async Task<ChatTurn> SendAsync(string userMessage, CancellationToken cancellationToken)
        {
            if (!_runtime.IsRunning)
            {
                throw new InvalidOperationException("模型尚未启动");
            }

            var raw = await _runtime.InferAsync(
                BuildSystemPrompt(),
                BuildUserPrompt(userMessage),
                ReplyMaxTokens,
                cancellationToken);

            var reply = TouchReactionPrompt.NormalizeLine(raw);
            if (string.IsNullOrWhiteSpace(reply))
            {
                reply = "……";
            }

            var turn = new ChatTurn(DateTime.Now, userMessage, reply);
            _turns.Add(turn);
            return turn;
        }

        /// <summary>判断这一轮是否包含值得长期记住的信息，达到阈值的写入长期记忆。</summary>
        public async Task<MemoryExtractionResult> ExtractMemoryAsync(
            ChatTurn turn,
            CancellationToken cancellationToken)
        {
            if (!_runtime.IsRunning)
            {
                throw new InvalidOperationException("模型尚未启动");
            }

            // 能力不足的模型不发起提取，避免拿到无法解析的 JSON
            if (!CanExtractMemory(out var reason))
            {
                return new MemoryExtractionResult(
                    Parsed: false,
                    Core: Array.Empty<ExtractedMemory>(),
                    Stored: Array.Empty<LongTermMemory>(),
                    Seconds: 0,
                    Skipped: true,
                    SkipReason: reason);
            }

            var watch = Stopwatch.StartNew();
            var raw = await _runtime.InferAsync(
                MemoryExtractionPrompt.BuildSystemPrompt(),
                MemoryExtractionPrompt.BuildUserPrompt(turn.User, turn.Assistant),
                ExtractMaxTokens,
                cancellationToken);
            watch.Stop();

            var (parsed, items) = MemoryExtractionPrompt.ParseItems(raw);
            var core = items
                .Where(item => item.Importance >= MemoryExtractionPrompt.MinImportance)
                .ToList();
            var stored = _memories.Add(core.Select(item => (item.Content, item.Importance)));

            return new MemoryExtractionResult(parsed, core, stored, watch.Elapsed.TotalSeconds);
        }

        /// <summary>人设提示词 + 当前长期记忆（按重要性取前几条）。</summary>
        private string BuildSystemPrompt()
        {
            var prompt = TouchReactionPrompt.BuildSystemPrompt();
            var memories = _memories.Load().Take(MemoryInjectCount).ToList();
            if (memories.Count == 0)
            {
                return prompt;
            }

            var builder = new StringBuilder(prompt);
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("关于主人的记忆（你已知晓）：");
            foreach (var memory in memories)
            {
                builder.AppendLine($"- {memory.Content}");
            }

            return builder.ToString();
        }

        /// <summary>把最近几轮对话拼成一段文本，和当前消息一起交给单轮执行器。</summary>
        private string BuildUserPrompt(string userMessage)
        {
            if (_turns.Count == 0)
            {
                return userMessage + " /no_think";
            }

            var builder = new StringBuilder();
            builder.AppendLine("以下是你们最近的对话记录：");
            foreach (var turn in _turns.TakeLast(HistoryTurnCount))
            {
                builder.AppendLine($"主人：{turn.User}");
                builder.AppendLine($"摇摇：{turn.Assistant}");
            }

            builder.AppendLine();
            builder.AppendLine($"主人：{userMessage}");
            builder.AppendLine("请以摇摇的身份自然回复主人这一句，只输出回复内容。 /no_think");
            return builder.ToString();
        }
    }

    /// <summary>一轮主对话。</summary>
    public sealed record ChatTurn(DateTime Time, string User, string Assistant);

    /// <summary>一轮记忆提取的结果。</summary>
    public sealed record MemoryExtractionResult(
        bool Parsed,
        IReadOnlyList<ExtractedMemory> Core,
        IReadOnlyList<LongTermMemory> Stored,
        double Seconds,
        bool Skipped = false,
        string? SkipReason = null);
}
