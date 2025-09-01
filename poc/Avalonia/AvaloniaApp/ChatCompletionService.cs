using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AvaloniaApp
{

    public class ChatCompletionService
    {
        private readonly IChatClient _chatClient;

        public ChatCompletionService(string endpoint, string apiKey, string model)
        {
             var httpClient = new HttpClient();
             httpClient.BaseAddress = new Uri(endpoint);
             httpClient.Timeout = TimeSpan.FromSeconds(30);
             httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

             _chatClient = new OllamaApiClient(httpClient, model);
        }

        public IList<ChatMessage> Initialize()
        {
             return new List<ChatMessage>()
            {
                new ChatMessage(ChatRole.System, "You are a helpful assistant. Tasks:" +
                                      "- main goal is to provide me a 1-5 smart questions that I can ask " +
                                      "- less questions is better but try to make them IQ 150 " +
                                      "- please use language that chat is done " +
                                      // "- add short explanation why question is valid" +
                                      "- sentence started with [m] is my text, [o] is others" +
                                      "- focus on [o] and don't repeat what [m] already asked " +
                                      "- for debug purposes add below each question reason why this question is relevant in format '[d] text'"),
            };
        }

        public async Task<string> GetCompletionAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            try
            {
                var response =
                    await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
                //ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, requestOptions, cancellationToken);
                return response.Text;
            }
            catch (Exception ex)
            {
                return $"Error in ChatCompletionService: {ex.Message}";
            }
        }
    }
}
