# Choose Desktop Application Framework for Cross-Platform Audio Recording App

* Status: proposed
* Deciders: Development Team, Tech Architects
* Date: 2025-08-19

## Context and Problem Statement

We need to build a desktop application that serves as a side prompter tool with the following specific requirements:
- Must run on Windows and macOS (Linux support is optional)
- Must support creating multiple separate windows
- Must support semi-transparent windows
- Must NOT be visible during screen sharing (Teams, Zoom, etc.)
- Must access microphone and speakers for audio recording and transcription
- Must run AI models locally (e.g., OpenAI Whisper) for speech-to-text conversion

The application needs to be robust, maintainable, and provide excellent user experience while meeting these unique technical constraints, particularly around screen sharing invisibility and local AI model execution.

## Decision Drivers

* **Cross-platform compatibility** - Windows and macOS support required
* **Window management capabilities** - Multiple windows, transparency support
* **Screen sharing invisibility** - Critical requirement for privacy/stealth mode
* **Audio system access** - Microphone and speaker access for recording
* **Local AI integration** - Must run Whisper and similar models locally
* **Development velocity** - Team expertise and development speed
* **Performance** - Smooth UI and efficient audio processing
* **Maintenance burden** - Long-term maintainability and updates
* **Distribution complexity** - Ease of packaging and deployment

## Considered Options

* **Electron + Node.js**
* **Tauri + Rust/JavaScript**
* **Flutter Desktop**
* **Qt (C++ or Python)**
* **WPF (.NET) + Avalonia**
* **.NET MAUI**
* **Native Development (Swift + C++)**

## Decision Outcome

Chosen option: **Tauri + Rust/JavaScript**, because it best meets our specific requirements with excellent performance, security, small bundle size, and strong cross-platform support. It provides the low-level system access needed for screen sharing invisibility while maintaining modern web UI development patterns.

### Positive Consequences

* Small application bundle size (10-20MB vs 100MB+ for Electron)
* Excellent security model with granular permissions
* Native performance for audio processing and AI model execution
* Strong window management capabilities including transparency
* Good cross-platform support for Windows and macOS
* Modern web technologies for UI development
* Growing ecosystem and active development
* Lower memory footprint compared to Electron

### Negative Consequences

* Smaller community compared to Electron
* Learning curve for Rust (though JavaScript frontend mitigates this)
* Fewer third-party plugins/integrations available
* More complex setup for some advanced system integrations

## Pros and Cons of the Options

### Electron + Node.js

* **Good**, because mature ecosystem with extensive documentation
* **Good**, because familiar web technologies (HTML, CSS, JavaScript)
* **Good**, because large community and many examples
* **Good**, because excellent cross-platform support
* **Bad**, because large bundle size (100MB+) and high memory usage
* **Bad**, because limited low-level system access for screen sharing invisibility
* **Bad**, because performance overhead for AI model execution
* **Bad**, because security concerns with full system access

### Tauri + Rust/JavaScript

* **Good**, because small bundle size and excellent performance
* **Good**, because strong security model with granular permissions
* **Good**, because native system access for advanced window management
* **Good**, because excellent for local AI model integration
* **Good**, because modern architecture with web frontend + native backend
* **Good**, because growing ecosystem with active development
* **Bad**, because smaller community and fewer resources
* **Bad**, because Rust learning curve for backend development
* **Bad**, because newer framework with potential stability concerns

### Flutter Desktop

* **Good**, because single codebase for mobile and desktop
* **Good**, because excellent UI framework with custom widgets
* **Good**, because good cross-platform support
* **Good**, because Dart language is approachable
* **Bad**, because limited low-level system access capabilities
* **Bad**, because desktop support still maturing
* **Bad**, because challenging for advanced window management features
* **Bad**, because complex integration with native audio systems and AI models

### Qt (C++ or Python)

* **Good**, because mature and stable framework
* **Good**, because excellent cross-platform support
* **Good**, because powerful native capabilities
* **Good**, because great window management and system integration
* **Bad**, because steep learning curve, especially for C++
* **Bad**, because complex deployment and licensing considerations
* **Bad**, because UI development can be cumbersome
* **Bad**, because Python version (PyQt) has performance limitations for AI workloads

### WPF (.NET) + Avalonia

* **Good**, because leverages existing .NET skills
* **Good**, because good Windows support
* **Good**, because decent cross-platform support with Avalonia
* **Good**, because strong ecosystem for desktop applications
* **Bad**, because primarily Windows-focused (Avalonia adds complexity)
* **Bad**, because limited low-level system access on non-Windows platforms
* **Bad**, because mixed cross-platform experience
* **Bad**, because potential challenges with screen sharing invisibility on macOS

### .NET MAUI

* **Good**, because unified development platform
* **Good**, because good integration with Microsoft ecosystem
* **Good**, because single codebase for multiple platforms
* **Bad**, because desktop support is still evolving
* **Bad**, because limited advanced window management capabilities
* **Bad**, because challenging for low-level system integrations
* **Bad**, because unclear support for screen sharing invisibility requirements

### Native Development (Swift + C++)

* **Good**, because maximum performance and system integration
* **Good**, because full access to platform-specific APIs
* **Good**, because best possible user experience per platform
* **Good**, because complete control over screen sharing behavior
* **Bad**, because requires separate codebases for each platform
* **Bad**, because significantly higher development and maintenance costs
* **Bad**, because requires expertise in multiple languages/frameworks
* **Bad**, because longer development timeline

## Technical Implementation Considerations

### Screen Sharing Invisibility
- **Tauri**: Can use platform-specific APIs through Rust backend to set window properties that exclude from screen capture
- **Implementation**: Use `kCGWindowLevel` on macOS and `SetWindowDisplayAffinity` on Windows

### Audio System Integration
- **Tauri**: Rust backend can integrate with system audio APIs (CoreAudio on macOS, WASAPI on Windows)
- **Libraries**: Use `cpal` for cross-platform audio, `rodio` for playback

### Local AI Model Integration
- **Tauri**: Rust backend excellent for running ML models via `candle-core`, `tch`, or Python subprocess
- **Whisper Integration**: Can use `whisper-rs` or call Python-based whisper through subprocess

### Window Management
- **Tauri**: Built-in support for multiple windows, transparency, and custom window decorations
- **API**: Rich window management API with per-window configuration

## Implementation Roadmap

1. **Phase 1**: Basic Tauri app with window management and transparency
2. **Phase 2**: Audio recording and playback functionality
3. **Phase 3**: Screen sharing invisibility implementation
4. **Phase 4**: Local AI model integration (Whisper)
5. **Phase 5**: UI polish and advanced features

## Links

* [Tauri Documentation](https://tauri.app/)
* [Tauri Window Management](https://tauri.app/v1/guides/features/window/)
* [Screen Capture Privacy](https://developer.apple.com/documentation/avfoundation/cameras_and_media_capture/requesting_authorization_for_media_capture_on_macos)
* [Windows Screen Capture API](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
