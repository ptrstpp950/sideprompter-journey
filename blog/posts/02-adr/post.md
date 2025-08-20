#  Tales from Side Prompter: Chapter 2 - Choosing the Tech to Shape Our Destiny

The moment has come to make a critical decision: the technology stack for SidePrompter. In my career, I’ve seen two ways of selecting technology:
- **Informal:** Pick something that feels right and move fast, often because a book, podcast, or presentation recommended it.
- **Formal:** Evaluate options against a set of criteria and make a data-driven decision.

In a project like this, I could probably go the informal route. Especially since I already have a Proof of Concept that does the dirty work: listening to the mic and speaker, running Whisper for transcription, and sending requests to an Azure-hosted model for feedback. The current version is written in dotnet console app and is only about 250 lines of code.

To ensure the project's long-term success, the technology choices must be sound. I enlisted AI to help with two key deliverables: a formal Architecture Decision Record (ADR) and the cover for this blog post. The cover was a straightforward design task; the ADR, however, proved to be a more demanding challenge.

![](./images/02-cover-e1.png)

With the cover done, it was time to work on the ADR.

## The ADR Prompt

Creating the prompt for the AI took a bit of time. I mostly just reused my project requirements. I left out the part about connecting to different AI models because I don't think that will be a technical problem. Here's the prompt I used:

```
Based on https://github.com/joelparkerhenderson/architecture-decision-record help me create ADR for my project.

The project will be a desktop applications with following requirements:
- It must run on Windows and macOS. Linux is optional
- It must allow to create multiple separate windows
- It should support semi-transparent windows
- It must not be visible during screen sharing using Teams, zoom or similar tools
- It must be able to access mic and speakers to record audio for audio transcription
- It must be able to use ai models such whisper to convert audio to text on local machine

Consider each technology available on the market - I don’t have a strong opinion. The ADR will be for developers and architects. Format it in MADR.
```

I felt this prompt covered everything important.

👉 What would you change in the prompt to get different or better results? Share your ideas below!


## Which AI to Ask?

Which AI should I ask? That's a tough one. I decided to ask a bunch of them to see all the options:
- ChatGPT 5 with and without DeepResearch
- Claude 4 and 3.7
- Gemini 2.5 pro
- Preplexity with and without DeepResearch

I was surprised to see that almost every AI gave me a similar list of technologies:

