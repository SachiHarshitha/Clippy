using Clippy.Core.Interfaces;
using Clippy.Core.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Windows.Security.Credentials;

namespace Clippy.Services
{
    public class ChatService : IChatService
    {
        private readonly HttpClient _httpClient;
        private const string ApiEndpoint = "https://openrouter.ai/api/v1/chat/completions";
        private const string ModelId = "deepseek/deepseek-r1-0528:free";
        private readonly IKeyService _keyService;
        private string _apiKey = string.Empty;

        private class OpenRouterMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class OpenRouterResponse
        {
            [JsonPropertyName("choices")]
            public List<Choice> Choices { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("message")]
            public OpenRouterMessage Message { get; set; }

            [JsonPropertyName("delta")]
            public OpenRouterMessage Delta { get; set; }
        }

        public ChatService(IKeyService keyService)
        {
            _keyService = keyService;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/mediaexplorer74/clippy");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Clippy");

            // Load API key from KeyService
            var key = _keyService.GetKey();
            _apiKey = key ?? string.Empty;
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            }
        }

        private void RefreshApiKey()
        {
            var key = _keyService.GetKey() ?? string.Empty;
            if (!string.Equals(_apiKey, key, StringComparison.Ordinal))
            {
                _apiKey = key;
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
                }
                else
                {
                    _httpClient.DefaultRequestHeaders.Authorization = null;
                }
            }
        }

        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey ?? string.Empty;
            _keyService.SetKey(_apiKey);

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
        }

        public async Task<string> SendChatAsync(IEnumerable<IMessage> messages)
        {
            RefreshApiKey();
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("OpenRouter API key is not set. Please set the API key first.");

            var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);

            var requestBody = new
            {
                model = ModelId,
                messages = messages.Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.MessageText//m.Content
                }),
                temperature = 0.7,
                stream = false
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8,
                "application/json");

            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObject = JsonSerializer.Deserialize<OpenRouterResponse>(responseContent);

            return responseObject?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response from the model.";
        }

        public async IAsyncEnumerable<string> StreamChatAsync(IEnumerable<IMessage> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RefreshApiKey();
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("OpenRouter API key is not set. Please set the API key first.");

            var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);

            var requestBody = new
            {
                model = ModelId,
                messages = messages.Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.MessageText//m.Content
                }),
                temperature = 0.7,
                stream = true
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8,
                "application/json");

            request.Content = content;

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                    continue;

                var eventData = line[6..]; // Remove "data: " prefix
                if (eventData == "[DONE]")
                    yield break;

                string chunk = null;
                try
                {
                    var json = JsonSerializer.Deserialize<OpenRouterResponse>(eventData);
                    chunk = json?.Choices?.FirstOrDefault()?.Delta?.Content;
                }
                catch (JsonException)
                {
                    // Skip any malformed JSON
                }

                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }
}