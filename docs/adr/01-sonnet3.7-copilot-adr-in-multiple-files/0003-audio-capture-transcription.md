# Audio Capture and Transcription Technology Selection

* Status: proposed
* Deciders: [list everyone involved in the decision]
* Date: 2025-08-19

## Context and Problem Statement

Our application needs to capture audio from the system's microphone and potentially speakers, then transcribe this audio to text using local AI models. We need to determine the best approach for audio capture and processing that works across platforms and integrates well with our chosen technology stack.

## Decision Drivers

* Cross-platform compatibility (Windows and macOS required, Linux optional)
* Audio capture quality and reliability
* Ability to access both microphone and system audio
* Integration with local AI models for transcription
* Performance considerations
* Privacy and security (keeping audio processing local)
* Developer experience and implementation complexity

## Considered Options

* WebAudio API with Tauri plugins
* FFmpeg integration via Rust
* Platform-specific native audio APIs (WASAPI for Windows, CoreAudio for macOS)
* PortAudio cross-platform library

## Decision Outcome

Chosen option: "WebAudio API with custom Tauri plugins", because it provides a good balance between web standard APIs for basic audio capture while allowing custom Rust plugins to handle more specialized needs and AI integration.

### Positive Consequences

* Leverages web standards for basic audio capture functionality
* Allows platform-specific optimizations through custom Rust plugins
* Maintains good developer experience with familiar Web APIs
* Enables secure access to system audio capabilities
* Provides flexibility to extend functionality as needed
* Works well within our Tauri framework selection

### Negative Consequences

* Requires custom Rust plugin development for advanced features
* May have limitations for system audio capture (as opposed to microphone)
* Could introduce complexity in managing audio data between JavaScript and Rust
* Potential performance overhead in audio data transfer between JS and Rust

## Pros and Cons of the Options

### WebAudio API with Tauri plugins

* Good, because it uses standard web APIs familiar to web developers
* Good, because it has well-documented interfaces for audio capture
* Good, because it enables custom Rust plugins to extend capabilities as needed
* Good, because it provides good cross-platform compatibility via web standards
* Good, because it maintains separation of concerns (UI in web, processing in Rust)
* Bad, because it may have limitations accessing system audio (vs microphone)
* Bad, because it requires building custom plugins for advanced functionality
* Bad, because it involves passing potentially large audio data between JS and Rust

### FFmpeg integration via Rust

* Good, because it offers comprehensive audio processing capabilities
* Good, because it has mature, battle-tested libraries
* Good, because it provides lower-level access to audio streams
* Good, because it enables both microphone and system audio capture
* Bad, because it has higher implementation complexity
* Bad, because it requires packaging FFmpeg with the application
* Bad, because it may have licensing considerations
* Bad, because it has less direct integration with web frontend

### Platform-specific native audio APIs

* Good, because it provides maximum performance and capability on each platform
* Good, because it has complete access to platform audio features
* Good, because it offers lowest-level control over audio capture
* Good, because it enables advanced features like system audio capture
* Bad, because it requires platform-specific code for each supported OS
* Bad, because it has increased maintenance burden for cross-platform support
* Bad, because it requires more specialized knowledge of platform APIs
* Bad, because it has steeper learning curve and development time

### PortAudio cross-platform library

* Good, because it provides unified API across platforms
* Good, because it offers mature, stable implementation
* Good, because it has good documentation and community support
* Good, because it abstracts away platform differences
* Bad, because it may lack some platform-specific optimizations
* Bad, because it requires additional dependency to be packaged
* Bad, because it has less direct integration with our web frontend
* Bad, because it may have performance overhead compared to direct native APIs

## Links

* [WebAudio API Documentation](https://developer.mozilla.org/en-US/docs/Web/API/Web_Audio_API)
* [Tauri Plugins Documentation](https://tauri.app/v1/guides/features/plugin/)
* [FFmpeg Documentation](https://ffmpeg.org/documentation.html)
* [PortAudio Documentation](http://www.portaudio.com/docs.html)
* [Whisper AI Model](https://github.com/openai/whisper)
