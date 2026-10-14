using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Services;

namespace AudioRecorderTrayApp
{
    public static class RedactionService
    {
        public static async Task<string> RedactAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            try
            {
                var swAll = Stopwatch.StartNew();
                Logger.Info($"[Natasha] RedactAsync: begin len={text.Length}");
                var result = await TryNatashaRedactAsync(text, ct);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
                Logger.Warn("[Natasha] Fallback: returning original text (redaction failed).");
                return text;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Natasha] ERROR RedactAsync: {ex}");
                return text;
            }
            finally
            {
                Logger.Info("[Natasha] RedactAsync: end");
            }
        }

        public static async Task<string?> RedactFileAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Warn($"[Natasha] RedactFileAsync: file not found '{filePath}'");
                return null;
            }

            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "natasha_redact.py");
            if (!File.Exists(scriptPath))
            {
                Logger.Error($"[Natasha] Script not found: '{scriptPath}'");
                return null;
            }

            Logger.Info($"[Natasha] Processing file: {filePath}");
            try
            {
                var result = await PythonService.RunScriptAsync(scriptPath, $"\"{filePath}\"");
                if (!string.IsNullOrWhiteSpace(result))
                {
                    var cleaned = result.Replace("\uFFFD", string.Empty).Trim();
                    Logger.Info($"[Natasha] File processing completed, output length: {cleaned.Length}");
                    return cleaned;
                }

                Logger.Warn("[Natasha] File processing returned empty result");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Natasha] RedactFileAsync failed: {ex}");
                return null;
            }
        }

        private static async Task<string?> TryNatashaRedactAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "natasha_redact.py");
            if (!File.Exists(scriptPath))
            {
                Logger.Error($"[Natasha] Script not found: '{scriptPath}'");
                return null;
            }

            string? tempInputFile = null;
            try
            {
                tempInputFile = Path.Combine(Path.GetTempPath(), $"natasha_input_{Guid.NewGuid():N}.txt");
                await File.WriteAllTextAsync(tempInputFile, text, Encoding.UTF8, ct);
                Logger.Info($"[Natasha] Created temp input file: {tempInputFile}");
                if (AppSettings.Current.VerboseTranscriberLogging)
                {
                    var prev = text.Length > 200 ? text.Substring(0, 200) + "…" : text;
                    prev = prev.Replace("\r", " ").Replace("\n", " ");
                    Logger.Info($"[Natasha] input_len={text.Length}, preview=\"{prev}\"");
                }

                var result = await PythonService.RunScriptAsync(scriptPath, $"\"{tempInputFile}\"");
                if (!string.IsNullOrWhiteSpace(result))
                {
                    var cleaned = result.Replace("\uFFFD", string.Empty).Trim();
                    if (AppSettings.Current.VerboseTranscriberLogging)
                    {
                        int Count(string tag) => Regex.Matches(cleaned, "\\[" + tag + "\\]").Count;
                        var name = Count("NAME");
                        var date = Count("DATE");
                        var addr = Count("ADDRESS");
                        var email = Count("EMAIL");
                        var url = Count("URL");
                        var ip = Count("IP");
                        var card = Count("CARD");
                        var phone = Count("PHONE");
                        var outPrev = cleaned.Length > 200 ? cleaned.Substring(0, 200) + "…" : cleaned;
                        outPrev = outPrev.Replace("\r", " ").Replace("\n", " ");
                        Logger.Info($"[Natasha] tags NAME={name}, DATE={date}, ADDR={addr}, EMAIL={email}, URL={url}, IP={ip}, CARD={card}, PHONE={phone}");
                        Logger.Info($"[Natasha] output_len={cleaned.Length}, preview=\"{outPrev}\"");
                    }

                    return cleaned;
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Natasha] TryNatashaRedactAsync failed: {ex}");
                return null;
            }
            finally
            {
                if (tempInputFile != null && File.Exists(tempInputFile))
                {
                    try
                    {
                        File.Delete(tempInputFile);
                        Logger.Info($"[Natasha] Deleted temp input file: {tempInputFile}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[Natasha] Failed to delete temp input file {tempInputFile}: {ex}");
                    }
                }
            }
        }
    }
}
