# Rust Audio Transcription for Tauri

This Rust implementation provides audio capture and transcription functionality using Whisper, similar to the C# version you provided. It's designed to work within a Tauri application.

## Features

- **Audio Device Detection**: Lists available input/output audio devices
- **Microphone Capture**: Real-time audio capture from the default microphone
- **Speaker Capture**: Attempts to capture system audio (requires loopback devices like "Stereo Mix" on Windows)
- **Whisper Model Management**: Download and manage Whisper models automatically
- **Audio File Transcription**: Transcribe WAV audio files
- **Real-time Processing**: Process audio in chunks suitable for real-time transcription

## Architecture

### Core Components

1. **`audio_capture.rs`**: Handles real-time audio capture from microphone and speakers
2. **`audio_utils.rs`**: Utilities for audio processing (loading, resampling, format conversion)
3. **`whisper_downloader.rs`**: Downloads Whisper models from Hugging Face
4. **`lib.rs`**: Tauri commands interface

### Key Differences from C# Implementation

| Feature | C# Implementation | Rust Implementation |
|---------|-------------------|-------------------|
| Audio Capture | NAudio with WaveInEvent/WasapiLoopbackCapture | cpal cross-platform audio |
| Audio Processing | NAudio SampleProviders | Custom buffer management |
| Whisper Integration | Whisper.net wrapper | whisper-rs native bindings |
| Async Processing | C# async/await | Tokio async runtime |
| Model Download | WhisperGgmlDownloader | Custom HTTP client with reqwest |
| Audio Formats | Multiple via NAudio | WAV (hound), extensible |

## Usage

### Tauri Commands

```typescript
// Download a Whisper model
await invoke('download_whisper_model', {
  modelType: 'tiny', // tiny, base, small, medium, large
  modelsDir: './models'
});

// List available audio devices
const devices = await invoke('list_audio_devices');

// Test audio capture
const result = await invoke('test_audio_capture');

// Transcribe an audio file
const text = await invoke('transcribe_audio', {
  audioPath: '/path/to/audio.wav',
  modelPath: '/path/to/model.bin'
});

// Check if model exists and is valid
const isValid = await invoke('check_whisper_model', {
  modelPath: '/path/to/model.bin'
});
```

### Vue Component

A complete Vue component (`AudioTranscriber.vue`) is provided that demonstrates:
- Model selection and downloading
- Device listing
- File transcription interface
- Status reporting

## Audio Capture Implementation

The audio capture system works as follows:

1. **Device Discovery**: Uses cpal to find input devices and attempts to locate loopback devices for speaker capture
2. **Stream Creation**: Creates audio streams with appropriate sample formats (F32/I16)
3. **Buffer Management**: Collects audio data in chunks and processes them periodically
4. **Format Conversion**: Converts to mono and resamples to 16kHz as required by Whisper
5. **Real-time Processing**: Processes audio in 3-second chunks for real-time transcription

### Speaker Capture (Windows)

On Windows, the system looks for devices with names containing:
- "Stereo Mix"
- "What U Hear"  
- "Loopback"

Note: These devices may need to be manually enabled in Windows Sound settings.

## Dependencies

### Rust Crates

```toml
cpal = "0.15"           # Cross-platform audio I/O
hound = "3.5"           # WAV file reading/writing
whisper-rs = "0.10"     # Whisper bindings
reqwest = "0.11"        # HTTP client for model downloads
crossbeam-channel = "0.5" # Multi-producer channels
tokio = "1.0"           # Async runtime
```

## Limitations & Future Improvements

1. **Audio Formats**: Currently only supports WAV files (can be extended with symphonia or ffmpeg)
2. **Speaker Capture**: Limited to Windows loopback devices (could use more sophisticated methods)
3. **Real-time Transcription**: Basic implementation (could add VAD, streaming transcription)
4. **Error Handling**: Could be more granular
5. **Configuration**: Hard-coded parameters could be made configurable

## Comparison with C# Implementation

### Advantages of Rust Version
- **Performance**: Lower-level control and zero-cost abstractions
- **Memory Safety**: Rust's ownership system prevents common audio processing bugs
- **Cross-platform**: cpal works on Windows, macOS, and Linux
- **Integration**: Native Tauri integration without additional runtime dependencies

### Advantages of C# Version
- **Rich Ecosystem**: NAudio provides extensive audio format support
- **Rapid Development**: Higher-level abstractions and libraries
- **Windows Integration**: Better Windows-specific audio features
- **Debugging**: More mature tooling for audio development

## Getting Started

1. Ensure you have Rust and Tauri development environment set up
2. Build the application: `cargo build`
3. Run the development server: `npm run tauri:dev`
4. The web interface will provide access to all audio transcription features

The system will automatically download Whisper models as needed and handle audio device detection and capture.
