using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;

namespace AudioRecorderTrayApp.Services
{
    public static class AnonymizationService
    {
        public sealed class Mapping
        {
            public Dictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string, int> _counters = new();
            private readonly Dictionary<string, HashSet<string>> _entityParts = new();
            public string GetOrAdd(string original, string tagPrefix)
            {
                string key = tagPrefix == "PERSON" ? NormalizeName(original) : original;
                if (Map.TryGetValue(key, out var alias))
                    return alias;
                if (tagPrefix == "PERSON")
                {
                    var existingAlias = TryResolvePartialName(key);
                    if (existingAlias != null)
                        return existingAlias;
                }

                if (!_counters.TryGetValue(tagPrefix, out var count))
                    count = 0;
                count++;
                _counters[tagPrefix] = count;
                var next = $"{tagPrefix}_{count}";
                Map[key] = next;
                if (tagPrefix == "PERSON")
                {
                    RememberNameParts(key, next);
                }

                return next;
            }

            private static string NormalizeName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return name;
                return string.Join(" ", name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
            }

            private string? TryResolvePartialName(string name)
            {
                var nameParts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nameParts.Length == 1)
                {
                    var singleWord = nameParts[0].ToLower();
                    foreach (var kvp in Map)
                    {
                        var existingParts = kvp.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (existingParts.Length > 1 && existingParts.Any(part => part.ToLower() == singleWord))
                        {
                            Map[name] = kvp.Value;
                            return kvp.Value;
                        }
                    }
                }

                return null;
            }

            private void RememberNameParts(string fullName, string alias)
            {
                var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var lowerPart = part.ToLower();
                    if (!_entityParts.ContainsKey(lowerPart))
                        _entityParts[lowerPart] = new HashSet<string>();
                    _entityParts[lowerPart].Add(alias);
                }
            }
        }

        public static async Task<string> ApplyNatashaAndBuildMappingAsync(string input, Mapping mapping, CancellationToken ct = default)
        {
            if (!AppSettings.Current.UseNatasha)
                return input ?? string.Empty;
            var redacted = await RedactionService.RedactAsync(input ?? string.Empty, ct).ConfigureAwait(false);
            var withSurface = Regex.Replace(redacted, @"\[(PER|ORG|LOC):([^\]]+)\]", m =>
            {
                var kind = MapKind(m.Groups[1].Value);
                var surface = m.Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(surface))
                    return m.Value;
                var alias = mapping.GetOrAdd(surface, kind);
                return $"[{alias}]";
            });
            return withSurface;
        }

        private static string MapKind(string natashaKind)
        {
            return natashaKind switch
            {
                "PER" => "PERSON",
                "ORG" => "ORG",
                "LOC" => "LOC",
                _ => natashaKind?.ToUpperInvariant() ?? "ENTITY"
            };
        }

        public static async Task<List<SpeakerSegment>> AnonymizeSegmentsAsync(IEnumerable<SpeakerSegment> segments, Mapping mapping, CancellationToken ct = default)
        {
            var result = new List<SpeakerSegment>();
            foreach (var s in segments)
            {
                var text = s.Text ?? string.Empty;
                var anon = await ApplyNatashaAndBuildMappingAsync(text, mapping, ct).ConfigureAwait(false);
                result.Add(new SpeakerSegment { Speaker = s.Speaker, Start = s.Start, End = s.End, Text = anon });
            }

            return result;
        }

        public static async Task<(List<SpeakerSegment> segments, string plainText, Mapping mapping)> AnonymizeWholeTextAsync(IEnumerable<SpeakerSegment> mergedSegments, CancellationToken ct = default)
        {
            var mapping = new Mapping();
            var ordered = mergedSegments?.OrderBy(s => s.Start).ToList() ?? new List<SpeakerSegment>();
            var sb = new System.Text.StringBuilder();
            var ranges = new List<(int start, int length)>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var t = ordered[i].Text ?? string.Empty;
                var start = sb.Length;
                sb.Append(t);
                ranges.Add((start, t.Length));
                if (i < ordered.Count - 1)
                    sb.Append('\n');
            }

            var whole = sb.ToString();
            var redacted = await ApplyNatashaAndBuildMappingAsync(whole, mapping, ct).ConfigureAwait(false);
            var parts = redacted.Split('\n');
            var outSegs = new List<SpeakerSegment>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                var text = i < parts.Length ? parts[i] : string.Empty;
                outSegs.Add(new SpeakerSegment { Speaker = ordered[i].Speaker, Start = ordered[i].Start, End = ordered[i].End, Text = text });
            }

            var plain = DialogueFormatter.ToPlainText(outSegs);
            return (outSegs, plain, mapping);
        }
    }
}
