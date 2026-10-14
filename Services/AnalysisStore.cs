using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AudioRecorderTrayApp.Services
{
    public static class AnalysisStore
    {
        private static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "analysis");
        public static string RootPath => RootDir;

        public static void Save(string sessionId, string analysisJson)
        {
            Directory.CreateDirectory(RootDir);
            var path = Path.Combine(RootDir, Sanitize(sessionId) + ".json");
            try
            {
                using var doc = JsonDocument.Parse(analysisJson ?? "{}");
                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                };
                var pretty = JsonSerializer.Serialize(doc.RootElement, options);
                File.WriteAllText(path, pretty, new System.Text.UTF8Encoding(true));
            }
            catch
            {
                File.WriteAllText(path, analysisJson ?? string.Empty, new System.Text.UTF8Encoding(true));
            }
        }

        public static void SaveRemote(string remoteId, string analysisJson) => Save($"remote_{remoteId}", analysisJson);
        public static string? Load(string sessionId)
        {
            var sanitized = Sanitize(sessionId);
            var jsonPath = Path.Combine(RootDir, sanitized + ".json");
            if (File.Exists(jsonPath))
                return File.ReadAllText(jsonPath, Encoding.UTF8);
            var legacyPath = Path.Combine(RootDir, sanitized + ".bin");
            if (!File.Exists(legacyPath))
                return null;
            var protectedBytes = File.ReadAllBytes(legacyPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(bytes);
            try
            {
                using var doc = JsonDocument.Parse(json ?? "{}");
                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                };
                var pretty = JsonSerializer.Serialize(doc.RootElement, options);
                File.WriteAllText(jsonPath, pretty, new System.Text.UTF8Encoding(true));
            }
            catch
            {
                try
                {
                    File.WriteAllText(jsonPath, json ?? string.Empty, new System.Text.UTF8Encoding(true));
                }
                catch
                {
                }
            }

            return json;
        }

        public static string? LoadRemote(string remoteId) => Load($"remote_{remoteId}");
        private static string Sanitize(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }
    }
}
