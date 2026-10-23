using System.Text.RegularExpressions;

namespace kfupet_local_chat.Services
{
    /// <summary>触摸反应的提示词与回复整理：事件文案与主项目角色包 reactions.json 保持一致。</summary>
    public static class TouchReactionPrompt
    {
        public const string PartHead = "head";

        private const string EventHeadPet = "headPet";
        private const string Instruction =
            "请用符合你人设的一句话回应，只输出这一句话（20 字以内），不要引号、不要旁白、不要解释。";

        private static readonly Dictionary<string, string> EventPrompts = new(StringComparer.OrdinalIgnoreCase)
        {
            [EventHeadPet] = "主人按住你的头，轻轻来回抚摸了几下",
            [PartHead] = "主人戳了戳你的头",
            ["armLeft"] = "主人戳了戳你的右手",
            ["armRight"] = "主人戳了戳你的左手",
            ["legLeft"] = "主人戳了戳你的右腿",
            ["legRight"] = "主人戳了戳你的左腿",
            ["body"] = "主人戳了戳你的身体",
            ["snapBack"] = "主人拖动你的时候手滑，你差点掉出屏幕，还好你及时靠边站稳了",
            ["idleChat"] = "主人已经有一阵子没理你了，你有点想主动找主人说说话",
            ["welcomeBack"] = "主人离开很久了，刚刚回到电脑前",
            ["morningGreeting"] = "现在是早上，主人新的一天开始了",
            ["eveningGreeting"] = "现在是晚上，主人还在电脑前忙碌着"
        };

        /// <summary>构造发给模型的触摸事件消息。</summary>
        public static string BuildEventPrompt(string partKey, bool isPetting)
        {
            var eventKey = partKey == PartHead && isPetting ? EventHeadPet : partKey;
            if (!EventPrompts.TryGetValue(eventKey, out var action))
            {
                return string.Empty;
            }

            return $"（触摸事件）{action}。{Instruction} /no_think";
        }

        /// <summary>构造拖动越界回正的事件消息。</summary>
        public static string BuildSnapBackEventPrompt()
        {
            return BuildEventMessage("拖动事件", "snapBack");
        }

        /// <summary>构造主动搭话的事件消息：语境 + 角色事件描述。</summary>
        public static string BuildProactiveEventPrompt(string eventKey, string contextNote)
        {
            var action = EventPrompts.GetValueOrDefault(eventKey);
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

            var context = string.IsNullOrWhiteSpace(contextNote) ? string.Empty : contextNote;
            return $"（主动搭话）{context}{action}。{Instruction} /no_think";
        }

        private static string BuildEventMessage(string label, string eventKey)
        {
            var action = EventPrompts.GetValueOrDefault(eventKey);
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

            return $"（{label}）{action}。{Instruction} /no_think";
        }

        /// <summary>桌面宠物的人设底线，正式项目会换成角色包的完整提示词。</summary>
        public static string BuildSystemPrompt()
        {
            return "你是一只名叫「摇摇」的桌面宠物，性格活泼、黏人、有点小傲娇，说话像日常聊天。\n" +
                   "只输出一句符合人设的中文回应，不要引号、不要旁白、不要解释，不要提到自己是 AI。";
        }

        /// <summary>整理模型回复：去掉思考段、特殊标记、成对引号，只保留第一行。</summary>
        public static string NormalizeLine(string reply)
        {
            var text = reply.Trim();

            // Qwen3 混合推理模型可能输出  思考… ，整段剔除
            text = Regex.Replace(text, "<think[^>]*>.*?</think\\s*>", string.Empty,
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 思考被截断、没有闭合标签时，从 <think 开始全部丢弃
            var openThink = text.IndexOf("<think", StringComparison.OrdinalIgnoreCase);
            if (openThink >= 0)
            {
                text = text[..openThink];
            }

            text = text.Replace("<|im_start|>", string.Empty)
                       .Replace("<|im_end|>", string.Empty)
                       .Trim();

            if (text.Length >= 2)
            {
                var first = text[0];
                var last = text[^1];
                if ((first == '"' && last == '"') ||
                    (first == '「' && last == '」') ||
                    (first == '『' && last == '』'))
                {
                    text = text[1..^1].Trim();
                }
            }

            var newlineIndex = text.IndexOfAny(new[] { '\r', '\n' });
            if (newlineIndex > 0)
            {
                text = text[..newlineIndex].Trim();
            }

            return text;
        }
    }
}
