# Speech-to-Text Implementation from Both Devices

This document explains how to implement live speech-to-text transcription from both microphone and speaker devices in the Tauri application.

## Implementation Overview

The implementation consists of several components working together:

1. **Live Transcription Module** (`live_transcription.rs`) - Handles real-time audio capture and transcription
2. **Audio Capture Module** (`audio_capture.rs`) - Low-level audio device management  
3. **Tauri Commands** - Bridge between Rust backend and Vue frontend
4. **Vue Component** - User interface for controlling transcription

## Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────────┐
│   Vue Frontend  │    │  Tauri Commands  │    │  Rust Audio Engine │
│                 │    │                  │    │                     │
│ AudioTranscriber│───▶│start_live_       │───▶│ LiveTranscription   │
│ Component       │    │transcription()   │    │ Session             │
│                 │    │                  │    │                     │
│ • Model mgmt    │    │test_audio_       │    │ • Mic capture       │
│ • UI controls   │    │capture_with_     │    │ • Speaker capture   │
│ • Results display│   │transcription()   │    │ • Whisper inference │
└─────────────────┘    └──────────────────┘    └─────────────────────┘
```

## Key Functions

### Rust Backend Commands

#### `start_live_transcription(model_path, duration_seconds)`
- **Purpose**: Starts live audio capture from microphone and speaker, transcribes in real-time
- **Parameters**:
  - `model_path`: Path to the Whisper model file
  - `duration_seconds`: How long to capture (5-300 seconds)
- **Returns**: Array of `TranscriptionResult` objects
- **Usage**: 
  ```typescript
  const results = await invoke('start_live_transcription', {
    modelPath: './models/ggml-tiny.bin',
    durationSeconds: 30
  });
  ```

#### `test_audio_capture_with_transcription(model_path)`
- **Purpose**: Tests audio setup and model loading without starting capture
- **Returns**: Status message indicating readiness
- **Usage**:
  ```typescript
  const status = await invoke('test_audio_capture_with_transcription', {
    modelPath: './models/ggml-tiny.bin'
  });
  ```

### Audio Processing Flow

1. **Device Detection**: Finds default microphone and attempts to locate loopback devices
2. **Stream Creation**: Creates audio input streams with proper sample rate conversion
3. **Buffer Management**: Collects audio in chunks suitable for Whisper processing
4. **Transcription**: Processes audio chunks through Whisper model every 3 seconds
5. **Result Formatting**: Formats results with timestamps and source identification

## TranscriptionResult Format

```typescript
interface TranscriptionResult {
  text: string;      // "[m] Hello world" or "[o] System audio"
  source: string;    // "microphone" | "speaker" | "system"
  timestamp: number; // Unix timestamp in milliseconds
}
```

## Vue Component Integration

### Template Features
- Model download and management
- Audio device listing
- Live transcription controls with duration selection
- Real-time results display with source indicators
- Status and error reporting

### Key Vue Functions

```typescript
// Test audio setup before starting
async function testAudioSetup() {
  const result = await invoke('test_audio_capture_with_transcription', {
    modelPath: modelPath.value
  });
}

// Start live transcription
async function startLiveTranscription() {
  const results = await invoke('start_live_transcription', {
    modelPath: modelPath.value,
    durationSeconds: transcriptionDuration.value
  });
  
  liveResults.value = results;
}
```

## Speaker Audio Capture

### Windows Implementation
On Windows, the system attempts to find loopback devices such as:
- "Stereo Mix" 
- "What U Hear"
- "Loopback" devices

**Note**: These devices may need to be manually enabled in Windows Sound settings:
1. Right-click sound icon → "Open Sound settings"
2. Click "Sound Control Panel" 
3. Go to "Recording" tab
4. Right-click → "Show Disabled Devices"
5. Enable "Stereo Mix" or similar loopback device

### Other Platforms
- **macOS**: Requires additional implementation (possibly using Core Audio)
- **Linux**: May use PulseAudio monitor sources

## Audio Format Handling

### Input Processing
- **Sample Rates**: Automatically converts from device native rate to 16kHz (Whisper requirement)
- **Channels**: Converts stereo to mono by averaging channels  
- **Formats**: Handles F32 and I16 sample formats from cpal
- **Buffering**: Collects 3-second chunks for optimal Whisper processing

### Quality Considerations
- **Resampling**: Uses linear interpolation (production should use better algorithms)
- **Noise Handling**: No current noise reduction (could add VAD/noise gates)
- **Latency**: ~3-second processing delay for transcription chunks

## Error Handling

### Common Issues and Solutions

1. **No Model Found**
   ```
   Error: "Model file not found"
   Solution: Download model using download_whisper_model command first
   ```

2. **No Input Device**
   ```
   Error: "No default input device available"  
   Solution: Check microphone permissions and device connections
   ```

3. **No Speaker Capture**
   ```
   Warning: "No loopback device found"
   Solution: Enable "Stereo Mix" in Windows sound settings
   ```

4. **Threading Issues**
   ```
   Error: "Task execution failed"
   Solution: Audio processing runs in spawn_blocking to handle cpal's thread requirements
   ```

## Performance Characteristics

### Resource Usage
- **CPU**: Moderate during transcription (depends on Whisper model size)
- **Memory**: ~100MB for tiny model, more for larger models
- **Audio Buffer**: ~10 seconds of audio buffered per stream

### Model Performance
| Model | Size | Speed | Quality |
|-------|------|-------|---------|
| tiny  | 39MB | Fastest | Basic |
| base  | 142MB | Fast | Good |
| small | 244MB | Medium | Better |
| medium| 769MB | Slow | Best |

## Usage Example

### Complete Workflow

1. **Setup**:
   ```vue
   <!-- Download model -->
   <button @click="downloadModel">Download Tiny Model</button>
   
   <!-- Test setup -->
   <button @click="testAudioSetup">Test Audio Setup</button>
   ```

2. **Live Transcription**:
   ```vue
   <!-- Start transcription -->
   <input v-model="transcriptionDuration" type="number" min="5" max="300" />
   <button @click="startLiveTranscription">Start (30s)</button>
   ```

3. **Results Display**:
   ```vue
   <!-- Show results -->
   <div v-for="result in liveResults" :key="result.timestamp">
     <span>{{ formatTimestamp(result.timestamp) }}</span>
     <span>{{ result.source === 'microphone' ? '🎤' : '🔊' }}</span>
     <span>{{ result.text }}</span>
   </div>
   ```

## Future Improvements

1. **Real-time Streaming**: Currently processes in 3-second chunks
2. **Better Audio Processing**: More sophisticated resampling and noise reduction
3. **Voice Activity Detection**: Only transcribe when speech is detected
4. **Multiple Languages**: Dynamic language detection and switching
5. **Speaker Diarization**: Identify different speakers
6. **Continuous Mode**: Run indefinitely with periodic results

## Debugging

### Enable Debug Logging
```rust
// In audio processing functions
eprintln!("Processing {} samples from {}", audio_data.len(), source);
```

### Check Device Availability
```typescript
const devices = await invoke('list_audio_devices');
console.log('Available devices:', devices);
```

### Monitor Results
```typescript
liveResults.value.forEach(result => {
  console.log(`[${result.source}] ${result.text}`);
});
```

This implementation provides a solid foundation for real-time speech-to-text from both microphone and speaker sources, with proper error handling and a user-friendly interface.
