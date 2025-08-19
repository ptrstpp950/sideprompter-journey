# 1. Choose desktop platform and audio/AI stack

- Status: Accepted
- Deciders: Project engineering team
- Date: 2025-08-19
- Tags: desktop, cross-platform, windows, macos, audio, system-audio, screen-capture-protection, transparency, multi-window, ai, whisper, whisper.cpp, electron, tauri, flutter, .net-maui, avalonia, qt, java

## Context and Problem Statement

We need to choose the primary technology stack for a new desktop application with the following requirements:

- Must run on Windows and macOS; Linux is optional.
- Must allow multiple separate windows.
- Should support semi-transparent windows.
- Must not be visible during screen sharing (Teams, Zoom, etc.) — i.e., prevent screen capture of app windows.
- Must be able to access microphone and system audio (speakers) to record audio for transcription.
- Must be able to run AI models such as Whisper locally to convert audio to text (no cloud requirement).

Additional constraints and preferences:

- Developers and architects are the audience; maintainability and time-to-market matter.
- No strong opinion on stack; consider mainstream options and ecosystem maturity.
- Prefer fewer platform-specific code paths where possible; allow native shims where necessary.

## Decision Drivers

- Cross-platform coverage (Windows + macOS) with consistent behavior.
- First-class support for: multi-window, transparent windows, and screen-capture protection.
- Feasible access to microphone and system audio capture without complex end-user setup.
- Local AI transcription with Whisper; performance, packaging, and licensing.
- Ecosystem maturity, availability of plugins/libraries, long-term support.
- Security, code signing/notarization paths, and build/release tooling.
- Application size, runtime performance, and developer productivity.

## Considered Options

