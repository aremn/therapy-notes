using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AudioRecorderTrayApp
{
    public enum GgmlType
    {
        Tiny,
        Base,
        Small,
        Medium,
        LargeV2,
        LargeV3
    }

    public static class ModelManager
    {
        private static readonly string _modelsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "models");
        private static readonly Dictionary<GgmlType, string> _modelUrls = new()
        {
            {
                GgmlType.Tiny,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"
            },
            {
                GgmlType.Base,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
            },
            {
                GgmlType.Small,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"
            },
            {
                GgmlType.Medium,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin"
            },
            {
                GgmlType.LargeV2,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v2.bin"
            },
            {
                GgmlType.LargeV3,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin"
            },
        };
        public static event Action<double>? OnDownloadProgress;
        public static string GetModelPath(GgmlType modelType)
        {
            return Path.Combine(_modelsDir, $"ggml-{modelType.ToString().ToLower()}.bin");
        }

        public static bool IsModelDownloaded(GgmlType modelType)
        {
            return File.Exists(GetModelPath(modelType));
        }

        public static async Task<string> EnsureModelDownloadedAsync(GgmlType modelType)
        {
            var modelPath = GetModelPath(modelType);
            if (File.Exists(modelPath))
            {
                Debug.WriteLine($"[ModelManager] Модель {modelType} уже на месте.");
                return modelPath;
            }

            Debug.WriteLine($"[ModelManager] Модель {modelType} не найдена. Начинаю загрузку...");
            Directory.CreateDirectory(_modelsDir);
            var url = _modelUrls[modelType];
            using (var client = new HttpClient())
            {
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var totalBytesRead = 0L;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            if (totalBytes != -1)
                            {
                                OnDownloadProgress?.Invoke((double)totalBytesRead / totalBytes * 100);
                            }
                        }
                    }
                }
            }

            OnDownloadProgress?.Invoke(100);
            Debug.WriteLine($"[ModelManager] Модель {modelType} успешно скачана.");
            return modelPath;
        }
    }
}
