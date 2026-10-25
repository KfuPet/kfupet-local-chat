using System.IO;

namespace kfupet_local_chat.Services
{
    /// <summary>
    /// 清除本试点运行过程中产生的数据：长期记忆、运行历史与真实记忆副本。
    /// 保留模型部署记录、模型目录 / 设备设置以及磁盘上的 GGUF 模型文件。
    /// </summary>
    public static class AppDataResetService
    {
        public static IReadOnlyList<string> ClearAll()
        {
            var removed = new List<string>();

            new MemoryStoreService().Clear();
            removed.Add("长期记忆");

            RunHistoryService.Clear();
            removed.Add("运行历史");

            // 早期「载入真实记忆副本」留下的只读副本，以及归档提炼的另存结果
            foreach (var name in new[] { "ArchiveMemory.json", "LongMemory.json", "ArchiveMemory.rebuilt.json" })
            {
                var path = Path.Combine(AppPaths.ProjectRoot, "data", name);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        removed.Add($"data/{name}");
                    }
                }
                catch
                {
                    // 副本删不掉不影响其它数据清理
                }
            }

            return removed;
        }
    }
}
