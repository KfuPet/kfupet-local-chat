using System.Diagnostics;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>
    /// 归档提炼：把归档里的原始对话批量提炼成信息点并评分，重要性达标的写入长期记忆，
    /// 其余低重要性内容再总结成一条留在归档。对应主项目的 ExtractFromConversationsAsync /
    /// ScoreContentAsync / SummarizeAsync 三个调用点。
    /// </summary>
    public sealed class ArchiveAnalysisService
    {
        private const int ExtractMaxTokens = 768;

        private const int ScoreMaxTokens = 48;

        private const int SummaryMaxTokens = 128;

        private readonly LocalModelRuntime _runtime;
        private readonly MemoryStoreService _memories;
        private readonly ModelDeploymentService _deployment = new();

        public ArchiveAnalysisService(LocalModelRuntime runtime, MemoryStoreService memories)
        {
            _runtime = runtime;
            _memories = memories;
        }

        /// <summary>跑完整条归档分析流程；失败时返回带原因的结果，不抛异常。</summary>
        public async Task<ArchiveAnalysisResult> AnalyzeAsync(
            IReadOnlyList<ArchiveMemoryEntry> entries,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (entries.Count == 0)
            {
                return ArchiveAnalysisResult.Failed("没有可分析的归档记录，请先载入归档文件。");
            }

            if (!_runtime.IsRunning)
            {
                return ArchiveAnalysisResult.Failed("本地模型尚未启动，请先在主窗口点击「运行本地」。");
            }

            var active = _deployment.LoadActive();
            if (!ModelCatalog.SupportsMemoryExtraction(active))
            {
                return ArchiveAnalysisResult.Failed(
                    $"当前模型 {active?.ModelName ?? "未记录"} 不支持稳定的信息提取，请切换到 4B 或 8B。");
            }

            var watch = Stopwatch.StartNew();
            var conversations = entries.Where(e => e.IsConversation && !e.RealtimeExtracted).ToList();
            var skippedRealtime = entries.Count(e => e.IsConversation && e.RealtimeExtracted);
            var summaries = entries.Where(e => !e.IsConversation).ToList();

            var results = new List<ArchiveResultItem>();
            var promoted = new List<ExtractedMemory>();
            var low = new List<string>();
            var extractionParsed = true;

            try
            {
                progress?.Report($"[归档] 共 {entries.Count} 条：待提炼对话 {conversations.Count} 条，" +
                                 $"总结条目 {summaries.Count} 条，已实时入库 {skippedRealtime} 条（跳过）");

                // 1. 总结条目：单条重新评分，≥3 进长期，<3 丢弃
                foreach (var entry in summaries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Content))
                    {
                        continue;
                    }

                    var scoreWatch = Stopwatch.StartNew();
                    var score = await ScoreContentAsync(entry.Content, cancellationToken);
                    scoreWatch.Stop();

                    if (score >= MemoryExtractionPrompt.MinImportance)
                    {
                        promoted.Add(new ExtractedMemory(entry.Content, score));
                        results.Add(new ArchiveResultItem(entry.Content, $"重要性 {score}", "待写入长期记忆"));
                    }
                    else
                    {
                        low.Add(entry.Content);
                        results.Add(new ArchiveResultItem(entry.Content, $"重要性 {score}", "保留归档"));
                    }

                    progress?.Report($"[归档] 总结条目重评分 {score}/5（{scoreWatch.Elapsed.TotalSeconds:0.0} s）：{entry.Content}");
                }

                // 2. 原始对话：一次性批量提炼并评分
                if (conversations.Count > 0)
                {
                    var extractWatch = Stopwatch.StartNew();
                    var (parsed, extracted) = await ExtractFromConversationsAsync(
                        conversations, cancellationToken);
                    extractWatch.Stop();
                    extractionParsed = parsed;

                    progress?.Report(parsed
                        ? $"[归档] 对话批量提炼：{conversations.Count} 条 → {extracted.Count} 个信息点" +
                          $"（{extractWatch.Elapsed.TotalSeconds:0.0} s）"
                        : $"[归档] 对话批量提炼没有返回可解析的 JSON（{extractWatch.Elapsed.TotalSeconds:0.0} s）");

                    foreach (var item in extracted)
                    {
                        if (item.Importance >= MemoryExtractionPrompt.MinImportance)
                        {
                            promoted.Add(item);
                            results.Add(new ArchiveResultItem(
                                item.Content, $"重要性 {item.Importance}", "待写入长期记忆"));
                        }
                        else
                        {
                            low.Add(item.Content);
                            results.Add(new ArchiveResultItem(
                                item.Content, $"重要性 {item.Importance}", "保留归档"));
                        }
                    }
                }

                // 3. 入选信息写入本试点长期记忆，并回填每条的实际状态
                var stored = _memories.Add(promoted.Select(item => (item.Content, item.Importance)));
                var storedContents = new HashSet<string>(
                    stored.Select(m => m.Content), StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < results.Count; i++)
                {
                    if (results[i].Status == "待写入长期记忆")
                    {
                        results[i] = results[i] with
                        {
                            Status = storedContents.Contains(results[i].Content)
                                ? "已写入长期记忆"
                                : "长期记忆已存在"
                        };
                    }
                }

                progress?.Report($"[归档] 重要性达标 {promoted.Count} 条，新写入长期记忆 {stored.Count} 条" +
                                 $"（其余 {promoted.Count - stored.Count} 条已存在）");

                // 4. 未入选信息：两条以上总结成一句，一条暂留归档
                string? summaryText = null;
                if (low.Count >= 2)
                {
                    var summaryWatch = Stopwatch.StartNew();
                    summaryText = await SummarizeAsync(low, cancellationToken);
                    summaryWatch.Stop();

                    if (!string.IsNullOrWhiteSpace(summaryText))
                    {
                        results.Add(new ArchiveResultItem(summaryText, "归档总结", "留在归档"));
                        progress?.Report($"[归档] {low.Count} 条未入选信息总结成一条" +
                                         $"（{summaryWatch.Elapsed.TotalSeconds:0.0} s）：{summaryText}");
                    }
                }
                else if (low.Count == 1)
                {
                    summaryText = low[0];
                    results.Add(new ArchiveResultItem(summaryText, "归档总结", "留在归档"));
                    progress?.Report("[归档] 1 条未入选信息暂留归档");
                }

                // 5. 重建归档：已处理的条目全部移除，未入选内容总结成一条放回；
                //    源归档只读，重建结果另存到项目 data 目录
                var rebuilt = new List<ArchiveMemoryEntry>();
                if (!string.IsNullOrWhiteSpace(summaryText))
                {
                    rebuilt.Add(new ArchiveMemoryEntry
                    {
                        Type = "summary",
                        Time = DateTime.Now,
                        Content = summaryText,
                        Importance = 0,
                        IsSummary = true
                    });
                }

                var rebuiltPath = ArchiveMemoryService.RebuiltArchivePath;
                ArchiveMemoryService.Save(rebuiltPath, rebuilt);
                progress?.Report(rebuilt.Count > 0
                    ? $"[归档] 已重建归档并另存：{rebuiltPath}（1 条总结）"
                    : $"[归档] 已重建归档并另存：{rebuiltPath}（空，全部提炼结果均达到重要性阈值，无剩余内容）");

                watch.Stop();
                return new ArchiveAnalysisResult
                {
                    Succeeded = true,
                    TotalEntries = entries.Count,
                    ConversationCount = conversations.Count,
                    SkippedRealtimeCount = skippedRealtime,
                    SummaryCount = summaries.Count,
                    ExtractedCount = promoted.Count + low.Count,
                    PromotedCount = promoted.Count,
                    StoredCount = stored.Count,
                    LowCount = low.Count,
                    SummaryText = summaryText,
                    RebuiltArchivePath = rebuiltPath,
                    ExtractionParsed = extractionParsed,
                    Seconds = watch.Elapsed.TotalSeconds,
                    Items = results
                };
            }
            catch (OperationCanceledException)
            {
                return ArchiveAnalysisResult.Failed("归档提炼已取消。");
            }
            catch (Exception ex)
            {
                return ArchiveAnalysisResult.Failed($"归档提炼失败：{ex.Message}");
            }
        }

        /// <summary>ExtractFromConversationsAsync：整批对话一次提炼，减少请求次数。</summary>
        private async Task<(bool Parsed, IReadOnlyList<ExtractedMemory> Items)> ExtractFromConversationsAsync(
            IReadOnlyList<ArchiveMemoryEntry> conversations,
            CancellationToken cancellationToken)
        {
            var raw = await _runtime.InferAsync(
                ArchiveMemoryPrompt.BuildExtractSystemPrompt(),
                ArchiveMemoryPrompt.BuildExtractUserPrompt(conversations),
                ExtractMaxTokens,
                cancellationToken);

            return MemoryExtractionPrompt.ParseItems(raw);
        }

        /// <summary>ScoreContentAsync：对单条内容重新评分；控制台也可单独调用这一调用点。</summary>
        public async Task<int> ScoreContentAsync(string content, CancellationToken cancellationToken)
        {
            var raw = await _runtime.InferAsync(
                ArchiveMemoryPrompt.BuildScoreSystemPrompt(),
                ArchiveMemoryPrompt.BuildScoreUserPrompt(content),
                ScoreMaxTokens,
                cancellationToken);

            return MemoryExtractionPrompt.ParseScore(raw);
        }

        /// <summary>SummarizeAsync：把多条低重要性信息总结成一句话；控制台也可单独调用这一调用点。</summary>
        public async Task<string> SummarizeAsync(
            IReadOnlyList<string> contents,
            CancellationToken cancellationToken)
        {
            var raw = await _runtime.InferAsync(
                ArchiveMemoryPrompt.BuildSummarySystemPrompt(),
                ArchiveMemoryPrompt.BuildSummaryUserPrompt(contents),
                SummaryMaxTokens,
                cancellationToken);

            return ArchiveMemoryPrompt.NormalizeSummary(raw);
        }
    }
}
