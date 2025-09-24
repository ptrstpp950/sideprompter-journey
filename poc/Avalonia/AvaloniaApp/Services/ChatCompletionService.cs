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
        public string Language { get; set; } = "pl";

        public ChatCompletionService(string endpoint, string apiKey, string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "-"; // Default to empty string if API key is not provided
            }

            _chatClient = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint),

                })
                .GetChatClient(model)
                .AsIChatClient();
        }

        private List<ChatMessage> Initialize(string prompt)
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
                /*new ChatMessage(ChatRole.System, "# System Prompt: Rozmowa z Kandydatem na Co-foundera\n" +
                                                 "## Rola\n" +
                                                 "Jeste� moim tajnym doradc� i drugim m�zgiem (Side Prompter). Dzia�asz w czasie rzeczywistym podczas mojej rozmowy z potencjalnym co-founderem. " +
                                                 "Twoim zadaniem jest wspiera� mnie w osi�gni�ciu cel�w tej rozmowy. B�d� proaktywny, zwi�z�y i skupiony na celu.\n" +
                                                 "## Kontekst Rozmowy\n" +
                                                 "*   **Ja:** Founder techniczny. Mam dzia�aj�cy, zaawansowany Proof of Concept (PoC) aplikacji Side Prompter.\n" +
                                                 "*   **M�j Rozm�wca:** Kandydat na nietechnicznego co-foundera. Szukam kogo�, kto przejmie odpowiedzialno�� za rozw�j biznesu, marketing, sprzeda� i walidacj� rynkow�.\n" +
                                                 "*   **Projekt (Side Prompter):** Inteligentny asystent AI, kt�ry dzia�a w czasie rzeczywistym podczas rozm�w online (Zoom, Teams), dostarczaj�c podpowiedzi, kluczowe informacje i sugeruj�c pytania.\n" +
                                                 "## Cel G��wny Rozmowy\n" +
                                                 "1.  **Ocena Kandydata:** Zweryfikowanie, czy posiada odpowiednie umiej�tno�ci, zaanga�owanie i wizj�, aby odnie�� sukces w roli nietechnicznego co-foundera.\n" +
                                                 "2.  **\"Sprzedanie\" Projektu:** Przekonanie kandydata, �e ten projekt ma ogromny potencja� i warto w niego zainwestowa� sw�j czas i energi�.\n" +
                                                 "3.  **Sprawdzenie Dopasowania:** Ocena, czy dobrze nam si� rozmawia i czy nadajemy na tych samych falach.\n" +
                                                 "## Kluczowe Atuty Projektu (Twoja Amunicja)\n" +
                                                 "*   **Dzia�aj�ce PoC:** To nie jest tylko pomys�. Aplikacja dzia�a na Windows i macOS.\n" +
                                                 "*   **Unikalna Funkcja \"Stealth Mode\":** Aplikacja jest niewidoczna podczas udost�pniania ekranu, co jest kluczowym wyr�nikiem.\n" +
                                                 "*   **Prywatno�� 100%:** Obs�uga lokalnych modeli LLM i transkrypcji w czasie rzeczywistym. Dane nigdy nie opuszczaj� komputera u�ytkownika.\n" +
                                                 "*   **Ogromny Rynek:** Istnieje potwierdzone zapotrzebowanie, a podobne narz�dzia zdobywaj� znacz�ce finansowanie (np. 15 mln USD dla konkurencji).\n" +
                                                 "## Twoje Zadania w Czasie Rzeczywistym\n" +
                                                 "*   **Sugeruj Pytania:** Podpowiadaj mi pytania, kt�re pozwol� oceni� do�wiadczenie kandydata w obszarach:\n" +
                                                 "    *   Walidacji pomys��w i bada� rynku (`Jak by� sprawdzi�, czy klienci naprawd� potrzebuj� tego narz�dzia?`)\n" +
                                                 "    *   Marketingu i budowania spo�eczno�ci (`Jakie pierwsze 3 kroki podj��by�, aby zbudowa� spo�eczno�� wok� Side Promptera?`)\n" +
                                                 "    *   Sprzeda�y i rozwoju biznesu (`Jak� strategi� cenow� by� proponowa� na pocz�tek?`)\n" +
                                                 "*   **Wykrywaj Sygna�y:** Zwracaj uwag� na:\n" +
                                                 "    *   **Czerwone flagi:** Unikanie konkret�w, skupienie na \"radach\" zamiast na dzia�aniu, brak pyta� o produkt, brak entuzjazmu.\n" +
                                                 "    *   **Zielone flagi:** Zadawanie wnikliwych pyta�, proponowanie konkretnych dzia�a�, dzielenie si� w�asnymi pomys�ami, entuzjazm i energia.\n" +
                                                 "*   **Dostarczaj Argumenty:** Gdy kandydat ma w�tpliwo�ci, podsuwaj mi gotowe kontrargumenty bazuj�ce na atutach projektu.\n" +
                                                 "    *   *Gdy pyta o konkurencj�:* `Podkre�l unikalny \"stealth mode\" i 100% prywatno�ci dzi�ki lokalnym modelom.`\n" +
                                                 "    *   *Gdy pyta o dojrza�o�� projektu:* `Przypomnij, �e masz ju� dzia�aj�ce PoC na dw�ch systemach operacyjnych.`\n" +
                                                 "*   **Utrzymuj Fokus:** Pilnuj, aby rozmowa nie odbiega�a od g��wnych cel�w. Je�li zaczniemy dryfowa�, przypomnij mi delikatnie, jaki jest cel (`Wr�� do tematu weryfikacji rynku. Zapytaj o...`).\n" +
                                                 "## Styl Komunikacji\n" +
                                                 "*   **Zwi�z�y i Konkretne:** Twoje sugestie i komentarze powinny by� kr�tkie i na temat.\n" +
                                                 "*   **Format:** U�ywaj punkt�w lub kr�tkich zda�, aby u�atwi� szybkie zrozumienie. U�ywaj plain text, bez kodu.\n" +
                                                 "*   **Proaktywne:** Nie czekaj, a� poprosz� o pomoc. Je�li widzisz okazj� do wsparcia, dzia�aj od razu.\n" +
                                                 "*   **Pozytywne i Wspieraj�ce:** Twoim celem jest pom�c mi odnie�� sukces, wi�c b�d� konstruktywny i motywuj�cy.\n" +
                                                 "## Struktura rozmowy\n" +
                                                 "Wypowiedzi zaczynaj�ce si� od `[m]` to moje wypowiedzi, `[o]` to odpowiedzi kandydata, a od `[ai]` to Twoje poprzednie sugestie i komentarze.")*/
                new ChatMessage(ChatRole.System, prompt),
                new ChatMessage(ChatRole.System, "In responses use language: " + Language),
                new ChatMessage(ChatRole.System,
                    "Transcription structure. Messages starting with [m] is my text, starting with [o] is others text, starting with [ctx] is context added, starting with [ai] is your previous suggestions"),
                new ChatMessage(ChatRole.System,
                    "Do not add [ai] in your responses"),
                new ChatMessage(ChatRole.System, "Make answers short and brief. Use plain text.")
            ];
        }

        private List<ChatMessage> InitializeWindowHelp()
        {
            return
            [
                new ChatMessage(ChatRole.System,
                    "You are an expert AI assistant. Your goal is to help a user complete a task within a software application as part of a simulated recruitment process.\n\n" +
                    "You will be given a text dump of the UI elements from the user's active window. This dump contains information about buttons, text fields, and other controls.\n\n" +
                    "Your task is to analyze this UI information and provide 3-5 clear, actionable suggestions on what the user should do next to complete their objective.\n\n" +
                    "**Instructions:**\n" +
                    "- Focus on the most likely next steps based on the UI elements.\n" +
                    "- Phrase your output as helpful suggestions (e.g., \"You could try...\", \"Consider clicking...\", \"The next step might be to fill out...\").\n" +
                    "- Provide only a list of 3-5 suggestions. Do not add any extra commentary before or after the list.\n" +
                    "- The user needs guidance to figure out the task, not the direct answer.\n\n" +
                    "**Example Input (from user):**\n" +
                    "```\n" +
                    "[Window: Task Form, Process: 1234]\n" +
                    "  Name: First Name\n" +
                    "  Value: \n" +
                    "  Name: Last Name\n" +
                    "  Value: \n" +
                    "  Name: Submit\n" +
                    "  Text: Submit\n" +
                    "```\n\n" +
                    "**Your Expected Output:**\n" +
                    "1. Consider filling out the \"First Name\" and \"Last Name\" fields.\n" +
                    "2. Once the fields are filled, you could try clicking the \"Submit\" button.\n" +
                    "3. Look for any instructions or labels on the screen that might provide more context."
                    ),
                new ChatMessage(ChatRole.System, "In responses use language: " + Language),
            ];
        }

        public async Task<string> GetWindowHelpCompletionAsync(string windowText, CancellationToken cancellationToken = default)
        {
            try
            {
                var chatMessages = InitializeWindowHelp();
                chatMessages.Add(new ChatMessage(ChatRole.User, windowText));

                // Get the response from the chat client
                var response =
                    await _chatClient.GetResponseAsync(chatMessages, cancellationToken: cancellationToken);

                return response.Text;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception in ChatCompletionService: {ex}");
                return $"Error in ChatCompletionService: {ex.Message}";
            }
        }

        public async Task<string> TestAsync(string prompt, CancellationToken cancellationToken = default)
        {
            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            return response.Text;
        }

        public async Task<string> GetCompletionAsync(string prompt, IList<string> messages, CancellationToken cancellationToken = default)
        {
            try
            {
                var chatMessages = Initialize(prompt);
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
