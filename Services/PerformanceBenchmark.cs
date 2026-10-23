using System.Text;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>启动后自动跑的两组固定基准：短句对话与长 Prompt 提炼，用来横向比较模型速度。</summary>
    public static class PerformanceBenchmark
    {
        public const string ShortName = "短句对话";

        public const string LongName = "长 Prompt 提炼";

        /// <summary>依次跑完两组基准；用户中途停止时只返回已完成的部分。</summary>
        public static async Task<IReadOnlyList<BenchmarkResult>> RunAsync(
            LocalModelRuntime runtime,
            Action<BenchmarkResult>? onItem,
            CancellationToken cancellationToken)
        {
            var results = new List<BenchmarkResult>();
            if (!runtime.IsRunning)
            {
                return results;
            }

            // 先跑一次极短推理做预热：否则首个基准的首 token 延迟会包含 Vulkan 着色器编译等一次性开销
            try
            {
                await runtime.InferWithMetricsAsync(
                    TouchReactionPrompt.BuildSystemPrompt(),
                    "你好 /no_think",
                    maxTokens: 1,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return results;
            }
            catch
            {
                // 预热失败不影响后续基准
            }

            var cases = new List<BenchmarkCase>
            {
                new(ShortName, TouchReactionPrompt.BuildEventPrompt(TouchReactionPrompt.PartHead, isPetting: true), 64),
                new(LongName, BuildLongPrompt(), 128)
            };

            foreach (var item in cases)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var metrics = await runtime.InferWithMetricsAsync(
                        TouchReactionPrompt.BuildSystemPrompt(),
                        item.Prompt,
                        item.MaxTokens,
                        cancellationToken);

                    var result = new BenchmarkResult(
                        item.Name,
                        item.Prompt.Length,
                        metrics.OutputTokens,
                        metrics.TimeToFirstTokenMs,
                        metrics.TokensPerSecond,
                        metrics.TotalSeconds,
                        Completed: true);

                    results.Add(result);
                    onItem?.Invoke(result);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var failed = new BenchmarkResult(
                        item.Name, item.Prompt.Length, 0, 0, 0, 0,
                        Completed: false, ex.Message);
                    results.Add(failed);
                    onItem?.Invoke(failed);
                }
            }

            return results;
        }

        /// <summary>与事件调用点一致的短句，方便和实际使用场景对齐。</summary>
        private static string BuildLongPrompt()
        {
            const string dialogue =
                "主人：今天外面下雨了，我回来的时候鞋子全湿了。\n" +
                "摇摇：那一定很不舒服吧，先把鞋换成干的，别着凉了。\n" +
                "主人：晚上想煮点面，但冰箱里只剩鸡蛋和青菜了。\n" +
                "摇摇：鸡蛋青菜面也很香呀，我可以陪你一起等水开。\n" +
                "主人：明天要早起开会，得七点半就出门。\n" +
                "摇摇：那我明早七点就提醒你，绝对不会让你睡过头。\n";

            var builder = new StringBuilder();
            builder.AppendLine("以下是一段和主人的对话记录，请提炼出三条长期记忆要点，");
            builder.AppendLine("每条不超过 20 字，用分号分隔，只输出这三条要点：");
            builder.AppendLine();
            builder.AppendLine(dialogue);
            builder.AppendLine(dialogue);
            builder.Append("/no_think");
            return builder.ToString();
        }

        private readonly record struct BenchmarkCase(string Name, string Prompt, int MaxTokens);
    }
}
