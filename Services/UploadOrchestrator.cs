using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;
using AudioRecorderTrayApp.Core;

namespace AudioRecorderTrayApp.Services
{
    public static class UploadOrchestrator
    {
        public sealed class Output
        {
            public List<SpeakerSegment> Segments { get; set; } = new();
            public string PlainText { get; set; } = string.Empty;
            public AnonymizationService.Mapping Mapping { get; set; } = new();
            public int DurationSeconds { get; set; }
            public string Id { get; set; } = string.Empty;
        }

        public static async Task<Output> ProcessAndUploadAsync(string therapistWav, string clientWav, int? durationOverrideSeconds = null, CancellationToken ct = default)
        {
            var therapist = await TranscriptionService.TranscribeFileToSegmentsAsync(therapistWav, "therapist", ct);
            var client = await TranscriptionService.TranscribeFileToSegmentsAsync(clientWav, "client", ct);
            Logger.Info($"Anonymization start: therapistSegs={therapist.Count} clientSegs={client.Count}");
            var pipeline = await ClientPipeline.BuildAsync(therapist, client, ct);
            Logger.Info($"Anonymization done: textLen={pipeline.PlainText.Length} mapCount={pipeline.Mapping.Map.Count}");
            var lastEnd = 0.0;
            foreach (var s in pipeline.Segments)
                if (s.End > lastEnd)
                    lastEnd = s.End;
            var duration = durationOverrideSeconds ?? (int)Math.Round(lastEnd);
            var aliasToOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in pipeline.Mapping.Map)
            {
                aliasToOriginal[kv.Value] = kv.Key;
            }

            var id = Guid.NewGuid().ToString("D");
            Logger.Info($"Local persist begin id={id}");
            try
            {
                MappingStore.DebugDump(id, aliasToOriginal);
                MappingStore.Save(id, aliasToOriginal);
            }
            catch (Exception ex)
            {
                Logger.Error($"MappingStore.Save failed for id={id}: {ex.Message}");
            }

            try
            {
                SegmentsStore.Save(id, pipeline.Segments);
            }
            catch (Exception ex)
            {
                Logger.Warn($"SegmentsStore.Save failed for id={id}: {ex.Message}");
            }

            Logger.Info($"Local persist end id={id}");
            try
            {
                var currentClient = CurrentClientService.CurrentClient;
                var localClientId = currentClient?.Id;
                var remoteClientId = currentClient?.RemoteId;
                SessionStore.AddOrUpdate(new SessionStore.SessionMeta { Id = id, CreatedAtUtc = DateTime.UtcNow, DurationSeconds = duration, Title = $"Сессия {DateTime.Now:dd.MM.yyyy HH:mm}", ClientId = localClientId });
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string textToAnalyze = pipeline.PlainText;
                        if (AppSettings.Current.UseTextEnhancement)
                        {
                            try
                            {
                                Logger.Info("Enhancing anonymized text...");
                                var enhancedText = await TextEnhancementService.EnhanceAsync(pipeline.PlainText);
                                if (!string.IsNullOrWhiteSpace(enhancedText))
                                {
                                    textToAnalyze = enhancedText;
                                    Logger.Info($"Text enhanced: {pipeline.PlainText.Length} -> {enhancedText.Length} chars");
                                    try
                                    {
                                        DialogueStore.Save(id, enhancedText);
                                        Logger.Info($"Enhanced text saved locally for id={id}");
                                    }
                                    catch (Exception saveEx)
                                    {
                                        Logger.Error($"Failed to save enhanced text for id={id}: {saveEx.Message}");
                                    }

                                    try
                                    {
                                        var updated = TryApplyEnhancedTextToSegments(enhancedText, pipeline.Segments);
                                        if (updated != null)
                                        {
                                            SegmentsStore.Save(id, updated);
                                            Logger.Info($"Segments updated with enhanced text for id={id}");
                                        }
                                    }
                                    catch (Exception segEx)
                                    {
                                        Logger.Warn($"Failed to update segments with enhanced text for id={id}: {segEx.Message}");
                                    }
                                }
                                else
                                {
                                    Logger.Warn("Text enhancement returned empty result, using original text");
                                    try
                                    {
                                        DialogueStore.Save(id, pipeline.PlainText);
                                    }
                                    catch (Exception saveEx)
                                    {
                                        Logger.Error($"Failed to save anonymized text for id={id}: {saveEx.Message}");
                                    }
                                }
                            }
                            catch (Exception enhanceEx)
                            {
                                Logger.Warn($"Text enhancement failed, using original text: {enhanceEx.Message}");
                                try
                                {
                                    DialogueStore.Save(id, pipeline.PlainText);
                                }
                                catch (Exception saveEx)
                                {
                                    Logger.Error($"Failed to save anonymized text for id={id}: {saveEx.Message}");
                                }
                            }
                        }
                        else
                        {
                            Logger.Info("Text enhancement disabled, using original anonymized text");
                            try
                            {
                                DialogueStore.Save(id, pipeline.PlainText);
                            }
                            catch (Exception saveEx)
                            {
                                Logger.Error($"Failed to save anonymized text for id={id}: {saveEx.Message}");
                            }
                        }

                        Logger.Info("Sending text for analysis...");
                        await TextUploader.UploadAsync(textToAnalyze, duration, id, remoteClientId);
                        Logger.Info("Text sent for analysis successfully");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Analysis upload failed (will be ignored): {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save session meta or start upload task: {ex.Message}");
            }

            return new Output
            {
                Segments = pipeline.Segments,
                PlainText = pipeline.PlainText,
                Mapping = pipeline.Mapping,
                DurationSeconds = duration,
                Id = id
            };
        }

