using System.Text.Json;
using System.Text.RegularExpressions;

namespace kfupet_local_chat.Services
{
    /// <summary>实时核心信息提取的提示词与解析：一轮对话结束后判断是否值得长期记住。</summary>
    public static class MemoryExtractionPrompt
    {
        /// <summary>写入长期记忆的最低重要性。</summary>
        public const int MinImportance = 3;

        private static readonly Regex ThinkRegex = new(
            "<think[^>]*>.*?</think\\s*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>与主项目 MemorySystem.ExtractCoreInfoAsync 保持一致的提取要求。</summary>
        public static string BuildSystemPrompt()
        {
            return """
                你是记忆提取器。判断下面这轮对话中是否包含关于用户的、值得长期记住的信息，
                例如：生日、姓名、性别、年龄、住址、联系方式、重要偏好、长期目标、重要关系、重要事件等。

                只提取重要性 ≥ 3 的信息（3 用户习惯，4 用户偏好/长期目标，5 核心用户信息），
                忽略普通聊天、临时信息、寒暄和琐碎内容。

                请只输出一个 JSON 数组，不要包含其他任何文字或代码块标记，格式如下：
                [{"content": "一句话概括的信息", "importance": 4}]
                没有需要立即记住的核心信息时输出空数组 []。
                """;
        }

        public static string BuildUserPrompt(string userMessage, string assistantReply)
        {
            return $"用户说：{userMessage}\n助手回复：{assistantReply} /no_think";
        }

        /// <summary>解析模型返回的 JSON 数组；返回是否解析成功与信息列表。</summary>
        public static (bool Parsed, IReadOnlyList<ExtractedMemory> Items) ParseItems(string reply)
        {
            var text = StripThink(reply);

            // 模型偶尔会在 JSON 前后附带说明文字，截取最外层数组
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                return (false, Array.Empty<ExtractedMemory>());
            }

            text = text[start..(end + 1)];
            try
            {
                using var doc = JsonDocument.Parse(text);
                var items = new List<ExtractedMemory>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var content = item.TryGetProperty("content", out var c)
                        ? c.GetString()?.Trim() ?? string.Empty
                        : string.Empty;
                    var importance = item.TryGetProperty("importance", out var i) && i.TryGetInt32(out var value)
                        ? Math.Clamp(value, 0, 5)
                        : 0;

                    if (content.Length > 0)
                    {
                        items.Add(new ExtractedMemory(content, importance));
                    }
                }

                return (true, items);
            }
            catch (JsonException)
            {
                return (false, Array.Empty<ExtractedMemory>());
            }
        }

        private static string StripThink(string reply)
        {
            var text = ThinkRegex.Replace(reply, string.Empty);

            // 思考段被截断、没有闭合标签时，从 <think 开始全部丢弃
            var openThink = text.IndexOf("<think", StringComparison.OrdinalIgnoreCase);
            return openThink >= 0 ? text[..openThink] : text;
        }
    }

    /// <summary>模型从一轮对话中提取出的一条信息。</summary>
    public sealed record ExtractedMemory(string Content, int Importance);
}
