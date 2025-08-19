# 1. Desktop Technology Stack

* Status: proposed
* Deciders: architects, developers
* Date: 2025-08-19

Technical Story: The project requires a desktop application that runs on Windows and macOS, with optional Linux support. The application must support multiple, semi-transparent windows that are not visible during screen sharing. It also needs to access the microphone for audio recording and run local AI models like Whisper for transcription.

## Context and Problem Statement

Choosing the right technology stack for a desktop application is a critical decision that impacts development speed, performance, cross-platform compatibility, and the ability to meet specific technical requirements. The key challenge is to find a framework that supports all the mandatory features:

*   **Cross-platform:** Windows and macOS.
*   **Multiple windows:** Ability to create and manage several windows.
*   **Semi-transparent windows:** For modern UI/UX.
*   **Screen sharing privacy:** Windows must be excludable from screen captures.
*   **Audio access:** Microphone access for recording.
*   **Local AI integration:** Ability to run local models like Whisper.

## Decision Drivers

*   **Cross-platform support:** A single codebase for Windows and macOS is highly preferred to reduce development and maintenance effort.
*   **Performance:** The application should be responsive and have a low memory footprint.
*   **Access to native APIs:** Crucial for implementing screen sharing privacy and other platform-specific features.
*   **Development experience:** A good developer experience, including a strong ecosystem and community support, is important for productivity.
*   **Maturity and stability:** The chosen technology should be mature and stable for production use.

## Considered Options

1.  **Tauri:** A framework for building lightweight, secure, and cross-platform desktop applications using web technologies for the frontend and Rust for the backend.
2.  **Electron:** A popular framework for building cross-platform desktop apps with web technologies (JavaScript, HTML, CSS) and Node.js.
3.  **Qt:** A mature C++ framework for building cross-platform applications with native performance.
4.  **.NET MAUI / Avalonia UI:** Modern .NET frameworks for building cross-platform applications.

## Decision Outcome

Chosen option: **Tauri**, because it provides the best balance of performance, security, and access to native APIs while allowing the use of modern web technologies for the UI.

### Positive Consequences

*   **Performance and small bundle size:** Tauri applications are significantly smaller and consume less memory than Electron apps.
*   **Security:** Tauri's design is more secure by default, with explicit API exposure.
*   **Native access via Rust:** Rust provides high-performance, low-level access to native APIs on both Windows and macOS, which is essential for implementing the screen sharing privacy feature.
*   **Web-based UI:** Allows for rapid UI development using modern web frameworks like React, Vue, or Svelte.
*   **Growing ecosystem:** Tauri has a rapidly growing community and a developing ecosystem of plugins.

### Negative Consequences

*   **Learning curve for Rust:** Developers unfamiliar with Rust will need time to learn it for backend development.
*   **Maturity:** While Tauri is production-ready, its ecosystem is not as extensive as Electron's. Some functionalities might require custom implementation in Rust.

## Pros and Cons of the Options

### Tauri

*   `+` Excellent performance and small application size.
*   `+` Secure by default.
*   `+` Direct access to native APIs through Rust.
*   `+` Use of web technologies for the frontend.
*   `-` Rust has a steeper learning curve than JavaScript.
*   `-` Smaller ecosystem compared to Electron.

### Electron

*   `+` Mature and very popular with a large ecosystem.
*   `+` Uses JavaScript/TypeScript for both frontend and backend, which is familiar to many developers.
*   `+` Good documentation and community support.
*   `-` Larger application size and higher memory consumption.
*   `-` Accessing native APIs can be complex (requires C++ addons or FFI).

### Qt

*   `+` Excellent performance and native look and feel.
*   `+` Mature and stable with a wide range of features.
*   `+` C++ provides direct access to native APIs.
*   `-` C++ development can be slower and more complex than web technologies.
*   `-` Licensing costs for commercial projects can be a factor.
*   `-` UI development with QML or C++ can be less flexible than with HTML/CSS.

### .NET MAUI / Avalonia UI

*   `+` Good performance and access to the .NET ecosystem.
*   `+` C# is a modern and powerful language.
*   `+` Access to native APIs via P/Invoke.
*   `-` The ecosystem for desktop development is not as large as for web or mobile.
*   `-` .NET MAUI is still relatively new, and Avalonia has a smaller community.

## Links

*   [Tauri Website](https://tauri.app/)
*   [Electron Website](https://www.electronjs.org/)
*   [Qt Website](https://www.qt.io/)
*   [.NET MAUI Website](https://dotnet.microsoft.com/apps/maui)
*   [Avalonia UI Website](https://avaloniaui.net/)