        public static async Task<Output> ProcessAndUploadTextAsync(string rawText, CancellationToken ct = default)
        {
            Logger.Info("Starting text processing pipeline.");
            var segments = new List<SpeakerSegment>();
            var lines = rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            double pseudoTime = 0.0;
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                string speaker = "unknown";
                string content = trimmedLine;
                var lowerTrimmedLine = trimmedLine.ToLowerInvariant();
                if (lowerTrimmedLine.StartsWith("therapist:") || lowerTrimmedLine.StartsWith("терапевт:") || lowerTrimmedLine.StartsWith("them:"))
                {
                    speaker = "therapist";
                    content = trimmedLine.Substring(trimmedLine.IndexOf(':') + 1).Trim();
                }
                else if (lowerTrimmedLine.StartsWith("client:") || lowerTrimmedLine.StartsWith("клиент:") || lowerTrimmedLine.StartsWith("me:"))
                {
                    speaker = "client";
                    content = trimmedLine.Substring(trimmedLine.IndexOf(':') + 1).Trim();
                }
                else
                {
                    Logger.Warn($"Line does not specify speaker, marking as 'unknown': '{trimmedLine}'");
                }

                if (speaker != "unknown")
                {
                    var start = pseudoTime;
                    var end = pseudoTime + 1.0;
                    segments.Add(new SpeakerSegment { Speaker = speaker, Text = content, Start = start, End = end });
                    pseudoTime = end;
                }
            }

            if (segments.Count == 0)
            {
                throw new InvalidOperationException("The provided text is empty or in an incorrect format.");
            }

            Logger.Info($"Parsed {lines.Length} lines from file into {segments.Count} speaker segments.");
            var therapistSegments = segments.Where(s => s.Speaker == "therapist").ToList();
            var clientSegments = segments.Where(s => s.Speaker == "client").ToList();
            Logger.Info($"Anonymization start: therapistSegs={therapistSegments.Count} clientSegs={clientSegments.Count}");
            var pipeline = await ClientPipeline.BuildAsync(therapistSegments, clientSegments, ct);
            Logger.Info($"Anonymization done: textLen={pipeline.PlainText.Length} mapCount={pipeline.Mapping.Map.Count}");
            var duration = segments.Count * 5;
            var aliasToOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in pipeline.Mapping.Map)
            {
                aliasToOriginal[kv.Value] = kv.Key;
            }

            var id = Guid.NewGuid().ToString("D");
            Logger.Info($"Local persist begin id={id}");
            try
            {
                MappingStore.DebugDump(id, aliasToOriginal);
                MappingStore.Save(id, aliasToOriginal);
            }
            catch (Exception ex)
            {
                Logger.Error($"MappingStore.Save failed for id={id}: {ex.Message}");
            }

            try
            {
                SegmentsStore.Save(id, pipeline.Segments);
            }
            catch (Exception ex)
            {
                Logger.Warn($"SegmentsStore.Save failed for id={id}: {ex.Message}");
            }

