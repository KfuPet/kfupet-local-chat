using System.IO;

namespace kfupet_local_chat.Services
{
    /// <summary>试点台的数据目录：模型与部署记录都放在 %AppData%\KfuPetLocalChat 下。</summary>
    public static class AppPaths
    {
        public static string BaseDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KfuPetLocalChat");

        public static string ModelsDirectory => Path.Combine(BaseDirectory, "Models");

        public static string ConfigDirectory => Path.Combine(BaseDirectory, "Config");

        /// <summary>项目根目录：开发运行时从 exe 所在目录向上找到 csproj 的位置。</summary>
        public static string ProjectRoot { get; } = ResolveProjectRoot();

        public static string DeployedConfigPath => Path.Combine(ConfigDirectory, "deployed.json");

        public static string SettingsConfigPath => Path.Combine(ConfigDirectory, "settings.json");

        /// <summary>历次完整运行的摘要，用于导出报告里的多模型对比。</summary>
        public static string RunHistoryPath => Path.Combine(ConfigDirectory, "runhistory.json");

        /// <summary>主对话的长期记忆。</summary>
        public static string MemoryPath => Path.Combine(ConfigDirectory, "memories.json");

        private static string ResolveProjectRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "kfupet-local-chat.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