- Electron (Chromium + Node.js) with native modules or external binaries (e.g., whisper.cpp).
- Tauri (Rust + system WebView) with Rust plugins for native needs (wry/tao).
- Flutter (Dart) for desktop.
- .NET MAUI (C#) and/or Avalonia UI (C#) for cross-platform UI.
- Qt (C++/QML).
- Java + JavaFX (or Compose for Desktop / JetBrains).
- NW.js (Chromium + Node.js), Neutralino.js.
- Native per-OS UIs (AppKit/Cocoa on macOS + WinUI/WPF on Windows) with shared core.
- Rust native (wry/tao directly) or C++ with CEF (Chromium Embedded Framework).

## Decision Outcome

Chosen option: Electron with Node.js runtime, Chromium rendering, and the following native integrations:

- Screen-capture protection via BrowserWindow.setContentProtection(true) across Windows and macOS.
- Transparent, multi-window UI using BrowserWindow (transparent: true, frame: false) and multiple BrowserWindow instances.
- Microphone capture via WebRTC getUserMedia. System audio capture via desktopCapturer with audio: true (supported on Windows and macOS 13+ with ScreenCaptureKit); provide macOS fallback guidance if system audio capture is not available without a virtual audio device.
- Local transcription via whisper.cpp (bundled CLI or native addon). Start with spawning a bundled whisper.cpp binary for simplicity and portability; consider native addon or WASM later for deeper integration/perf.
- Packaging with Electron Builder or Electron Forge for code signing, notarization, auto-update.

This option best meets the non-negotiable requirements with the least custom native work and the broadest battle-tested ecosystem.

### Positive Consequences

- Satisfies screen-capture protection requirement with a single cross-platform API.
- Mature support for multi-window and transparent windows.
- Straightforward mic capture with standard web APIs; workable system audio capture path.
- Large ecosystem, docs, examples, and community support; many production apps.
- Easy integration path for whisper.cpp via child_process; can ship model files with app.
- Rapid developer onboarding (web stack) and faster time-to-market.

### Negative Consequences

- Larger runtime footprint and memory usage vs. WebView-based or native stacks.
- Node native modules require attention per-platform for building/signing.
- System audio capture UX on macOS may still require permissions (Screen Recording) and may need user-installed virtual audio drivers on older macOS versions.
- Security posture needs careful configuration (contextIsolation, CSP, IPC hardening).

## Pros and Cons of the Options

### Electron

- Pro: Cross-platform parity; excellent multi-window and transparency support.
- Pro: Screen-capture protection via setContentProtection on Windows/macOS/Linux.
- Pro: Mic capture via getUserMedia; system audio capture via desktopCapturer on supported OS versions.
- Pro: Many examples of whisper.cpp integration; can run external binaries.
- Con: Heavier runtime; larger installers.
- Con: Need to manage Node/Electron native build chain and code signing/notarization.

### Tauri (Rust + WebView)

- Pro: Small binaries, low memory; strong security defaults.
- Pro: Multi-window and transparency supported via wry/tao.
- Pro: Rust side can integrate whisper.cpp cleanly and efficiently.
- Con: Screen-capture protection and system audio capture require custom platform code/plugins; not as turnkey as Electron.
- Con: Web layer may have limitations for advanced capture scenarios vs. Chromium’s desktopCapturer.

### Flutter

- Pro: High-quality UI; good multi-window (stable) and transparency via plugins/native code.
- Pro: Native integrations possible for audio and capture; Dart FFI to whisper.cpp.
- Con: Screen-capture protection and system audio capture are not turnkey; requires platform channels and plugins; maturity varies.
- Con: Heavier toolchain; desktop is newer than mobile.

### .NET MAUI / Avalonia

- Pro: Strong Windows support; Avalonia cross-platform desktop maturity is good.
- Pro: Easy C# interop for native APIs and whisper.cpp via bindings.
- Con: macOS support, screen-capture protection, and system audio capture require significant per-OS native implementations.
- Con: Fewer battle-tested samples for this exact combo.

### Qt (C++/QML)

- Pro: Very mature cross-platform; full control over native APIs (audio, windows, capture).
- Pro: Easy to integrate whisper.cpp (C++).
- Con: Higher dev complexity; licensing considerations for commercial distribution; UI web stack not native.

### Java + JavaFX / Compose for Desktop

- Pro: Cross-platform; JVM ecosystem; good tooling.
- Con: Advanced window flags (screen-capture protection, transparency) and system audio capture are non-trivial and platform-specific.
- Con: Whisper integration feasible via JNI, but packaging JVM + models increases size.

### NW.js / Neutralino.js

- NW.js: Similar to Electron but smaller ecosystem momentum; fewer maintained examples.
- Neutralino: Very small footprint but limited capabilities; advanced capture features unlikely without custom native extensions.

### Native per-OS UIs; Rust native; C++ with CEF

- Pro: Maximum control and performance; can implement all requirements.
- Con: Highest cost to build and maintain two codebases or heavy cross-platform plumbing.

## Alignment to Requirements

- Windows and macOS: Electron is first-class on both.
- Multiple windows: Supported natively via multiple BrowserWindow instances.
- Semi-transparent windows: Supported via BrowserWindow transparent windows with GPU acceleration.
- Invisible during screen sharing: setContentProtection(true) prevents screenshots and screen capture across supported OSes.
- Mic and speaker access: getUserMedia for mic; desktopCapturer with audio for system audio (with OS-specific constraints); provide fallbacks.
- Local Whisper transcription: Bundle whisper.cpp binary and models; spawn for transcription; consider native addon later.

## Validation Plan

- Build a proof of concept that:
  - Creates two transparent BrowserWindows and toggles setContentProtection(true/false).
  - Captures microphone and system audio (where supported) to a single WAV track; on macOS, verify behavior on 12/13+ and document fallback.
  - Runs whisper.cpp on a short audio clip locally and returns text in-app.
- Verify on Windows 11 and macOS 13+ with required permissions (Microphone and Screen Recording on macOS).
- Establish CI builds and signing workflows (Windows code signing; macOS notarization).

## Implementation Sketch

- Electron app baseline (Forge or Builder) with contextIsolation and secure IPC.
- Main process APIs:
  - Window factory supporting transparency, always-on-top, and content protection flag.
  - Audio service that uses desktopCapturer (with audio: true) to capture system audio on supported OS; fallback path documented for macOS (virtual audio device like BlackHole) when needed.
  - Child process wrapper to run bundled whisper.cpp with selected model; stream progress/partial results.
- Ship small models by default (e.g., base/base.en) with optional on-demand download for larger models.

## Security and Privacy Notes

- Enable contextIsolation; disable nodeIntegration in renderer; strict CSP.
- Request minimum permissions; on macOS, ensure Screen Recording and Microphone entitlements and user prompts are handled.
- Consider on-disk encryption of cached audio and transcripts if sensitive.

## Risks and Mitigations

- System audio capture variability on macOS: Document OS version support; provide guided install of a virtual audio device if needed.
- Binary size: Use differential updates; allow external model downloads.
- Native builds: Use prebuilds where possible; pin Electron/Node versions; automate notarization.

## Alternatives Considered

- Tauri: Attractive footprint and Rust integration; chosen against due to higher effort for capture protection and system audio parity across OSes right now.
- Flutter: Strong UI, but plugin maturity for our exact needs is mixed; more native work required.
- Qt: Technically excellent but heavier C++ stack and licensing/training costs for the team.
- .NET MAUI/Avalonia: Feasible, especially for Windows-first; macOS advanced window/capture features need significant native work.

## Work Breakdown (Initial)

- Week 1–2: Electron scaffolding, window features (multi-window, transparency, content protection).
- Week 2–3: Audio capture POC (mic + system) and permissions handling.
- Week 3–4: Integrate whisper.cpp (bundle small model), transcript pipeline, basic UI.
- Week 4–5: Packaging, signing/notarization, telemetry, logs, and error handling.

## References

- ADR template inspiration: https://github.com/joelparkerhenderson/architecture-decision-record
- MADR: https://adr.github.io/madr/
- Electron BrowserWindow.setContentProtection: https://www.electronjs.org/docs/latest/api/browser-window
- Electron desktopCapturer (system audio): https://www.electronjs.org/docs/latest/api/desktop-capturer
- Windows SetWindowDisplayAffinity (ExcludeFromCapture): https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- macOS ScreenCaptureKit: https://developer.apple.com/documentation/screencapturekit
- whisper.cpp: https://github.com/ggerganov/whisper.cpp
