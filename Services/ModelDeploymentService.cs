using System.IO;
using System.Text.Json;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>管理已部署模型的记录文件与磁盘文件，卸载时一并清理。</summary>
    public sealed class ModelDeploymentService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>读取已部署模型；记录损坏或模型文件缺失时自动清理并返回 null。</summary>
        public DeployedModel? Load()
        {
            try
            {
                if (!File.Exists(AppPaths.DeployedConfigPath))
                {
                    return null;
                }

                var model = JsonSerializer.Deserialize<DeployedModel>(
                    File.ReadAllText(AppPaths.DeployedConfigPath));

                if (model == null || string.IsNullOrWhiteSpace(model.FilePath) || !File.Exists(model.FilePath))
                {
                    Clear();
                    return null;
                }

                return model;
            }
            catch
            {
                Clear();
                return null;
            }
        }

        /// <summary>模型在本地磁盘的落地路径。</summary>
        public string GetTargetPath(LocalModelOption option)
        {
            return Path.Combine(AppPaths.ModelsDirectory, option.FileName);
        }

        public void Save(DeployedModel model)
        {
            Directory.CreateDirectory(AppPaths.ConfigDirectory);
            File.WriteAllText(AppPaths.DeployedConfigPath, JsonSerializer.Serialize(model, JsonOptions));
        }

        /// <summary>删除部署记录，不动模型文件。</summary>
        public void Clear()
        {
            try
            {
                if (File.Exists(AppPaths.DeployedConfigPath))
                {
                    File.Delete(AppPaths.DeployedConfigPath);
                }
            }
            catch
            {
                // 记录删不掉时下次启动仍会按文件是否存在校验
            }
        }

        /// <summary>卸载：删除模型文件与部署记录。</summary>
        public void DeleteFiles(DeployedModel model)
        {
            try
            {
                if (File.Exists(model.FilePath))
                {
                    File.Delete(model.FilePath);
                }
            }
            finally
            {
                Clear();
            }
        }
    }
}
