# ADR: Desktop Application Framework Selection

- Source: https://chatgpt.com/s/dr_68a5763ebd408191a6670382a194c007
- Status: proposed
- Deciders: project architects and developers
- Date: 2025-08-18
- Technical Story: A cross-platform desktop app is needed (Windows, macOS; Linux optional) that supports multiple independent windows, semi-transparent overlays, privacy during screen-sharing, microphone/speaker I/O, and on-device AI transcription (e.g., Whisper).

## Context and Problem Statement

We need to choose a technology stack for a new desktop application with strict requirements. It must run natively on Windows and macOS (Linux optional). The UI must support multiple independent windows and semi-transparent overlays. Critically, these windows must be excluded from screen sharing (Zoom/Teams, etc.). The app also needs to record audio (mic/speakers) and perform local speech-to-text using AI models like OpenAI’s Whisper (no cloud). In short: cross-platform GUI framework with transparency and screen-capture exclusion, plus audio/ML integration. There is no existing preference; we will consider all major options.

## Decision Drivers

- Cross-platform (Windows & macOS): Same codebase for Win/Mac preferred (Linux support optional).
- Multiple windows & overlays: Framework must support creating many windows and setting transparency.
- Screen-share exclusion: Windows must be able to opt out of screen capture via OS APIs.
- Audio I/O: Must capture microphone and play sound (integrate with OS audio).
- Local AI inference: Must run Whisper-like model locally (via Python/C++ libs or embedded code).
- Performance & footprint: Efficient memory/CPU and modest install size to leave headroom for local AI.
- Developer ecosystem: Prefer mature frameworks and good tooling.
- Maintainability: Ideally a single codebase (no separate Win/Mac forks) to reduce effort.

## Considered Options

- Electron (Chromium/Node.js)
- Tauri (Rust + WebView)
- Qt (C++ or PyQt for Python)
- .NET (Avalonia or MAUI)
- Flutter (Dart)
- Native (C#/.NET WinUI on Windows + SwiftUI on macOS)
- Python GUI (PyQt, Kivy, etc.)

## Decision Outcome

Chosen option: Tauri (Rust + OS WebView-based desktop app).

Tauri best meets the drivers: it supports Windows and macOS with one codebase, allows multiple transparent windows, and provides an API to prevent screen capture on supported platforms (e.g., `setContentProtected(true)`). Tauri apps are lightweight (no Chromium bundle; they rely on the OS WebView). We can record audio via web APIs or Rust bindings and run Whisper locally via a Rust–Python bridge or a native port (e.g., whisper.cpp). This avoids Electron’s heavyweight Chromium footprint and its uneven content-protection behavior. On macOS, this maps to `NSWindow.sharingType = .none`; on Windows, to `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.

We acknowledge that Tauri/Rust may require new expertise, but the performance, security, and compliance with requirements justify the choice.

### Positive Consequences

- Cross-platform single codebase: One Tauri app targets Windows/macOS (and Linux) without separate UI logic.
- Lightweight & performant: Uses OS WebView (lower RAM/disk) and Rust backend for native performance.
- Privacy compliance: Content protection to prevent capture in Zoom/Teams-like tools satisfies privacy needs.
- Transparency & windows: HTML/CSS can handle transparent backgrounds; Tauri supports multiple windows.
- Audio/AI integration: Use Web/Rust for audio; call Whisper via embedded Python process or native library (e.g., whisper.cpp). Runs fully on-device.
- Security: Rust core reduces attack surface compared to Node-in-renderer models; fine-grained permissions.

### Negative Consequences

- Learning curve: Team must learn Rust and Tauri’s model; ecosystem smaller than Electron’s.
- GUI complexity: Deep OS-level behaviors (drag, resize quirks) may need extra work.
- Limited plugins: Fewer third-party widgets; may require custom components.
- Whisper integration: Need robust interop and packaging for the AI engine; Python version is more mature than Rust-native options.

## Pros and Cons of the Options

### Electron (Chromium + Node.js)

- Pros: Very mature; vast plugin ecosystem. Cross-platform, multi-window, transparency. Easy HTML/CSS/JS UI. Node access to filesystem; can spawn processes (e.g., Python Whisper).
- Cons: Heavy footprint (bundles Chromium) with large downloads and high memory use. Content protection (e.g., `BrowserWindow.setContentProtection`) is inconsistent in practice with modern capture tools. Higher CPU/battery and broader security surface.

### Tauri (Rust + OS WebView)

- Pros: Lightweight (OS WebView). Lower RAM/disk; fast startup. Rust backend is memory-safe and performant. API includes content protection. Natural transparency via CSS and multi-window support. Web UI development speed; Rust can spawn Python or link to native libs.
- Cons: Newer tech; smaller ecosystem. Native integration (audio/ML) can be trickier. Requires Rust knowledge. Fewer off-the-shelf widgets than Electron.

### Qt (C++ or PyQt)

- Pros: Established native GUI framework. Full control over windows/transparency; multi-window. Audio via QtMultimedia. PyQt allows direct use of Python Whisper packages.
- Cons: Large runtime/deployment size. Licensing considerations (LGPL/commercial). Screen-share exclusion requires manual OS calls (e.g., SetWindowDisplayAffinity / NSWindow) rather than a single cross-platform API. C++ development slower; PyQt still produces sizable binaries.

### .NET (Avalonia or MAUI)

- Pros: C# productivity; cross-platform (Avalonia Win/Mac/Linux). Transparency and multi-window feasible. Audio via libraries (e.g., NAudio). Interop with Python possible.
- Cons: Avalonia not as mature as WPF; macOS support still evolving. Screen-capture flags via P/Invoke per-OS. Shipping .NET runtime increases size. Whisper via embedded Python or limited .NET ML alternatives.

### Flutter (Dart)

- Pros: Modern, hardware-accelerated UI with a single codebase; desktop supported.
- Cons: Desktop is newer; transparent windows require native setup. No built-in content protection—needs custom plugins. Audio via plugins. Whisper likely via external process.

### Native (C#/SwiftUI separately)

- Pros: Best OS integration/performance. On macOS, `window.sharingType = .none`; on Windows, `SetWindowDisplayAffinity` available. Mature audio APIs.
- Cons: Two independent codebases increase cost; feature parity and maintenance burdens.

### Python GUI (PyQt, Kivy, etc.)

- Pros: Fast iteration; direct use of Whisper Python APIs. Transparent windows possible. Microphone via PyAudio or similar.
- Cons: Desktop UX polish can lag. Packaging Python + Qt + AI models is complex and large. Performance overhead. Screen-capture exclusion still requires ctypes to call OS APIs. Less common for production desktop tools.

## Links and References

- Tauri vs. Electron: The Ultimate Desktop Framework Comparison — https://peerlist.io/jagss/articles/tauri-vs-electron-a-deep-technical-comparison
- Electron content protection discussion — https://stackoverflow.com/questions/78959648/using-win-setcontentprotection-to-hide-app-from-other-apps-not-working-but-do
- Create a Translucent Overlay Window on macOS (NSWindow sharing) — https://gaitatzis.medium.com/create-a-translucent-overlay-window-on-macos-in-swift-67d5e000ce90
- SetWindowDisplayAffinity (Win32) — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- Tauri Window API — https://v1.tauri.app/v1/api/js/window/
- Whisper local usage overview — https://y-consulting.medium.com/technical-blueprint-for-a-privacy-first-ai-assistant-mvp-3d3989862684
