using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;

namespace AudioRecorderTrayApp.Services
{
    public static class SupabaseAnalysisService
    {
        public sealed class AnalysisDto
        {
            public string? Summary { get; set; }
            public string[] Topics { get; set; } = Array.Empty<string>();
            public string? Sentiment { get; set; }
            public string[] ActionItems { get; set; } = Array.Empty<string>();
            public string? Status { get; set; }
            public string? Error { get; set; }
        }

        public static async Task<AnalysisDto?> GetByIdAsync(Guid id)
        {
            try
            {
                var user = App.Supabase.Auth.CurrentUser;
                if (user == null)
                    return null;
                var table = App.Supabase.From<TranscriptionEntity>();
                var resp = await table.Where(t => t.Id == id).Get();
                var row = resp.Models?.FirstOrDefault();
                if (row == null)
                    return null;
                var dto = new AnalysisDto
                {
                    Summary = row.Summary,
                    Sentiment = row.SentimentAnalysis,
                    Status = row.AnalysisStatus,
                    Error = row.ErrorMessage
                };
                dto.Topics = ParseJsonStringArray(row.KeyTopics);
                dto.ActionItems = ParseJsonStringArray(row.ActionItems);
                return dto;
            }
            catch (Postgrest.Exceptions.PostgrestException ex)when (Core.AuthHelper.IsJwtExpiredError(ex))
            {
                await Core.AuthHelper.HandleJwtExpiredAsync(ex);
                return null;
            }
            catch (Exception ex)
            {
                Services.Logger.Error($"Error in GetByIdAsync: {ex.Message}");
                return null;
            }
        }

        private static string[] ParseJsonStringArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<string>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return doc.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty) : e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                }
            }
            catch
            {
            }

            return json.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }
    }
}
