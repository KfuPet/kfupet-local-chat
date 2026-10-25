using System.Text;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>归档批量提炼用到的三段提示词：对话提炼、单条重评分、多条总结。</summary>
    public static class ArchiveMemoryPrompt
    {
        /// <summary>与主项目 ExtractFromConversationsAsync 保持一致。</summary>
        public static string BuildExtractSystemPrompt()
        {
            return """
                你是记忆提取器。从以下对话中提取值得长期记住的、关于用户的信息
                （如名字、喜好、习惯、经历、目标、重要关系等）。
                只提取稳定且长期有用的信息，忽略临时、琐碎或纯寒暄的内容。

                对每条提取的信息进行重要性评分（0~5 整数）：
                0 临时信息，1 普通聊天，2 可能有用的信息，3 用户习惯，4 用户偏好/长期目标，5 核心用户信息。

                请只输出一个 JSON 数组，不要包含其他任何文字或代码块标记，格式如下：
                [{"content": "一句话概括的信息", "importance": 4}]
                没有值得记住的信息时输出空数组 []。
                """;
        }

        public static string BuildExtractUserPrompt(IReadOnlyList<ArchiveMemoryEntry> conversations)
        {
            var builder = new StringBuilder();
            foreach (var entry in conversations)
            {
                builder.AppendLine($"用户说：{entry.User}");
                builder.AppendLine($"助手回复：{entry.Assistant}");
                builder.AppendLine();
            }

            builder.Append("/no_think");
            return builder.ToString();
        }

        /// <summary>与主项目 ScoreContentAsync 保持一致。</summary>
        public static string BuildScoreSystemPrompt()
        {
            return """
                对下面这条信息进行重要性评分（0~5 整数）：
                0 临时信息，1 普通聊天，2 可能有用的信息，3 用户习惯，4 用户偏好/长期目标，5 核心用户信息。

                请只输出一个 JSON 对象，不要包含其他任何文字，格式：{"importance": 4}
                """;
        }

        public static string BuildScoreUserPrompt(string content)
        {
            return $"信息：{content} /no_think";
        }

        /// <summary>与主项目 SummarizeAsync 保持一致。</summary>
        public static string BuildSummarySystemPrompt()
        {
            return """
                把下面多条零散信息总结成一句话，保留关键事实，不要遗漏重要点。
                请只输出总结后的一句话，不要包含其他任何文字。
                """;
        }

        public static string BuildSummaryUserPrompt(IReadOnlyList<string> contents)
        {
            var builder = new StringBuilder();
            foreach (var content in contents)
            {
                builder.AppendLine($"- {content}");
            }

            builder.Append("/no_think");
            return builder.ToString();
        }

        /// <summary>整理总结回复：去掉思考段与首尾空白，只保留正文。</summary>
        public static string NormalizeSummary(string reply)
        {
            return MemoryExtractionPrompt.StripThinking(reply).Trim();
        }
    }
}
