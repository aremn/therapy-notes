using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Postgrest.Exceptions;
using AudioRecorderTrayApp.Core;

namespace AudioRecorderTrayApp
{
    public static class TextUploader
    {
        public static event Action? AnalysisCompleted;
        public static event Action? AnalysisFailed;
        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public static async Task SaveMappingRowAsync(string transcriptionId, string mappingJson)
        {
            try
            {
                var clientKey = await AppSettings.GetOrCreateClientKeyAsync();
                var enc = EncryptString(mappingJson ?? "{}", clientKey);
                if (!Guid.TryParse(transcriptionId, out var guid))
                    return;
                var envelope = System.Text.Json.JsonSerializer.Serialize(new { ct = enc.Ct, nonce = enc.Nonce, tag = enc.Tag, schema = 1 });
                var row = new AudioRecorderTrayApp.Models.TranscriptionRow
                {
                    Id = guid,
                    Mapping = envelope
                };
                var table = App.Supabase.From<AudioRecorderTrayApp.Models.TranscriptionRow>();
                const int maxAttempts = 5;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var updateResp = await table.Update(row);
                        var affected = updateResp.Models?.Count ?? 0;
                        if (affected > 0)
                        {
                            Services.Logger.Info($"SaveMappingRowAsync OK (updated {affected} row): id={transcriptionId} bytes={mappingJson?.Length ?? 0}");
                            break;
                        }
                        else if (attempt == maxAttempts)
                        {
                            Services.Logger.Warn($"SaveMappingRowAsync: no rows updated after {maxAttempts} attempts (id={transcriptionId}). Row may not exist or RLS blocks.");
                        }
                    }
                    catch (PostgrestException pex)
                    {
                        var msg = pex.Content ?? pex.Message;
                        if (msg?.Contains("row-level security", StringComparison.OrdinalIgnoreCase) == true || msg?.Contains("42501") == true)
                        {
                            Services.Logger.Error($"SaveMappingRowAsync RLS blocked update for id={transcriptionId}: {msg}");
                            break;
                        }

                        if (attempt == maxAttempts)
                        {
                            Services.Logger.Error($"SaveMappingRowAsync failed after {maxAttempts} attempts for id={transcriptionId}: {msg}");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (attempt == maxAttempts)
                            Services.Logger.Error($"SaveMappingRowAsync failed after {maxAttempts} attempts for id={transcriptionId}: {ex.Message}");
                    }

                    await Task.Delay(700);
                }
            }
            catch (Exception ex)
            {
                Services.Logger.Error($"SaveMappingRowAsync failed: {ex.Message}");
            }
        }

        private sealed class EncPayload
        {
            public string Ct { get; set; } = string.Empty;
            public string Nonce { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
        }

