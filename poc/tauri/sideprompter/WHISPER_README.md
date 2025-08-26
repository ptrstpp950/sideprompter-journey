# Whisper Integration

This document explains how to use the Whisper speech-to-text functionality in your Tauri application.

## Overview

The application now includes `whisper-rs` integration, allowing you to transcribe audio files to text using OpenAI's Whisper models.

## Features

- ✅ Transcribe audio files to text
- ✅ Model validation
- ✅ Async processing (non-blocking)
- ⚠️  Basic audio format support (implementation needed)
- 🔄 Frontend Vue component for easy testing

## Getting Started

### 1. Download a Whisper Model

You'll need to download a Whisper model file. These are available from:

- [Official Whisper.cpp models](https://huggingface.co/ggerganov/whisper.cpp/tree/main)
- [Hugging Face Hub](https://huggingface.co/models?search=whisper)

Popular models:
- `ggml-base.en.bin` (~74MB) - English only, good balance of speed/accuracy
- `ggml-small.en.bin` (~244MB) - English only, better accuracy
- `ggml-base.bin` (~74MB) - Multilingual
- `ggml-small.bin` (~244MB) - Multilingual, better accuracy

### 2. Prepare Audio Files

Currently, the implementation expects:
- 16kHz sample rate
- Mono channel
- WAV format (other formats require additional implementation)

### 3. Build the Application

```bash
# Install dependencies
npm install

# Build the Rust backend
cd src-tauri
cargo build --release

# Run the application
npm run tauri:dev
```

## API Reference

### Rust Commands

#### `transcribe_audio`
Transcribes an audio file to text.

```rust
#[tauri::command]
async fn transcribe_audio(audio_path: String, model_path: String) -> Result<String, String>
```

**Parameters:**
- `audio_path`: Path to the audio file
- `model_path`: Path to the Whisper model file

**Returns:** Transcribed text or error message

#### `check_whisper_model`
Validates a Whisper model file.

```rust
#[tauri::command]
async fn check_whisper_model(model_path: String) -> Result<bool, String>
```

**Parameters:**
- `model_path`: Path to the Whisper model file

**Returns:** Boolean indicating if the model is valid

### TypeScript Service

```typescript
import { whisperService } from './whisper-service';

// Check if model is valid
const isValid = await whisperService.checkWhisperModel('/path/to/model.bin');

// Transcribe audio
const transcription = await whisperService.transcribeAudio(
  '/path/to/audio.wav', 
  '/path/to/model.bin'
);
```

## Current Limitations

### Audio Format Support
The current implementation has placeholder audio loading. To support various audio formats, you'll need to:

1. **For WAV files**: Add the `hound` crate
   ```toml
   [dependencies]
   hound = "3.5"
   ```

2. **For general audio support**: Add `symphonia` or `ffmpeg-next`
   ```toml
   [dependencies]
   symphonia = { version = "0.5", features = ["mp3", "flac", "vorbis"] }
   ```

3. **Update the audio loading implementation** in `src-tauri/src/audio_utils.rs`

### Sample Rate Conversion
The current resampling implementation is basic. For production use, consider:
- `rubato` crate for high-quality resampling
- `samplerate` crate (libsamplerate bindings)

## Development Notes

### File Structure
```
src-tauri/src/
├── lib.rs                 # Main Tauri commands
├── audio_utils.rs         # Audio processing utilities
└── ...

src/
├── whisper-service.ts     # TypeScript service interface
├── components/
│   └── WhisperDemo.vue   # Demo component
└── ...
```

### Building with Different Features

You can customize the whisper-rs build with features:

```toml
[dependencies]
whisper-rs = { version = "0.10", features = ["metal"] }  # For macOS Metal acceleration
# or
whisper-rs = { version = "0.10", features = ["cuda"] }   # For NVIDIA GPU acceleration
```

## Troubleshooting

### Model Loading Issues
- Ensure the model file exists and is a valid Whisper model
- Check file permissions
- Verify the model is compatible with whisper-rs

### Audio Processing Issues
- Ensure audio files are in supported formats
- Check that audio files are not corrupted
- Verify sample rate and channel configuration

### Performance Issues
- Use smaller models for faster processing
- Consider GPU acceleration features
- Process audio files in chunks for very long recordings

## Future Improvements

- [ ] Complete audio format support (MP3, M4A, FLAC, etc.)
- [ ] File dialog integration for easier file selection
- [ ] Real-time audio transcription from microphone
- [ ] Progress indicators for long transcriptions
- [ ] Language detection and model switching
- [ ] Batch processing multiple files
- [ ] Audio preprocessing (noise reduction, normalization)
- [ ] Export transcriptions to different formats (SRT, VTT, etc.)
