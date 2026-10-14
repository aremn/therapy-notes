using System;
using System.IO;

namespace AudioRecorderTrayApp.Services
{
    public static class RemoteLinkStore
    {
        private static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "links");

        private static string PathFor(string localId) => System.IO.Path.Combine(RootDir, Sanitize(localId) + ".txt");
        public static event Action<string, string>? Linked;
        public static void Save(string localId, string remoteId)
        {
            Directory.CreateDirectory(RootDir);
            File.WriteAllText(PathFor(localId), remoteId ?? string.Empty);
        }

        public static string? Load(string localId)
        {
            var p = PathFor(localId);
            if (!File.Exists(p))
                return null;
            return File.ReadAllText(p).Trim();
        }

        public static void MigrateLocalIdToRemote(string localId, string remoteId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(localId) || string.IsNullOrWhiteSpace(remoteId))
                    return;
                Directory.CreateDirectory(RootDir);
                Save(localId, remoteId);
                try
                {
                    if (!DialogueStore.MigrateId(localId, remoteId))
                    {
                        var content = DialogueStore.Load(localId);
                        if (!string.IsNullOrEmpty(content))
                            DialogueStore.Save(remoteId, content);
                    }
                }
                catch
                {
                }

                try
                {
                    MappingStore.Save(remoteId, MappingStore.Load(localId) ?? new System.Collections.Generic.Dictionary<string, string>());
                }
                catch
                {
                }

                try
                {
                    SegmentsStore.MigrateId(localId, remoteId);
                }
                catch
                {
                }

                try
                {
                    var cached = AnalysisStore.Load(localId);
                    if (!string.IsNullOrWhiteSpace(cached))
                        AnalysisStore.Save(remoteId, cached);
                }
                catch
                {
                }

                try
                {
                    var list = SessionStore.LoadAll();
                    var meta = list.FirstOrDefault(s => string.Equals(s.Id, localId, StringComparison.OrdinalIgnoreCase));
                    if (meta != null)
                    {
                        SessionStore.AddOrUpdate(new SessionStore.SessionMeta { Id = remoteId, CreatedAtUtc = meta.CreatedAtUtc, DurationSeconds = meta.DurationSeconds, Title = meta.Title, ClientId = meta.ClientId });
                        SessionStore.Remove(localId);
                    }
                }
                catch
                {
                }

                try
                {
                    Linked?.Invoke(localId, remoteId);
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private static string Sanitize(string id)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }
    }
}
