using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using OpenAI.Chat;

namespace AvaloniaApp
{
    public class ChatCompletionService
    {
        private readonly ChatClient _chatClient;

        public ChatCompletionService(string endpoint, string apiKey, string deploymentName)
        {
            AzureOpenAIClient azureClient = new(
                new Uri(endpoint),
                new AzureKeyCredential(apiKey));
            _chatClient = azureClient.GetChatClient(deploymentName);
        }

        public IList<ChatMessage> Initialize()
        {
             return new List<ChatMessage>()
            {
                new SystemChatMessage("You are a helpful assistant. Tasks:" +
                                      "- main goal is to provide me a 1-5 smart questions that I can ask " +
                                      "- less questions is better but try to make them IQ 150 " +
                                      "- please use language that chat is done " +
                                      // "- add short explanation why question is valid" +
                                      "- sentence started with [m] is my text, [o] is others" +
                                      "- focus on [o] and don't repeat what [m] already asked " +
                                      "- questions should be in format '[q][matching indicator in %] text'" +
                                      "- for debug purposes add below each question reason why this question is relevant in format '[d] text'"),
            };
        }

        public async Task<string> GetCompletionAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            try
            {
                var requestOptions = new ChatCompletionOptions()
                {
                    MaxOutputTokenCount = 10000,
                };

#pragma warning disable AOAI001
                requestOptions.SetNewMaxCompletionTokensPropertyEnabled(true);
#pragma warning restore AOAI001

                ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, requestOptions, cancellationToken);
                return completion.Content[0].Text;
            }
            catch (Exception ex)
            {
                return $"Error in ChatCompletionService: {ex.Message}";
            }
        }
    }
}
