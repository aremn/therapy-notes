using System;
using System.Collections.Generic;

namespace AudioRecorderTrayApp.Services
{
    public static class MappingUtils
    {
        public static string ApplyReverseMapping(string text, Dictionary<string, string>? aliasToOriginal)
        {
            if (string.IsNullOrEmpty(text) || aliasToOriginal == null || aliasToOriginal.Count == 0)
                return text ?? string.Empty;
            var ordered = new List<KeyValuePair<string, string>>(aliasToOriginal);
            ordered.Sort((a, b) => (b.Key?.Length ?? 0).CompareTo(a.Key?.Length ?? 0));
            var result = text;
            foreach (var kv in ordered)
            {
                var alias = kv.Key ?? string.Empty;
                var original = kv.Value ?? string.Empty;
                var token = "[" + alias + "]";
                result = result.Replace(token, original);
            }

            return result;
        }
    }
}
