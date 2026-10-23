using System.Diagnostics;
using System.IO;
using System.Net.Http;
using kfupet_local_chat.Models;

namespace kfupet_local_chat.Services
{
    /// <summary>下载进度快照。</summary>
    public sealed record DownloadProgress(long ReceivedBytes, long TotalBytes, double BytesPerSecond)
    {
        public double Percent => TotalBytes > 0
            ? Math.Clamp(ReceivedBytes * 100d / TotalBytes, 0, 100)
            : 0;

        public string ReceivedText => $"{ReceivedBytes / 1024d / 1024d:0.0} MB";

        public string TotalText => TotalBytes > 0
            ? $"{TotalBytes / 1024d / 1024d / 1024d:0.00} GB"
            : "未知";

        public string SpeedText => BytesPerSecond > 0
            ? $"{BytesPerSecond / 1024d / 1024d:0.0} MB/s"
            : "—";
    }

    /// <summary>把 GGUF 模型下载到本地，支持多地址回退与取消。</summary>
    public static class ModelDownloadService
    {
        private static readonly HttpClient Client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        public static async Task DownloadAsync(
            LocalModelOption option,
            string targetPath,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var partPath = targetPath + ".part";
            Exception? lastError = null;

            foreach (var url in option.DownloadUrls)
            {
                try
                {
                    await DownloadFromUrlAsync(url, partPath, progress, cancellationToken);
                    File.Move(partPath, targetPath, overwrite: true);
                    return;
                }
                catch (OperationCanceledException)
                {
                    TryDelete(partPath);
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    TryDelete(partPath);
                }
            }

            throw new InvalidOperationException(
                lastError == null ? "没有可用的下载地址" : $"下载失败：{lastError.Message}", lastError);
        }

        private static async Task DownloadFromUrlAsync(
            string url,
            string partPath,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("KfuPetLocalChat/1.0");

            using var response = await Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long receivedBytes = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            long lastBytes = 0;
            double speed = 0;

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                receivedBytes += read;

                var elapsed = stopwatch.Elapsed;
                if (elapsed - lastReport >= TimeSpan.FromMilliseconds(250))
                {
                    speed = (receivedBytes - lastBytes) / (elapsed - lastReport).TotalSeconds;
                    lastReport = elapsed;
                    lastBytes = receivedBytes;
                    progress?.Report(new DownloadProgress(receivedBytes, totalBytes, speed));
                }
            }

            progress?.Report(new DownloadProgress(receivedBytes, totalBytes, speed));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 临时文件删不掉不影响后续重试
            }
        }
    }
}
