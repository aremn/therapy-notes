using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AudioRecorderTrayApp.Models;

namespace AudioRecorderTrayApp.Services
{
    public static class DialogueFormatter
    {
        public static string ToPlainText(IEnumerable<SpeakerSegment> segments)
        {
            if (segments == null)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var s in segments)
            {
                var speaker = string.IsNullOrWhiteSpace(s.Speaker) ? "unknown" : s.Speaker;
                var text = s.Text?.Trim() ?? string.Empty;
                if (text.Length == 0)
                    continue;
                sb.Append(speaker);
                sb.Append(": ");
                sb.AppendLine(text);
            }

            return sb.ToString().Trim();
        }

        public static string RemoveLineNumbers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                var cleaned = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^\[\d+\]\s+", "");
                sb.AppendLine(cleaned);
            }

            return sb.ToString().Trim();
        }
    }
}
