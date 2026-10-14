using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AudioRecorderTrayApp.Services
{
    public static class DialogueStore
    {
        private static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "dialogues");
        public static string RootPath => RootDir;

        public static void Save(string transcriptionId, string dialogue)
        {
            Directory.CreateDirectory(RootDir);
            var jsonPath = Path.Combine(RootDir, Sanitize(transcriptionId) + ".json");
            var payload = new DialoguePayload
            {
                Version = 1,
                Text = dialogue ?? string.Empty,
                SavedAtUtc = DateTime.UtcNow
            };
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(payload, options);
            File.WriteAllText(jsonPath, json, new UTF8Encoding(true));
        }

        public static bool Delete(string transcriptionId)
        {
            try
            {
                var sanitized = Sanitize(transcriptionId);
                var jsonPath = Path.Combine(RootDir, sanitized + ".json");
                var txtPath = Path.Combine(RootDir, sanitized + ".txt");
                var binPath = Path.Combine(RootDir, sanitized + ".bin");
                bool any = false;
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    any = true;
                }

                if (File.Exists(txtPath))
                {
                    File.Delete(txtPath);
                    any = true;
                }

                if (File.Exists(binPath))
                {
                    File.Delete(binPath);
                    any = true;
                }

                return any;
            }
            catch
            {
                return false;
            }
        }

        public static bool MigrateId(string oldId, string newId)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                var oldSan = Sanitize(oldId);
                var newSan = Sanitize(newId);
                var oldJson = Path.Combine(RootDir, oldSan + ".json");
                var newJson = Path.Combine(RootDir, newSan + ".json");
                if (!File.Exists(oldJson))
                {
                    var txt = Path.Combine(RootDir, oldSan + ".txt");
                    if (File.Exists(txt))
                    {
                        var txtContent = File.ReadAllText(txt, Encoding.UTF8);
                        Save(newId, txtContent);
                        File.Delete(txt);
                        return true;
                    }

                    return false;
                }

                var content = File.ReadAllText(oldJson, Encoding.UTF8);
                if (File.Exists(newJson))
                    File.Delete(newJson);
                File.WriteAllText(newJson, content, new UTF8Encoding(true));
                File.Delete(oldJson);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string? Load(string transcriptionId)
        {
            var sanitized = Sanitize(transcriptionId);
            var jsonPath = Path.Combine(RootDir, sanitized + ".json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var obj = System.Text.Json.JsonSerializer.Deserialize<DialoguePayload>(json);
                    if (obj != null && obj.Text != null)
                        return obj.Text;
                    var asString = System.Text.Json.JsonSerializer.Deserialize<string>(json);
                    return asString;
                }
                catch
                {
                    try
                    {
                        return File.ReadAllText(jsonPath, Encoding.UTF8);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            var txtPath = Path.Combine(RootDir, sanitized + ".txt");
            if (File.Exists(txtPath))
            {
                try
                {
                    var txtContent = File.ReadAllText(txtPath, Encoding.UTF8);
                    var payload = new DialoguePayload
                    {
                        Version = 1,
                        Text = txtContent ?? string.Empty,
                        SavedAtUtc = DateTime.UtcNow
                    };
                    var options = new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        WriteIndented = true
                    };
                    var json = JsonSerializer.Serialize(payload, options);
                    File.WriteAllText(jsonPath, json, new UTF8Encoding(true));
                    return txtContent;
                }
                catch
                {
                }
            }

            var legacyPath = Path.Combine(RootDir, sanitized + ".bin");
            if (!File.Exists(legacyPath))
                return null;
            var protectedBytes = File.ReadAllBytes(legacyPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            var text = Encoding.UTF8.GetString(bytes);
            try
            {
                Directory.CreateDirectory(RootDir);
                var payload = new DialoguePayload
                {
                    Version = 1,
                    Text = text ?? string.Empty,
                    SavedAtUtc = DateTime.UtcNow
                };
                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(payload, options);
                File.WriteAllText(jsonPath, json, new UTF8Encoding(true));
            }
            catch
            {
            }

            return text;
        }

        private static string Sanitize(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }

        private class DialoguePayload
        {
            public int Version { get; set; }
            public string? Text { get; set; }
            public DateTime SavedAtUtc { get; set; }
        }
    }
}
