using System;
using System.Collections.Generic;
using System.Linq;
using AudioRecorderTrayApp.Models;

namespace AudioRecorderTrayApp.Services
{
    public static class DiarizationService
    {
        public static List<SpeakerSegment> MergeTimeline(IEnumerable<SpeakerSegment> therapist, IEnumerable<SpeakerSegment> client)
        {
            var merged = new List<SpeakerSegment>();
            if (therapist != null)
            {
                merged.AddRange(therapist.Select(s => new SpeakerSegment { Speaker = string.IsNullOrWhiteSpace(s.Speaker) ? "therapist" : s.Speaker, Text = s.Text ?? string.Empty, Start = s.Start, End = s.End }));
            }

            if (client != null)
            {
                merged.AddRange(client.Select(s => new SpeakerSegment { Speaker = string.IsNullOrWhiteSpace(s.Speaker) ? "client" : s.Speaker, Text = s.Text ?? string.Empty, Start = s.Start, End = s.End }));
            }

            merged.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
            var cleaned = new List<SpeakerSegment>(merged.Count);
            SpeakerSegment? last = null;
            var lastPerSpeaker = new System.Collections.Generic.Dictionary<string, SpeakerSegment>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in merged)
            {
                var spk = string.IsNullOrWhiteSpace(s.Speaker) ? "therapist" : s.Speaker;
                s.Speaker = spk;
                s.Text = s.Text?.Trim() ?? string.Empty;
                if (last != null && string.Equals(last.Speaker, spk, StringComparison.OrdinalIgnoreCase) && Math.Abs(last.Start - s.Start) < 0.01 && Math.Abs(last.End - s.End) < 0.01 && string.Equals(last.Text, s.Text, StringComparison.Ordinal))
                {
                    continue;
                }

                if (last != null && string.Equals(last.Speaker, spk, StringComparison.OrdinalIgnoreCase) && s.Start - last.End <= 0.25)
                {
                    if (string.Equals(last.Text, s.Text, StringComparison.Ordinal))
                    {
                        last.End = Math.Max(last.End, s.End);
                        continue;
                    }
                }

                if (s.End - s.Start < 0.10)
                    continue;
                bool isNearDupSameSpeaker = false;
                if (lastPerSpeaker.TryGetValue(spk, out var prevSame))
                {
                    if (s.Start - prevSame.Start <= 3.0)
                    {
                        var a = Normalize(prevSame.Text);
                        var b = Normalize(s.Text);
                        if (a.Length > 0 && a == b)
                        {
                            if (s.Start - prevSame.End <= 1.0)
                            {
                                prevSame.End = Math.Max(prevSame.End, s.End);
                            }

                            isNearDupSameSpeaker = true;
                        }
                    }
                }

                if (isNearDupSameSpeaker)
                {
                    last = cleaned.Count > 0 ? cleaned[cleaned.Count - 1] : null;
                    continue;
                }

                cleaned.Add(s);
                last = s;
                lastPerSpeaker[spk] = s;
            }

            return cleaned;
        }

        private static string Normalize(string? t)
        {
            if (string.IsNullOrWhiteSpace(t))
                return string.Empty;
            var s = t.ToLowerInvariant().Trim();
            var arr = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch) || ch == ' ')
                    arr.Append(ch);
            }

            var norm = System.Text.RegularExpressions.Regex.Replace(arr.ToString(), "\\s+", " ").Trim();
            return norm;
        }
    }
}
