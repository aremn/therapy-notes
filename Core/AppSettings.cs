using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Postgrest.Attributes;
using Postgrest.Models;

namespace AudioRecorderTrayApp
{
    public class AppConfiguration
    {
        public HotkeySettings Hotkey { get; set; } = new HotkeySettings();
        public string Theme { get; set; } = "Системная";
        public string? SelectedMicrophoneId { get; set; }
        public double MicrophoneVolume { get; set; } = 1.0;
        public double SystemVolume { get; set; } = 1.0;
        public bool AutoRecordOnCall { get; set; } = false;
        public System.Windows.Point? IndicatorPosition { get; set; }
        public bool EncryptMetadata { get; set; } = true;
        public string? ProtectedClientKey { get; set; }

        [JsonIgnore]
        public bool EncryptAudio => true;

        [JsonIgnore]
        public bool RedactTranscripts => true;
        public bool VerboseTranscriberLogging { get; set; } = false;

        [JsonIgnore]
        public bool UseNatasha => true;

        [JsonIgnore]
        public bool NatashaWholeText => true;

        [JsonIgnore]
        public bool NumberTranscriptForLLM => true;

        [JsonIgnore]
        public bool UseTextEnhancement => true;
        public string PythonPath { get; set; } = ".venv\\Scripts\\python.exe";
        public string NatashaScript { get; set; } = "natasha_redact.py";
        public string EnhancerScript { get; set; } = "enhancer.py";
        public string? SelectedClientId { get; set; }
    }

    public class HotkeySettings
    {
        public Key Key { get; set; } = Key.F2;
        public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Shift;
    }

    public static class AppSettings
    {
        public static event Action<HotkeySettings>? HotkeyChanged;
        private static readonly string _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "settings.json");
        public static AppConfiguration Current { get; private set; } = new AppConfiguration();

        private static byte[]? _clientKey;
        public static void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    Current = JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
                }
            }
            catch
            {
                Current = new AppConfiguration();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                string json = JsonSerializer.Serialize(Current);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
            }
        }

        public static void SaveAndNotifyHotkey(HotkeySettings newHotkey)
        {
            Current.Hotkey = newHotkey;
            Save();
            HotkeyChanged?.Invoke(Current.Hotkey);
        }

        public static async Task<byte[]> GetOrCreateClientKeyAsync()
        {
            if (_clientKey != null)
                return _clientKey;
            var user = App.Supabase.Auth.CurrentUser;
            if (user == null)
                throw new Exception("Пользователь не аутентифицирован для получения ключа.");
            if (!string.IsNullOrEmpty(Current.ProtectedClientKey))
            {
                try
                {
                    var protectedBytes = Convert.FromBase64String(Current.ProtectedClientKey);
                    var unprotected = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    if (unprotected.Length == 32)
                    {
                        _clientKey = unprotected;
                        Debug.WriteLine("[AppSettings] Ключ восстановлен из локального хранилища.");
                        return _clientKey;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AppSettings] Не удалось расшифровать локальный ключ: {ex.Message}");
                }
            }

            Debug.WriteLine("[AppSettings] Ключ не найден. Генерирую новый...");
            _clientKey = RandomNumberGenerator.GetBytes(32);
            try
            {
                var protectedNew = ProtectedData.Protect(_clientKey, null, DataProtectionScope.CurrentUser);
                Current.ProtectedClientKey = Convert.ToBase64String(protectedNew);
                Save();
                Debug.WriteLine("[AppSettings] Новый ключ сохранен локально.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Ошибка сохранения локального ключа: {ex.Message}");
            }

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", App.Supabase.Auth.CurrentSession!.AccessToken);
                var edgeUrl = App.SupabaseBaseUrl + "/functions/v1/save-client-key";
                var payload = new
                {
                    clientKeyBase64 = Convert.ToBase64String(_clientKey)
                };
                var resp = await http.PostAsJsonAsync(edgeUrl, payload);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[AppSettings] save-client-key failed: {resp.StatusCode} {err}");
                }
                else
                {
                    Debug.WriteLine("[AppSettings] Client key saved on server.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Не удалось сохранить ключ на сервере: {ex.Message}");
            }

            return _clientKey;
        }
    }

    [Table("profiles")]
    public class Profile : BaseModel
    {
        [PrimaryKey("id")]
        public Guid Id { get; set; }

        [Column("client_key")]
        public string? ClientKey { get; set; }
    }
}
