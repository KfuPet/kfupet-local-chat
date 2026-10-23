namespace kfupet_local_chat.Services
{
    /// <summary>更新通知正文的提示词：与主项目 UpdateNotificationService 的结构保持一致。</summary>
    public static class UpdateNotificationPrompt
    {
        private const int MaxNotificationLength = 100;

        /// <summary>构造更新事件消息；试点用固定版本号与更新说明演示。</summary>
        public static string BuildEventPrompt()
        {
            const string notes =
                "新增本地模型下载与切换；控制台接入触摸反应、拖动越界回正与主动搭话；修复界面与状态同步问题。";

            return "（系统事件）你的新版本 v0.1.6 已经发布，主人当前使用的是 v0.1.5。\n" +
                   $"本次版本的更新说明（节选）：\n{notes}\n" +
                   "请用符合你人设的一句话告诉主人有新版本可以更新，并提醒主人点击这条通知查看更新详情" +
                   "（40 字以内）。只输出这一句话：不要引号、不要旁白、不要解释。 /no_think";
        }

        /// <summary>整理为一句通知正文，超过硬上限时截断。</summary>
        public static string NormalizeLine(string reply)
        {
            var text = TouchReactionPrompt.NormalizeLine(reply);
            return text.Length > MaxNotificationLength
                ? text[..MaxNotificationLength] + "…"
                : text;
        }
    }
}