            Logger.Info($"Local persist end id={id}");
            try
            {
                var currentClient = CurrentClientService.CurrentClient;
                var localClientId = currentClient?.Id;
                var remoteClientId = currentClient?.RemoteId;
                SessionStore.AddOrUpdate(new SessionStore.SessionMeta { Id = id, CreatedAtUtc = DateTime.UtcNow, DurationSeconds = duration, Title = $"Сессия (текст) {DateTime.Now:dd.MM.yyyy HH:mm}", ClientId = localClientId });
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string textToAnalyze = pipeline.PlainText;
                        if (AppSettings.Current.UseTextEnhancement)
                        {
                            try
                            {
                                var textToEnhanceSnippet = pipeline.PlainText.Length > 200 ? pipeline.PlainText.Substring(0, 200) + "..." : pipeline.PlainText;
                                Logger.Info($"Enhancing anonymized text (length: {pipeline.PlainText.Length}). Snippet: '{textToEnhanceSnippet}'");
                                var enhancedText = await TextEnhancementService.EnhanceAsync(pipeline.PlainText);
                                if (!string.IsNullOrWhiteSpace(enhancedText))
                                {
                                    textToAnalyze = enhancedText;
                                    var enhancedTextSnippet = enhancedText.Length > 200 ? enhancedText.Substring(0, 200) + "..." : enhancedText;
                                    Logger.Info($"Text enhanced: {pipeline.PlainText.Length} -> {enhancedText.Length} chars. Snippet: '{enhancedTextSnippet}'");
                                    try
                                    {
                                        DialogueStore.Save(id, enhancedText);
                                        Logger.Info($"Enhanced text saved locally for id={id}");
                                    }
                                    catch (Exception saveEx)
                                    {
                                        Logger.Error($"Failed to save enhanced text for id={id}: {saveEx.Message}");
                                    }

                                    try
                                    {
                                        var updated = TryApplyEnhancedTextToSegments(enhancedText, pipeline.Segments);
                                        if (updated != null)
                                        {
                                            SegmentsStore.Save(id, updated);
                                            Logger.Info($"Segments updated with enhanced text for id={id}");
                                        }
                                    }
                                    catch (Exception segEx)
                                    {
                                        Logger.Warn($"Failed to update segments with enhanced text for id={id}: {segEx.Message}");
                                    }
                                }
                                else
                                {
                                    Logger.Warn("Text enhancement returned empty result, using original text");
                                    try
                                    {
                                        DialogueStore.Save(id, pipeline.PlainText);
                                    }
                                    catch (Exception saveEx)
                                    {
                                        Logger.Error($"Failed to save anonymized text for id={id}: {saveEx.Message}");
                                    }
                                }
                            }
                            catch (Exception enhanceEx)
                            {
                                Logger.Warn($"Text enhancement failed, using original text: {enhanceEx.Message}");
                                try
                                {
                                    DialogueStore.Save(id, pipeline.PlainText);
                                }
                                catch (Exception saveEx)
                                {
                                    Logger.Error($"Failed to save anonymized text for id={id}: {saveEx.Message}");
                                }
                            }
                        }
                        else
                        {
                            Logger.Info("Text enhancement disabled, using original anonymized text");
                            try
                            {
                                DialogueStore.Save(id, pipeline.PlainText);
                            }
                            catch (Exception saveEx)
                            {
                                Logger.Error($"Failed to save anonymized text for id={id}: {saveEx.Message}");
                            }
                        }

                        var textToAnalyzeSnippetFinal = textToAnalyze.Length > 200 ? textToAnalyze.Substring(0, 200) + "..." : textToAnalyze;
                        Logger.Info($"Final text to analyze length: {textToAnalyze.Length}. Snippet: '{textToAnalyzeSnippetFinal}'");
                        Logger.Info("Sending text for analysis...");
                        await TextUploader.UploadAsync(textToAnalyze, duration, id, remoteClientId);
                        Logger.Info("Text sent for analysis successfully");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Analysis upload failed (will be ignored): {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save session meta or start upload task: {ex.Message}");
            }

            return new Output
            {
                Segments = pipeline.Segments,
                PlainText = pipeline.PlainText,
                Mapping = pipeline.Mapping,
                DurationSeconds = duration,
                Id = id
            };
        }

