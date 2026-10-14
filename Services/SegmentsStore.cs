using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AudioRecorderTrayApp.Models;

namespace AudioRecorderTrayApp.Services
{
    public static class SegmentsStore
    {
        private static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "segments");
        public static string RootPath => RootDir;

        public static void Save(string sessionId, IList<SpeakerSegment> segments)
        {
            Directory.CreateDirectory(RootDir);
            var path = Path.Combine(RootDir, Sanitize(sessionId) + ".bin");
            var json = JsonSerializer.Serialize(segments ?? new List<SpeakerSegment>());
            var bytes = Encoding.UTF8.GetBytes(json);
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, protectedBytes);
        }

        public static IList<SpeakerSegment>? Load(string sessionId)
        {
            var path = Path.Combine(RootDir, Sanitize(sessionId) + ".bin");
            if (!File.Exists(path))
                return null;
            var protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<List<SpeakerSegment>>(json);
        }

        public static bool MigrateId(string oldId, string newId)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                var oldPath = Path.Combine(RootDir, Sanitize(oldId) + ".bin");
                var newPath = Path.Combine(RootDir, Sanitize(newId) + ".bin");
                if (!File.Exists(oldPath))
                    return false;
                if (File.Exists(newPath))
                    File.Delete(newPath);
                File.Move(oldPath, newPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Sanitize(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }
    }
}
