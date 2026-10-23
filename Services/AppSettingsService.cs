using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>试点台的用户设置：模型存放目录与推理设备。</summary>
    public sealed class AppSettings
    {
        /// <summary>自定义模型目录；为空时使用默认目录。</summary>
        public string? ModelDirectory { get; set; }

        public InferenceDevice Device { get; set; } = InferenceDevice.Auto;
    }

    /// <summary>读写 Config\settings.json，供部署窗口与引擎卡片共享。</summary>
    public static class AppSettingsService
    {
        private static readonly object SyncRoot = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static AppSettings? _current;

        public static AppSettings Current
        {
            get
            {
                lock (SyncRoot)
                {
                    return _current ??= Load();
                }
            }
        }

        /// <summary>实际使用的模型目录：用户选过就用用户的，否则走默认目录。</summary>
        public static string ResolveModelDirectory()
        {
            var directory = Current.ModelDirectory;
            return string.IsNullOrWhiteSpace(directory) ? AppPaths.ModelsDirectory : directory;
        }

        public static void Save()
        {
            lock (SyncRoot)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.ConfigDirectory);
                    File.WriteAllText(AppPaths.SettingsConfigPath,
                        JsonSerializer.Serialize(Current, JsonOptions));
                }
                catch
                {
                    // 设置写入失败不影响当前会话使用
                }
            }
        }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsConfigPath))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(AppPaths.SettingsConfigPath), JsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch
            {
                // 设置损坏时回落到默认值
            }

            return new AppSettings();
        }
    }
}