        public static List<SpeakerSegment>? TryApplyEnhancedTextToSegments(string enhancedText, List<SpeakerSegment> originalSegments)
        {
            if (string.IsNullOrWhiteSpace(enhancedText) || originalSegments == null || originalSegments.Count == 0)
                return null;
            var cleaned = DialogueFormatter.RemoveLineNumbers(enhancedText);
            var rawLines = cleaned.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (rawLines.Count == 0)
                return null;
            var parsed = new List<(string role, string content)>();
            foreach (var raw in rawLines)
            {
                var lower = raw.ToLowerInvariant();
                string? role = null;
                string content = raw;
                if (lower.StartsWith("therapist:") || lower.StartsWith("терапевт:"))
                {
                    role = "therapist";
                    content = raw.Substring(raw.IndexOf(':') + 1).Trim();
                }
                else if (lower.StartsWith("client:") || lower.StartsWith("клиент:"))
                {
                    role = "client";
                    content = raw.Substring(raw.IndexOf(':') + 1).Trim();
                }
                else
                {
                    continue;
                }

                if (content.Length >= 2 && ((content.StartsWith("\"") && content.EndsWith("\"")) || (content.StartsWith("“") && content.EndsWith("”"))))
                {
                    content = content.Substring(1, content.Length - 2).Trim();
                }

                parsed.Add((role, content));
            }

            if (parsed.Count == 0)
                return null;
            var therapistIdx = originalSegments.Select((s, i) => (s, i)).Where(x => string.Equals(x.s.Speaker, "therapist", StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToList();
            var clientIdx = originalSegments.Select((s, i) => (s, i)).Where(x => string.Equals(x.s.Speaker, "client", StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToList();
            var fastUpdated = originalSegments.Select(s => new SpeakerSegment { Speaker = s.Speaker, Start = s.Start, End = s.End, Text = s.Text }).ToList();
            bool canFastMap = parsed.Count <= originalSegments.Count;
            if (canFastMap)
            {
                int tPtr = 0, cPtr = 0, anyPtr = 0;
                foreach (var(role, content)in parsed)
                {
                    if (role == "therapist" && tPtr < therapistIdx.Count)
                    {
                        var idx = therapistIdx[tPtr++];
                        fastUpdated[idx].Speaker = "therapist";
                        fastUpdated[idx].Text = content;
                    }
                    else if (role == "client" && cPtr < clientIdx.Count)
                    {
                        var idx = clientIdx[cPtr++];
                        fastUpdated[idx].Speaker = "client";
                        fastUpdated[idx].Text = content;
                    }
                    else if (anyPtr < fastUpdated.Count)
                    {
                        var idx = anyPtr++;
                        fastUpdated[idx].Speaker = role;
                        fastUpdated[idx].Text = content;
                    }
                    else
                    {
                        canFastMap = false;
                        break;
                    }
                }
            }

            if (canFastMap)
                return fastUpdated;
            var assigned = new Dictionary<int, List<(string role, string content)>>();
            bool hasTherapistInOrig = therapistIdx.Count > 0;
            bool hasClientInOrig = clientIdx.Count > 0;
            var allIdx = Enumerable.Range(0, originalSegments.Count).ToList();
            int tP = 0, cP = 0, aP = 0;
            foreach (var(role, content)in parsed)
            {
                int container;
                if (role == "therapist" && hasTherapistInOrig)
                {
                    container = tP < therapistIdx.Count ? therapistIdx[tP++] : therapistIdx[^1];
                }
                else if (role == "client" && hasClientInOrig)
                {
                    container = cP < clientIdx.Count ? clientIdx[cP++] : clientIdx[^1];
                }
                else
                {
                    container = aP < allIdx.Count ? allIdx[aP++] : allIdx[^1];
                }

                if (!assigned.TryGetValue(container, out var list))
                {
                    list = new List<(string role, string content)>();
                    assigned[container] = list;
                }

                list.Add((role, content));
            }

            var result = new List<SpeakerSegment>(parsed.Count);
            for (int i = 0; i < originalSegments.Count; i++)
            {
                if (!assigned.TryGetValue(i, out var lines) || lines.Count == 0)
                    continue;
                var src = originalSegments[i];
                var start = src.Start;
                var end = src.End;
                var duration = Math.Max(0, end - start);
                var lengths = lines.Select(l => Math.Max(1, l.content.Length)).ToArray();
                double total = lengths.Sum();
                if (duration > 0 && total > 0)
                {
                    double cursor = start;
                    for (int k = 0; k < lines.Count; k++)
                    {
                        var(role, content) = lines[k];
                        var slice = duration * (lengths[k] / total);
                        var seg = new SpeakerSegment
                        {
                            Speaker = role,
                            Start = cursor,
                            End = (k == lines.Count - 1) ? end : cursor + slice,
                            Text = content
                        };
                        result.Add(seg);
                        cursor = seg.End;
                    }
                }
                else
                {
                    foreach (var(role, content)in lines)
                    {
                        result.Add(new SpeakerSegment { Speaker = role, Start = start, End = end, Text = content });
                    }
                }
            }

            return result.Count > 0 ? result : null;
        }
    }
}
