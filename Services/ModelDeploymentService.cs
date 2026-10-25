using System.IO;
using System.Text.Json;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>deployed.json 的存储结构：多个已下载模型 + 当前使用的一个。</summary>
    internal sealed class DeployedModelLibrary
    {
        public List<DeployedModel> Models { get; set; } = new();

        public string? ActiveFileName { get; set; }
    }

    /// <summary>管理所有已下载模型：新增、切换当前模型、卸载单个模型。</summary>
    public sealed class ModelDeploymentService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly object _sync = new();

        public IReadOnlyList<DeployedModel> LoadAll()
        {
            lock (_sync)
            {
                return LoadValidated().Models.ToList();
            }
        }

        public DeployedModel? LoadActive()
        {
            lock (_sync)
            {
                var library = LoadValidated();
                return library.Models.FirstOrDefault(m => m.FileName == library.ActiveFileName)
                       ?? library.Models.FirstOrDefault();
            }
        }

        /// <summary>新增或更新模型记录，并把新下载的模型设为当前使用。</summary>
        public void Save(DeployedModel model)
        {
            lock (_sync)
            {
                var library = LoadValidated();
                library.Models.RemoveAll(m =>
                    string.Equals(m.FileName, model.FileName, StringComparison.OrdinalIgnoreCase));
                library.Models.Add(model);
                library.ActiveFileName = model.FileName;
                Write(library);
            }
        }

        public void SetActive(string fileName)
        {
            lock (_sync)
            {
                var library = LoadValidated();
                if (library.Models.Any(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    library.ActiveFileName = fileName;
                    Write(library);
                }
            }
        }

        /// <summary>删除单个模型文件与记录；若删的是当前模型，自动切换到剩余的第一个。</summary>
        public void DeleteFiles(DeployedModel model)
        {
            lock (_sync)
            {
                var library = LoadValidated();
                library.Models.RemoveAll(m =>
                    string.Equals(m.FileName, model.FileName, StringComparison.OrdinalIgnoreCase));
                library.ActiveFileName = library.Models.Count > 0 ? library.Models[0].FileName : null;
                Write(library);

                try
                {
                    if (File.Exists(model.FilePath))
                    {
                        File.Delete(model.FilePath);
                    }
                }
                catch
                {
                    // 文件删不掉时记录已移除，下次启动不会再引用它
                }
            }
        }

        /// <summary>模型在本地磁盘的落地路径；directory 为空时使用设置里的目录。</summary>
        public string GetTargetPath(LocalModelOption option, string? directory = null)
        {
            var root = string.IsNullOrWhiteSpace(directory)
                ? AppSettingsService.ResolveModelDirectory()
                : directory;
            return Path.Combine(root, option.FileName);
        }

        private DeployedModelLibrary LoadValidated()
        {
            // deployed.json 丢失时（例如旧版本全量清除过），按模型目录里已有的 GGUF 自动重建
            if (!File.Exists(AppPaths.DeployedConfigPath))
            {
                var recovered = TryRecoverFromModelDirectory();
                if (recovered != null)
                {
                    Write(recovered);
                    return recovered;
                }
            }

            var library = ReadLibrary();

            var missing = library.Models.RemoveAll(m => !File.Exists(m.FilePath));
            if (library.Models.Count == 0)
            {
                library.ActiveFileName = null;
            }
            else if (string.IsNullOrEmpty(library.ActiveFileName) ||
                     library.Models.All(m => m.FileName != library.ActiveFileName))
            {
                library.ActiveFileName = library.Models[0].FileName;
            }

            if (missing > 0)
            {
                Write(library);
            }

            return library;
        }

        /// <summary>扫描当前模型目录，把模型目录里定义的已知 GGUF 重新登记为已部署记录。</summary>
        private static DeployedModelLibrary? TryRecoverFromModelDirectory()
        {
            var root = AppSettingsService.ResolveModelDirectory();
            var library = new DeployedModelLibrary();

            foreach (var option in ModelCatalog.All)
            {
                var path = Path.Combine(root, option.FileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                library.Models.Add(new DeployedModel
                {
                    ModelName = option.Name,
                    FileName = option.FileName,
                    FilePath = path,
                    SizeBytes = new FileInfo(path).Length,
                    DeployedAt = File.GetLastWriteTime(path),
                    Parameters = option.Parameters,
                    Quantization = option.Quantization
                });
            }

            if (library.Models.Count == 0)
            {
                return null;
            }

            // 默认选目录中最大的已下载模型（目录顺序为 1.7B、4B、8B）
            library.ActiveFileName = library.Models[^1].FileName;
            return library;
        }

        /// <summary>读取配置；兼容早期只存单个模型的 deployed.json。</summary>
        private static DeployedModelLibrary ReadLibrary()
        {
            try
            {
                if (!File.Exists(AppPaths.DeployedConfigPath))
                {
                    return new DeployedModelLibrary();
                }

                var json = File.ReadAllText(AppPaths.DeployedConfigPath);

                try
                {
                    var library = JsonSerializer.Deserialize<DeployedModelLibrary>(json);
                    if (library?.Models is { Count: > 0 })
                    {
                        return library;
                    }
                }
                catch
                {
                    // 旧格式是单个对象，继续尝试下面的兼容读取
                }

                var single = JsonSerializer.Deserialize<DeployedModel>(json);
                if (single != null)
                {
                    return new DeployedModelLibrary
                    {
                        Models = new List<DeployedModel> { single },
                        ActiveFileName = single.FileName
                    };
                }
            }
            catch
            {
                // 配置损坏时按空库处理
            }

            return new DeployedModelLibrary();
        }

        private static void Write(DeployedModelLibrary library)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                File.WriteAllText(AppPaths.DeployedConfigPath,
                    JsonSerializer.Serialize(library, JsonOptions));
            }
            catch
            {
                // 记录写入失败不影响当前会话；下次启动会按文件存在性重新校验
            }
        }
    }
}
