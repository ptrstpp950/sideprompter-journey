using Microsoft.Extensions.AI;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Services
{

    public class LoggingHttpMessageHandler : DelegatingHandler
    {
        public LoggingHttpMessageHandler() : base(new HttpClientHandler()) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Log the request
            var requestContent = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "No content";
            Trace.WriteLine($"HTTP Request: {request.Method} {request.RequestUri} - Headers: {string.Join(", ", request.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))} - Content: {requestContent}");

            var response = await base.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            Trace.WriteLine($"HTTP Response: {response.StatusCode} - {responseContent}");
            return response;
        }
    }

    public class ChatCompletionService
    {
        private readonly IChatClient _chatClient;

        public ChatCompletionService(string endpoint, string apiKey, string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "-"; // Default to empty string if API key is not provided
            }
            _chatClient = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint),
                })
                .GetChatClient(model)
                .AsIChatClient();
            //_chatClient = new OllamaApiClient(httpClient, model);
        }

        private List<ChatMessage> Initialize()
        {
             return
             [
                 /*new ChatMessage(ChatRole.System, "You are a helpful assistant. Tasks:" +
                                               "- main goal is to provide me a 1-5 smart questions that I can ask " +
                                               "- less questions is better but try to make them IQ 150 " +
                                               "- please use language that chat is done " +
                                               // "- add short explanation why question is valid" +
                                               "- sentence started with [m] is my text, [o] is others " +
                                               "- focus on [o] and don't repeat what [m] already asked " +
                                               "- for debug purposes add below each question reason why this question is relevant in format '[d] text' " +
                                               "- focus more on more recent messages " +
                                               "- skip question that was already asked until they are more relevant now - mark them ")*/

                 new ChatMessage(ChatRole.System, "You are a \"Second Brain\" AI assistant. " +
                                                  "Your role is to provide immediate and accurate answers to questions that arise during a meeting.\n\n" +
                                                  "**Task:**\nBased on the user's query, provide a concise and factual answer using ONLY the information from the provided context. " +
                                                  "If the information is not available, state \"I don't have that information.\" " +
                                                  "If you use external knowledge give source as a link."),
                 new ChatMessage(ChatRole.System, "**Details:**\n\n" +
                                                  "My audio transcription starts with [m], others with [o], your previous responses with [ai]. " +
                                                  "Make answers short and brief. Use plain text.")

             ];
        }

        public async Task<string> GetCompletionAsync(IList<string> messages, CancellationToken cancellationToken = default)
        {
            try
            {
                var chatMessages = Initialize();
                chatMessages.AddRange(messages.Select(message => new ChatMessage(ChatRole.User, message)));

                // Get the response from the chat client
                var response =
                    await _chatClient.GetResponseAsync(chatMessages, cancellationToken: cancellationToken);

                // Add the assistant's response to the conversation
                chatMessages.AddMessages(response);

                return response.Text;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception in ChatCompletionService: {ex}");
                return $"Error in ChatCompletionService: {ex.Message}";
            }
        }
    }
}
