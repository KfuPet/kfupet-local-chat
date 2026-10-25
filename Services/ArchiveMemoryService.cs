using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>归档记忆文件的读取与另存，绝不修改源文件。</summary>
    public static class ArchiveMemoryService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>提炼后的归档另存路径：放在项目 data 目录，源文件保持不变。</summary>
        public static string RebuiltArchivePath =>
            Path.Combine(AppPaths.ProjectRoot, "data", "ArchiveMemory.rebuilt.json");

        /// <summary>读取归档 JSON；文件不存在或格式错误时抛异常，由调用方提示。</summary>
        public static IReadOnlyList<ArchiveMemoryEntry> Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ArchiveMemoryEntry>>(json, JsonOptions)
                   ?? new List<ArchiveMemoryEntry>();
        }

        /// <summary>把重建后的归档写到指定路径。</summary>
        public static void Save(string path, IReadOnlyList<ArchiveMemoryEntry> entries)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
        }
    }
}
