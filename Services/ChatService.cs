using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AudioRecorderTrayApp.Services
{
    public static class ChatService
    {
        private static readonly HttpClient _httpClient = new();
        private class ChatRequest
        {
            public string transcriptionId { get; set; } = string.Empty;
            public string userQuestion { get; set; } = string.Empty;
        }

        private class ChatResponse
        {
            public string? answer { get; set; }
            public string? error { get; set; }
        }

        public static async Task<string?> AskAboutAnalysisAsync(string sessionId, string question)
        {
            if (App.Supabase.Auth.CurrentSession?.AccessToken == null)
            {
                throw new InvalidOperationException("Пользователь не авторизован.");
            }

            var edgeFunctionUrl = App.SupabaseBaseUrl + "/functions/v1/query-analysis-report";
            var request = new HttpRequestMessage(HttpMethod.Post, edgeFunctionUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Supabase.Auth.CurrentSession.AccessToken);
            request.Content = JsonContent.Create(new ChatRequest { transcriptionId = sessionId, userQuestion = question });
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Ошибка сервера ({response.StatusCode}): {errorBody}");
            }

            var responseBody = await response.Content.ReadFromJsonAsync<ChatResponse>();
            if (!string.IsNullOrEmpty(responseBody?.error))
            {
                throw new Exception($"Ошибка от ассистента: {responseBody.error}");
            }

            return responseBody?.answer;
        }
    }
}
