using System.IO;
using System.Text.Json;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>归档记忆文件的读取。</summary>
    public static class ArchiveMemoryService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>读取归档 JSON；文件不存在或格式错误时抛异常，由调用方提示。</summary>
        public static IReadOnlyList<ArchiveMemoryEntry> Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ArchiveMemoryEntry>>(json, JsonOptions)
                   ?? new List<ArchiveMemoryEntry>();
        }
    }
}
