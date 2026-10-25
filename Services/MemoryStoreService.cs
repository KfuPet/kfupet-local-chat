using System.IO;
using System.Text.Json;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>长期记忆的读写：单一 JSON 文件，按重要性排序，容量满时丢弃最低优先级的条目。</summary>
    public sealed class MemoryStoreService
    {
        public const int Capacity = 100;

        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>读取全部长期记忆，重要性高的在前。</summary>
        public IReadOnlyList<LongTermMemory> Load()
        {
            lock (Sync)
            {
                return Read()
                    .OrderByDescending(m => m.Importance)
                    .ThenByDescending(m => m.CreatedAt)
                    .ToList();
            }
        }

        /// <summary>写入新记忆（同内容去重），返回真正新增的条目。</summary>
        public IReadOnlyList<LongTermMemory> Add(IEnumerable<(string Content, int Importance)> items)
        {
            var added = new List<LongTermMemory>();

            lock (Sync)
            {
                var memories = Read();
                var existing = new HashSet<string>(
                    memories.Select(m => m.Content), StringComparer.OrdinalIgnoreCase);

                foreach (var (content, importance) in items)
                {
                    var text = content.Trim();
                    if (text.Length == 0 || !existing.Add(text))
                    {
                        continue;
                    }

                    var memory = new LongTermMemory
                    {
                        Content = text,
                        Importance = Math.Clamp(importance, 0, 5)
                    };
                    memories.Add(memory);
                    added.Add(memory);
                }

                if (added.Count == 0)
                {
                    return added;
                }

                // 容量满时优先丢弃重要性最低、时间最早的条目
                while (memories.Count > Capacity)
                {
                    var victim = memories
                        .OrderBy(m => m.Importance)
                        .ThenBy(m => m.CreatedAt)
                        .First();
                    memories.Remove(victim);
                }

                Write(memories);
                return added;
            }
        }

        public void Clear()
        {
            lock (Sync)
            {
                try
                {
                    if (File.Exists(AppPaths.MemoryPath))
                    {
                        File.Delete(AppPaths.MemoryPath);
                    }
                }
                catch
                {
                    // 删除失败时下次读取仍会拿到旧数据
                }
            }
        }

        private static List<LongTermMemory> Read()
        {
            try
            {
                if (!File.Exists(AppPaths.MemoryPath))
                {
                    return new List<LongTermMemory>();
                }

                return JsonSerializer.Deserialize<List<LongTermMemory>>(
                    File.ReadAllText(AppPaths.MemoryPath)) ?? new List<LongTermMemory>();
            }
            catch
            {
                // 记忆文件损坏时按空处理，不影响对话
                return new List<LongTermMemory>();
            }
        }

        private static void Write(List<LongTermMemory> memories)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                File.WriteAllText(AppPaths.MemoryPath,
                    JsonSerializer.Serialize(memories, JsonOptions));
            }
            catch
            {
                // 写入失败不阻断对话，本次记忆只在内存里丢掉
            }
        }
    }
}
