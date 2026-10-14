using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AudioRecorderTrayApp.Services
{
    public static class MappingStore
    {
        private static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "mappings");
        public static string RootPath => RootDir;

        public static void Save(string recordingId, Dictionary<string, string> aliasToOriginal)
        {
            Directory.CreateDirectory(RootDir);
            var path = Path.Combine(RootDir, Sanitize(recordingId) + ".json");
            try
            {
                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(aliasToOriginal, options);
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(true));
                try
                {
                    var fi = new FileInfo(path);
                    AppendLog($"SAVE id={recordingId} count={(aliasToOriginal?.Count ?? 0)} path={path} size={fi.Length}");
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                AppendLog($"SAVE_FAIL id={recordingId} path={path} ex={ex.GetType().Name}:{ex.Message}");
                throw;
            }
        }

        public static Dictionary<string, string>? Load(string recordingId)
        {
            Directory.CreateDirectory(RootDir);
            var sanitized = Sanitize(recordingId);
            var jsonPath = Path.Combine(RootDir, sanitized + ".json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    AppendLog($"LOAD_OK id={recordingId} path={jsonPath} count={(dict?.Count ?? 0)}");
                    return dict;
                }
                catch (Exception ex)
                {
                    AppendLog($"LOAD_FAIL id={recordingId} path={jsonPath} ex={ex.GetType().Name}:{ex.Message}");
                    return null;
                }
            }

            var legacyPath = Path.Combine(RootDir, sanitized + ".bin");
            if (!File.Exists(legacyPath))
            {
                AppendLog($"LOAD_MISSING id={recordingId} path={jsonPath}; legacy={legacyPath}");
                return null;
            }

            try
            {
                var protectedBytes = File.ReadAllBytes(legacyPath);
                try
                {
                    AppendLog($"LOAD_READ_LEGACY id={recordingId} path={legacyPath} bytes={protectedBytes.Length}");
                }
                catch
                {
                }

                var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(bytes);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                AppendLog($"LOAD_OK_LEGACY id={recordingId} path={legacyPath} count={(dict?.Count ?? 0)}");
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        WriteIndented = true
                    };
                    var pretty = JsonSerializer.Serialize(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(), options);
                    File.WriteAllText(jsonPath, pretty, new System.Text.UTF8Encoding(true));
                }
                catch
                {
                }

                return dict;
            }
            catch (Exception ex)
            {
                AppendLog($"LOAD_FAIL_LEGACY id={recordingId} path={legacyPath} ex={ex.GetType().Name}:{ex.Message}");
                return null;
            }
        }

        private static string Sanitize(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }

        private static void AppendLog(string text)
        {
            try
            {
                var logPath = Path.Combine(RootDir, "mappingstore.log");
                var line = $"[{DateTime.UtcNow:O}] {text}" + Environment.NewLine;
                File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch
            {
            }
        }

        public static void DebugDump(string recordingId, Dictionary<string, string>? aliasToOriginal)
        {
            try
            {
                var logPath = Path.Combine(RootDir, "mappingstore.log");
                var count = aliasToOriginal?.Count ?? 0;
                var sample = aliasToOriginal == null ? new Dictionary<string, int>() : aliasToOriginal.Take(5).ToDictionary(kv => kv.Key, kv => kv.Value?.Length ?? 0);
                var payload = new
                {
                    id = recordingId,
                    count,
                    sample
                };
                var line = $"[{DateTime.UtcNow:O}] DEBUG_DUMP {JsonSerializer.Serialize(payload)}" + Environment.NewLine;
                File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
