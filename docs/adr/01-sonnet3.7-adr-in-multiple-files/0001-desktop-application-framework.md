# Desktop Application Framework Selection

* Status: proposed
* Deciders: [list everyone involved in the decision]
* Date: 2025-08-19

## Context and Problem Statement

We need to select a framework for developing a desktop application that must run on multiple platforms (Windows and macOS, with Linux as optional). The application requires specific capabilities such as multiple separate windows, semi-transparency, invisibility during screen sharing, and access to audio hardware for transcription using local AI models.

## Decision Drivers

* Cross-platform compatibility (Windows and macOS required, Linux optional)
* Ability to create multiple separate windows
* Support for window transparency/semi-transparency
* Ability to remain invisible during screen sharing tools (Teams, Zoom, etc.)
* Access to microphone and speakers for audio capture
* Ability to utilize local AI models for audio transcription (e.g., Whisper)
* Developer experience and productivity
* Community support and documentation
* Long-term maintainability

## Considered Options

* Electron (JavaScript/TypeScript)
* Tauri (Rust + Web technologies)
* Qt (C++)
* .NET MAUI (C#)
* Flutter Desktop (Dart)
* JavaFX (Java)

## Decision Outcome

Chosen option: "Tauri", because it provides the best balance of performance, security, cross-platform capabilities, and can effectively meet all our specialized requirements while maintaining small binary sizes.

### Positive Consequences

* Smaller application size compared to Electron (uses system's WebView instead of bundling Chromium)
* Strong security model with Rust backend
* Native performance for resource-intensive tasks like AI processing
* Cross-platform with good compatibility on Windows and macOS
* Web-based frontend provides flexibility for UI development
* Direct access to system APIs through Rust for specific requirements like screen sharing detection
* Growing community and corporate backing

### Negative Consequences

* Relatively newer framework compared to some alternatives (less mature ecosystem)
* Requires development knowledge of both Rust and web technologies
* Linux support may require additional development effort
* May require custom Rust plugins for some specialized functionality

## Pros and Cons of the Options

### Electron

* Good, because it has excellent cross-platform capabilities (Windows, macOS, Linux)
* Good, because it offers a familiar web development environment (JavaScript/TypeScript)
* Good, because it has extensive community support and documentation
* Good, because it supports window transparency and multiple windows
* Good, because it offers APIs for audio capture and processing
* Bad, because it has high resource usage due to bundling Chromium
* Bad, because it may be detectable during screen sharing (harder to hide)
* Bad, because it may have performance limitations with local AI processing
* Bad, because it has larger application size

### Tauri

* Good, because it has significantly smaller app size than Electron
* Good, because it provides better performance due to Rust backend
* Good, because it has strong security features
* Good, because it supports window transparency
* Good, because it allows direct access to system APIs via Rust for implementing screen sharing invisibility
* Good, because its Rust backend is well-suited for integrating AI models like Whisper
* Good, because it has growing community and backing from companies like Microsoft
* Bad, because it's a newer framework with a less mature ecosystem
* Bad, because it requires knowledge of both Rust and web technologies
* Bad, because some features may require custom plugin development

### Qt

* Good, because it offers high performance and native feel
* Good, because it has extensive cross-platform capabilities
* Good, because it has mature development tools
* Good, because it supports transparent windows and multiple windows
* Good, because it provides good access to system hardware
* Bad, because C++ development can be more complex and error-prone
* Bad, because licensing can be complex and potentially costly
* Bad, because integration with modern AI libraries may require additional work
* Bad, because UI development may be less flexible than web-based approaches

### .NET MAUI

* Good, because it provides good Windows integration
* Good, because it has a familiar C# development experience
* Good, because it offers strong tooling with Visual Studio
* Good, because it enables access to system hardware for audio processing
* Bad, because macOS support is less mature than Windows support
* Bad, because it may have limitations for creating specialized window behaviors
* Bad, because it may be more challenging to hide from screen sharing applications
* Bad, because Linux support is not officially supported

### Flutter Desktop

* Good, because it has strong cross-platform capabilities
* Good, because it offers consistent UI across platforms
* Good, because it provides good performance for UI elements
* Good, because it supports multiple windows
* Bad, because desktop support is less mature than mobile
* Bad, because it may have limitations for specialized system access like remaining invisible during screen sharing
* Bad, because integration with native AI models like Whisper may be more complex
* Bad, because window transparency features may be platform-dependent

### JavaFX

* Good, because it offers good cross-platform capabilities
* Good, because it has mature development ecosystem
* Good, because it provides access to audio hardware
* Good, because it supports multiple windows
* Bad, because JVM overhead may impact performance for AI processing
* Bad, because window transparency support may be limited
* Bad, because remaining invisible during screen sharing would require complex native code integration
* Bad, because application size may be large due to JVM dependency

## Links

* [Tauri Official Documentation](https://tauri.app/v1/guides/)
* [Electron Documentation](https://www.electronjs.org/docs)
* [Qt Documentation](https://doc.qt.io/)
* [MAUI Documentation](https://learn.microsoft.com/en-us/dotnet/maui/)
* [Flutter Desktop Documentation](https://docs.flutter.dev/desktop)