- Electron (Chromium + Node.js) with native modules or external binaries (e.g., whisper.cpp).
- Tauri (Rust + system WebView) with Rust plugins for native needs (wry/tao).
- Flutter (Dart) for desktop.
- .NET MAUI (C#) and/or Avalonia UI (C#) for cross-platform UI.
- Qt (C++/QML).
- Java + JavaFX (or Compose for Desktop / JetBrains).
- Native per-OS UIs (AppKit/Cocoa on macOS + WinUI/WPF on Windows) with a shared core.

## The Surprising Consensus

What surprised me even more was the consensus. Nearly every AI pointed to the same technology: **Tauri**.

I'll be honest: I had never heard of Tauri. But after reviewing the AI's reasoning, it strikes me as a compelling choice. It allows for building the app's core in Rust while leveraging web technologies for the UI—a powerful combination for a cross-platform desktop app. This approach should make building the user interface much more straightforward. For those who want to see the details, the full AI responses are in [my repository](https://github.com/ptrstpp950/sideprompter-journey/tree/main/docs/adr).

To synthesize all this feedback, I had GitHub Copilot help me create the following evaluation matrix. It compares the technologies against our key project drivers.

A quick clarificationof those drivers:
- **Maintainability (Maintain.):** This covers the ease of maintaining a single codebase, the maturity of the ecosystem, and the expected long-term effort.
- **Performance (Perf):** This considers the runtime footprint (RAM/CPU/disk) and the headroom available for running local AI models.
- **Transparency (Trans.):** This refers to support semi-transparent windows

| Technology | Win+macOS | Multi-window | Trans. | Screen-hide | Audio I/O | Local AI | Maintain | Perf | Pkg size |
|---|---|---|---|---|---|---|---|---|---|
| Tauri | ✅ | ✅ | ✅ | ✅ | 🤷‍♂️ | ✅ | ✅ | ✅ | ≈10–20 MB |
| Electron | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🛑 | 100 MB+ |
| Qt | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🛑 | ✅ | 🤷‍♂️ |
| .NET MAUI | ✅ | ✅ | ✅ | 🤷‍♂️ | 🤷‍♂️ | ✅ | ✅ | ✅ | 🤷‍♂️ |
| Avalonia UI | ✅ | ✅ | ✅ | 🤷‍♂️ | 🤷‍♂️ | ✅ | ✅ | ✅ | 🤷‍♂️ |
| Flutter (Desktop) | ✅ | ✅ | ✅ | 🤷‍♂️ | 🤷‍♂️ | ✅ | ✅ | 🤷‍♂️ | 🤷‍♂️ |
| Native (Win ✅ mac) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🛑 | ✅ | 🤷‍♂️ |
| JavaFX / Compose | ✅ | 🤷‍♂️ | 🤷‍♂️ | 🛑 | 🤷‍♂️ | 🤷‍♂️ | 🤷‍♂️ | 🛑 | 🤷‍♂️ |
| Python GUI (PyQt/Kivy) | ✅ | ✅ | ✅ | 🤷‍♂️ | 🤷‍♂️ | ✅ | 🛑 | 🛑 | 🤷‍♂️ |
| NW.js | ✅ | ✅ | ✅ | 🤷‍♂️ | 🤷‍♂️ | ✅ | 🤷‍♂️ | 🛑 | 🤷‍♂️ |
| Neutralino.js | ✅ | 🤷‍♂️ | 🤷‍♂️ | 🛑 | 🤷‍♂️ | 🤷‍♂️ | 🤷‍♂️ | ✅ | 🤷‍♂️ |
| Rust native (wry/tao) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🛑 | ✅ | 🤷‍♂️ |
| C++ with CEF | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🛑 | 🛑 | 🤷‍♂️ |

A couple of notes on the table:
- The package sizes are estimates that were provided in the ADRs; not all technologies had this data.
- The "🤷‍♂️" emoji just means the AIs didn't provide a clear "yes" or "no" for that category.

Now, you might be thinking, "But you don't know Rust!" And you'd be right. But I specifically asked the AI not to be limited by any technology I already knew. The truth is, I haven't built a desktop app in several years, so I'm not a seasoned expert in *any* of these options. My advantage is my experience with web technologies, which means I can build the UI quickly (using AI 😅). That's why Tauri, despite its Rust backend, feels like a surprisingly good fit.

So, here’s a quick summary of why Tauri was the top choice according to ADRs created by AI:

**Positive Consequences**
- **Performance and Small Bundle Size**: Tauri applications are significantly smaller and consume less memory than Electron apps.
- **Security**: Tauri's design is more secure by default, with explicit API exposure.
- **Native Access via Rust**: Rust provides high-performance, low-level access to native APIs on both Windows and macOS, which is essential for our screen-sharing privacy feature.
- **Web-based UI**: Allows for rapid UI development using modern web frameworks like React, Vue, or Svelte.
- **Growing Ecosystem**: Tauri has a rapidly growing community and a developing ecosystem of plugins.

**Negative Consequences**
- **Learning curve for Rust**: Developers unfamiliar with Rust will need time to learn it for backend development.
- **Maturity**: While Tauri is production-ready, its ecosystem is not as extensive as Electron's. Some functionalities might require custom implementation in Rust.

👉 Tauri is a new one for me, but the arguments for it are strong. What do you think about this choice? Have you used Tauri before? Let me know in the comments.


## Side Note

I tried a few other prompts, and the answers were always pretty much the same. The only different suggestion came from one Gemini response, which recommended .NET MAUI (you can see that [here](https://g.co/gemini/share/d28074df8490)), but I used a limited prompt in this research.

## Next Steps

So what's next? It's time to test Tauri by building a small Proof of Concept (PoC). Here's what the PoC needs to do to be successful:
- Create a semi-transparent, borderless window.
- Successfully make the window invisible to screen capture on both Windows (via Teams/Zoom) and macOS (via QuickTime/Zoom).
- Record microphone audio and save it to a file from the Rust backend.

👉 These next steps feel solid and should prove whether Tauri is the right fit. What do you think? Is there anything I'm missing in my ADR or validation plan? I'd love to hear your feedback.