        private static EncPayload EncryptString(string plaintext, byte[] key)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var pt = System.Text.Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
            var ct = new byte[pt.Length];
            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Encrypt(nonce, pt, ct, tag);
            return new EncPayload
            {
                Ct = Convert.ToBase64String(ct),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag)
            };
        }

        public static async Task UploadAsync(string rawText, int durationSeconds, string? localSessionId = null, string? remoteClientId = null)
        {
            try
            {
                var user = App.Supabase.Auth.CurrentUser;
                if (user == null)
                    throw new Exception("Пользователь не аутентифицирован.");
                if (string.IsNullOrWhiteSpace(rawText))
                    return;
                var anonymizedText = rawText;
                if (AppSettings.Current.NumberTranscriptForLLM)
                {
                    anonymizedText = NumberLines(anonymizedText);
                }

                try
                {
                    var preview = anonymizedText?.Length > 300 ? anonymizedText.Substring(0, 300) + "..." : anonymizedText;
                    Services.Logger.Info($"UploadAsync PRE-SEND: length={anonymizedText?.Length ?? 0} preview=\"{preview}\"");
                    Debug.WriteLine("--- ANONYMIZED TEXT (FULL) ---\n" + (anonymizedText ?? string.Empty) + "\n--- END ANONYMIZED TEXT ---");
                }
                catch
                {
                }

                var clientKey = await AppSettings.GetOrCreateClientKeyAsync();
                var enc = EncryptString(anonymizedText ?? string.Empty, clientKey);
                var payload = new
                {
                    payload_ct = enc.Ct,
                    payload_nonce = enc.Nonce,
                    payload_tag = enc.Tag,
                    duration_seconds = durationSeconds,
                    client_id = remoteClientId
                };
                var edgeFunctionUrl = App.SupabaseBaseUrl + "/functions/v1/submit-for-analysis";
                var byteLen = System.Text.Encoding.UTF8.GetByteCount(anonymizedText ?? string.Empty);
                Services.Logger.Info($"UploadAsync: url={edgeFunctionUrl} duration={durationSeconds} textBytes={byteLen}");
                var maxAttempts = 3;
                var delayMs = 2000;
                string? okBody = null;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    using var httpClient = new HttpClient
                    {
                        Timeout = TimeSpan.FromMinutes(5)
                    };
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", App.Supabase.Auth.CurrentSession!.AccessToken);
                    Services.Logger.Info($"UploadAsync attempt {attempt}/{maxAttempts}...");
                    try
                    {
                        var started = DateTime.UtcNow;
                        Services.Logger.Info($"UploadAsync HTTP POST begin at {started:O}");
                        var response = await httpClient.PostAsJsonAsync(edgeFunctionUrl, payload);
                        var ended = DateTime.UtcNow;
                        Services.Logger.Info($"UploadAsync HTTP POST end at {ended:O} took={(ended - started).TotalMilliseconds:F0}ms status={(int)response.StatusCode} {response.StatusCode}");
                        if ((int)response.StatusCode >= 500)
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            Services.Logger.Warn($"UploadAsync server error {response.StatusCode}: {Truncate(body, 400)}");
                            if (attempt == maxAttempts)
                                throw new Exception($"Ошибка сервера: {response.StatusCode}");
                        }
                        else if (!response.IsSuccessStatusCode)
                        {
                            var errorBody = await response.Content.ReadAsStringAsync();
                            Services.Logger.Error($"UploadAsync failed: {response.StatusCode} {Truncate(errorBody, 600)}");
                            throw new Exception($"Ошибка сервера: {response.StatusCode}. {errorBody}");
                        }
                        else
                        {
                            okBody = await response.Content.ReadAsStringAsync();
                            if (AppSettings.Current.VerboseTranscriberLogging)
                            {
                                Services.Logger.Info("UploadAsync OK, response body >>>\n" + okBody + "\n<<< END RESPONSE");
                            }
                            else
                            {
                                Services.Logger.Info($"UploadAsync OK, response preview: {Truncate(okBody, 800)}");
                            }

                            break;
                        }
                    }
                    catch (TaskCanceledException tex)
                    {
                        Services.Logger.Warn($"UploadAsync timeout on attempt {attempt}: {tex.Message}");
                        if (attempt == maxAttempts)
                            throw;
                    }
                    catch (HttpRequestException hrex)
                    {
                        Services.Logger.Warn($"UploadAsync network error on attempt {attempt}: {hrex.Message}");
                        if (attempt == maxAttempts)
                            throw;
                    }

                    await Task.Delay(delayMs);
                    delayMs = Math.Min(delayMs * 2, 10000);
                }

                if (okBody == null)
                    throw new Exception("Не удалось получить ответ от сервера");
                Services.Logger.Info($"UploadAsync OK: {Truncate(okBody, 800)}");
                try
                {
                    using var doc = JsonDocument.Parse(okBody);
                    var root = doc.RootElement;
                    static string? GetAnyId(JsonElement e)
                    {
                        string? id = TryGetString(e, "transcriptionId") ?? TryGetString(e, "transcription_id") ?? TryGetString(e, "recording_id") ?? TryGetString(e, "id");
                        if (!string.IsNullOrWhiteSpace(id))
                            return id;
                        if (e.ValueKind == JsonValueKind.Object)
                        {
                            if (e.TryGetProperty("data", out var d))
                            {
                                id = TryGetString(d, "transcriptionId") ?? TryGetString(d, "transcription_id") ?? TryGetString(d, "recording_id") ?? TryGetString(d, "id");
                                if (!string.IsNullOrWhiteSpace(id))
                                    return id;
                            }

                            if (e.TryGetProperty("result", out var r))
                            {
                                id = TryGetString(r, "transcriptionId") ?? TryGetString(r, "transcription_id") ?? TryGetString(r, "recording_id") ?? TryGetString(r, "id");
                                if (!string.IsNullOrWhiteSpace(id))
                                    return id;
                            }
                        }

                        return null;
                    }

                    var remoteId = GetAnyId(root);
                    if (!string.IsNullOrWhiteSpace(remoteId) && !string.IsNullOrWhiteSpace(localSessionId))
                    {
                        Services.RemoteLinkStore.Save(localSessionId, remoteId);
                        Services.RemoteLinkStore.MigrateLocalIdToRemote(localSessionId, remoteId);
                        Services.Logger.Info($"Remote link saved and data migrated: local={localSessionId} -> remote={remoteId}");
                    }
                    else
                    {
                        Services.Logger.Warn($"UploadAsync: remote id not found in response; analytics will not auto-link to local session {localSessionId}");
                    }
                }
                catch (Exception ex)
                {
                    Services.Logger.Warn($"UploadAsync: failed to parse remote id from response: {ex.Message}");
                }

                Debug.WriteLine($"Ответ от Edge Function: {okBody}");
                System.Windows.Application.Current.Dispatcher.Invoke(() => new NotificationWindow("Анализ успешно завершен!").Show());
                try
                {
                    AnalysisCompleted?.Invoke();
                }
                catch
                {
                }
            }
            catch (Exception ex)when (AuthHelper.IsJwtExpiredError(ex))
            {
                await AuthHelper.HandleJwtExpiredAsync(ex);
            }
            catch (Exception ex)
            {
                Services.Logger.Error($"UploadAsync exception: {ex}");
                Debug.WriteLine($"!!! ОШИБКА ОТПРАВКИ ТЕКСТА: {ex}");
                System.Windows.Application.Current.Dispatcher.Invoke(() => new NotificationWindow($"Ошибка отправки: {ex.Message}").Show());
            }
        }

        private static string NumberLines(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input ?? string.Empty;
            var lines = input.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new System.Text.StringBuilder(input.Length + lines.Length * 6);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (line.Length == 0)
                    continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\[\d+\]\s"))
                {
                    sb.AppendLine(line);
                }
                else
                {
                    sb.Append('[').Append(i).Append("] ").AppendLine(line);
                }
            }

            return sb.ToString();
        }

        public static async Task<string?> UploadWithMappingAsync(string dialoguePlainText, Dictionary<string, string> mapping, int durationSeconds)
        {
            try
            {
                var user = App.Supabase.Auth.CurrentUser;
                if (user == null)
                    throw new Exception("Пользователь не аутентифицирован.");
                if (string.IsNullOrWhiteSpace(dialoguePlainText))
                    return null;
                var clientKey = await AppSettings.GetOrCreateClientKeyAsync();
                var encDialogue = EncryptString(dialoguePlainText ?? string.Empty, clientKey);
                var mappingJson = JsonSerializer.Serialize(mapping ?? new Dictionary<string, string>());
                var encMapping = EncryptString(mappingJson ?? string.Empty, clientKey);
                var body = new
                {
                    payload_ct = encDialogue.Ct,
                    payload_nonce = encDialogue.Nonce,
                    payload_tag = encDialogue.Tag,
                    mapping_ct = encMapping.Ct,
                    mapping_nonce = encMapping.Nonce,
                    mapping_tag = encMapping.Tag,
                    mapping_schema_version = 1,
                    duration_seconds = durationSeconds
                };
                var unencryptedDataForLog = new
                {
                    dialoguePlainText,
                    mapping,
                    durationSeconds
                };
                string logMessage = $@"
                ================= PRE-ENCRYPTION PAYLOAD (START) =================
                Timestamp: {DateTime.UtcNow:o}
                Uploading data to Edge Function 'submit-for-analysis'.

                --- DIALOGUE PLAIN TEXT ---
                {dialoguePlainText}

                --- MAPPING JSON ---
                {mappingJson}

                --- DURATION ---
                {durationSeconds} seconds

                ================== PRE-ENCRYPTION PAYLOAD (END) ==================
                ";
                Services.Logger.Info(logMessage);
                System.Diagnostics.Debug.WriteLine(logMessage);
                var edgeFunctionUrl = App.SupabaseBaseUrl + "/functions/v1/submit-for-analysis";
                Services.Logger.Info($"UploadWithMapping: duration={durationSeconds} dialogueLen={(dialoguePlainText?.Length ?? 0)} mappingCount={(mapping?.Count ?? 0)}");
                var maxAttempts2 = 3;
                var delayMs2 = 2000;
                HttpResponseMessage? response2 = null;
                string? content = null;
                for (int attempt = 1; attempt <= maxAttempts2; attempt++)
                {
                    using var httpClient2 = new HttpClient
                    {
                        Timeout = TimeSpan.FromMinutes(5)
                    };
                    httpClient2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", App.Supabase.Auth.CurrentSession!.AccessToken);
                    Services.Logger.Info($"UploadWithMapping attempt {attempt}/{maxAttempts2}...");
                    try
                    {
                        response2 = await httpClient2.PostAsJsonAsync(edgeFunctionUrl, body);
                        if ((int)response2.StatusCode >= 500)
                        {
                            var bodyText = await response2.Content.ReadAsStringAsync();
                            Services.Logger.Warn($"UploadWithMapping server error {response2.StatusCode}: {Truncate(bodyText, 400)}");
                            if (attempt == maxAttempts2)
                                throw new Exception($"Ошибка сервера: {response2.StatusCode}");
                        }
                        else if (!response2.IsSuccessStatusCode)
                        {
                            var errorBody = await response2.Content.ReadAsStringAsync();
                            Services.Logger.Error($"UploadWithMapping failed: {response2.StatusCode} {Truncate(errorBody, 600)}");
                            throw new Exception($"Ошибка сервера: {response2.StatusCode}. {errorBody}");
                        }
                        else
                        {
                            content = await response2.Content.ReadAsStringAsync();
                            Services.Logger.Info("UploadWithMapping OK");
                            break;
                        }
                    }
                    catch (TaskCanceledException tex)
                    {
                        Services.Logger.Warn($"UploadWithMapping timeout on attempt {attempt}: {tex.Message}");
                        if (attempt == maxAttempts2)
                            throw;
                    }
                    catch (HttpRequestException hrex)
                    {
                        Services.Logger.Warn($"UploadWithMapping network error on attempt {attempt}: {hrex.Message}");
                        if (attempt == maxAttempts2)
                            throw;
                    }

                    await Task.Delay(delayMs2);
                    delayMs2 = Math.Min(delayMs2 * 2, 10000);
                }

                if (content == null)
                    throw new Exception("Не удалось получить ответ от сервера");
                Debug.WriteLine($"Ответ от Edge Function: {content}");
                Services.Logger.Info($"UploadWithMapping OK: {content}");
                string? id = null;
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    id = TryGetString(root, "transcriptionId") ?? TryGetString(root, "transcription_id") ?? TryGetString(root, "id") ?? TryGetString(root, "recording_id");
                }
                catch
                {
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() => new NotificationWindow("Анализ успешно завершен!").Show());
                try
                {
                    AnalysisCompleted?.Invoke();
                }
                catch
                {
                }

                return id;
            }
            catch (Exception ex)
            {
                Services.Logger.Error($"UploadWithMapping exception: {ex}");
                Debug.WriteLine($"!!! ОШИБКА ОТПРАВКИ ТЕКСТА/СЛОВАРЯ: {ex}");
                System.Windows.Application.Current.Dispatcher.Invoke(() => new NotificationWindow($"Ошибка отправки: {ex.Message}").Show());
                try
                {
                    AnalysisFailed?.Invoke();
                }
                catch
                {
                }
            }

            return null;
        }

        private static string? TryGetString(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (!root.TryGetProperty(name, out var v))
                return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        }
    }
}
