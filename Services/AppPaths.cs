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

        public static string DeployedConfigPath => Path.Combine(ConfigDirectory, "deployed.json");
    }
}
