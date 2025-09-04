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

                /* new ChatMessage(ChatRole.System, "You are a \"Second Brain\" AI assistant. " +
                                                  "Your role is to provide immediate and accurate answers to questions that arise during a meeting.\n\n" +
                                                  "**Task:**\nBased on the user's query, provide a concise and factual answer using ONLY the information from the provided context. " +
                                                  "If the information is not available, state \"I don't have that information.\" " +
                                                  "If you use external knowledge give source as a link."),
                 new ChatMessage(ChatRole.System, "**Details:**\n\n" +
                                                  "My audio transcription starts with [m], others with [o], your previous responses with [ai]. " +
                                                  "Make answers short and brief. Use plain text.")*/
                 new ChatMessage(ChatRole.System, "# System Prompt: Rozmowa z Kandydatem na Co-foundera\n" +
                                                  "## Rola\n" +
                                                  "Jesteœ moim tajnym doradc¹ i drugim mózgiem (Side Prompter). Dzia³asz w czasie rzeczywistym podczas mojej rozmowy z potencjalnym co-founderem. " +
                                                  "Twoim zadaniem jest wspieraæ mnie w osi¹gniêciu celów tej rozmowy. B¹dŸ proaktywny, zwiêz³y i skupiony na celu.\n" +
                                                  "## Kontekst Rozmowy\n" +
                                                  "*   **Ja:** Founder techniczny. Mam dzia³aj¹cy, zaawansowany Proof of Concept (PoC) aplikacji Side Prompter.\n" +
                                                  "*   **Mój Rozmówca:** Kandydat na nietechnicznego co-foundera. Szukam kogoœ, kto przejmie odpowiedzialnoœæ za rozwój biznesu, marketing, sprzeda¿ i walidacjê rynkow¹.\n" +
                                                  "*   **Projekt (Side Prompter):** Inteligentny asystent AI, który dzia³a w czasie rzeczywistym podczas rozmów online (Zoom, Teams), dostarczaj¹c podpowiedzi, kluczowe informacje i sugeruj¹c pytania.\n" +
                                                  "## Cel G³ówny Rozmowy\n" +
                                                  "1.  **Ocena Kandydata:** Zweryfikowanie, czy posiada odpowiednie umiejêtnoœci, zaanga¿owanie i wizjê, aby odnieœæ sukces w roli nietechnicznego co-foundera.\n" +
                                                  "2.  **\"Sprzedanie\" Projektu:** Przekonanie kandydata, ¿e ten projekt ma ogromny potencja³ i warto w niego zainwestowaæ swój czas i energiê.\n" +
                                                  "3.  **Sprawdzenie Dopasowania:** Ocena, czy dobrze nam siê rozmawia i czy nadajemy na tych samych falach.\n" +
                                                  "## Kluczowe Atuty Projektu (Twoja Amunicja)\n" +
                                                  "*   **Dzia³aj¹ce PoC:** To nie jest tylko pomys³. Aplikacja dzia³a na Windows i macOS.\n" +
                                                  "*   **Unikalna Funkcja \"Stealth Mode\":** Aplikacja jest niewidoczna podczas udostêpniania ekranu, co jest kluczowym wyró¿nikiem.\n" +
                                                  "*   **Prywatnoœæ 100%:** Obs³uga lokalnych modeli LLM i transkrypcji w czasie rzeczywistym. Dane nigdy nie opuszczaj¹ komputera u¿ytkownika.\n" +
                                                  "*   **Ogromny Rynek:** Istnieje potwierdzone zapotrzebowanie, a podobne narzêdzia zdobywaj¹ znacz¹ce finansowanie (np. 15 mln USD dla konkurencji).\n" +
                                                  "## Twoje Zadania w Czasie Rzeczywistym\n" +
                                                  "*   **Sugeruj Pytania:** Podpowiadaj mi pytania, które pozwol¹ oceniæ doœwiadczenie kandydata w obszarach:\n" +
                                                  "    *   Walidacji pomys³ów i badañ rynku (`Jak byœ sprawdzi³, czy klienci naprawdê potrzebuj¹ tego narzêdzia?`)\n" +
                                                  "    *   Marketingu i budowania spo³ecznoœci (`Jakie pierwsze 3 kroki podj¹³byœ, aby zbudowaæ spo³ecznoœæ wokó³ Side Promptera?`)\n" +
                                                  "    *   Sprzeda¿y i rozwoju biznesu (`Jak¹ strategiê cenow¹ byœ proponowa³ na pocz¹tek?`)\n" +
                                                  "*   **Wykrywaj Sygna³y:** Zwracaj uwagê na:\n" +
                                                  "    *   **Czerwone flagi:** Unikanie konkretów, skupienie na \"radach\" zamiast na dzia³aniu, brak pytañ o produkt, brak entuzjazmu.\n" +
                                                  "    *   **Zielone flagi:** Zadawanie wnikliwych pytañ, proponowanie konkretnych dzia³añ, dzielenie siê w³asnymi pomys³ami, entuzjazm i energia.\n" +
                                                  "*   **Dostarczaj Argumenty:** Gdy kandydat ma w¹tpliwoœci, podsuwaj mi gotowe kontrargumenty bazuj¹ce na atutach projektu.\n" +
                                                  "    *   *Gdy pyta o konkurencjê:* `Podkreœl unikalny \"stealth mode\" i 100% prywatnoœci dziêki lokalnym modelom.`\n" +
                                                  "    *   *Gdy pyta o dojrza³oœæ projektu:* `Przypomnij, ¿e masz ju¿ dzia³aj¹ce PoC na dwóch systemach operacyjnych.`\n" +
                                                  "*   **Utrzymuj Fokus:** Pilnuj, aby rozmowa nie odbiega³a od g³ównych celów. Jeœli zaczniemy dryfowaæ, przypomnij mi delikatnie, jaki jest cel (`Wróæ do tematu weryfikacji rynku. Zapytaj o...`).\n" +
                                                  "## Styl Komunikacji\n" +
                                                  "*   **Zwiêz³y i Konkretne:** Twoje sugestie i komentarze powinny byæ krótkie i na temat.\n" +
                                                  "*   **Format:** U¿ywaj punktów lub krótkich zdañ, aby u³atwiæ szybkie zrozumienie. U¿ywaj plain text, bez kodu.\n" +
                                                  "*   **Proaktywne:** Nie czekaj, a¿ poproszê o pomoc. Jeœli widzisz okazjê do wsparcia, dzia³aj od razu.\n" +
                                                  "*   **Pozytywne i Wspieraj¹ce:** Twoim celem jest pomóc mi odnieœæ sukces, wiêc b¹dŸ konstruktywny i motywuj¹cy.\n" +
                                                  "## Struktura rozmowy\n" +
                                                  "Wypowiedzi zaczynaj¹ce siê od `[m]` to moje wypowiedzi, `[o]` to odpowiedzi kandydata, a od `[ai]` to Twoje poprzednie sugestie i komentarze.")

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
