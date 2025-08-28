# AudioTee C# Wrapper

A C# wrapper for the AudioTee macOS system audio capture binary, providing a clean .NET interface for real-time audio capture in Avalonia applications.

## Overview

The AudioTee C# wrapper consists of several components:

- **`AudioTeeService`** - Main service class that wraps the AudioTee binary
- **`AudioTeeTranscriptionService`** - Example service combining AudioTee with transcription
- **`AudioTeeExample`** - Example usage patterns and test code
- **`AudioTeeTest`** - Test utilities for development

## Key Features

- **Event-driven API** - Receive audio data, logs, and status updates through events
- **Configurable options** - Sample rate, chunk duration, process filtering, etc.
- **Automatic binary resolution** - Handles macOS app bundle and development paths
- **Error handling** - Comprehensive error reporting and event handling
- **Memory efficient** - Uses `ReadOnlyMemory<byte>` for audio data
- **Thread-safe** - Safe to use from multiple threads with proper synchronization

## Basic Usage

```csharp
// Create service with options
var options = new AudioTeeOptions
{
    SampleRate = 16000,      // 16kHz for speech recognition
    ChunkDurationMs = 1000,  // 1 second chunks
    Mute = false             // Don't mute system audio
};

using var audioTeeService = new AudioTeeService(options);

// Subscribe to events
audioTeeService.DataReceived += (sender, chunk) =>
{
    Console.WriteLine($"Received {chunk.Data.Length} bytes at {chunk.Timestamp}");
    // Process audio data here
};

audioTeeService.ErrorOccurred += (sender, error) =>
{
    Console.WriteLine($"Error: {error.Message}");
};

// Start capturing
await audioTeeService.StartAsync();

// Let it run for 10 seconds
await Task.Delay(10000);

// Stop capturing
await audioTeeService.StopAsync();
```

## Configuration Options

### AudioTeeOptions

| Property | Type | Description |
|----------|------|-------------|
| `SampleRate` | `int?` | Audio sample rate (e.g., 16000, 44100) |
| `ChunkDurationMs` | `int?` | Duration of each audio chunk in milliseconds |
| `Mute` | `bool` | Whether to mute system audio during capture |
| `IncludeProcesses` | `IReadOnlyList<int>?` | Process IDs to include (null for all) |
| `ExcludeProcesses` | `IReadOnlyList<int>?` | Process IDs to exclude |

### Common Configurations

```csharp
// For speech recognition
var speechOptions = new AudioTeeOptions
{
    SampleRate = 16000,
    ChunkDurationMs = 1000
};

// For high-quality recording
var hqOptions = new AudioTeeOptions
{
    SampleRate = 44100,
    ChunkDurationMs = 500
};

// Filter specific applications
var filteredOptions = new AudioTeeOptions
{
    SampleRate = 16000,
    IncludeProcesses = new[] { 1234, 5678 }, // Specific app PIDs
    ExcludeProcesses = new[] { 9999 }        // System sounds
};
```

## Events

### DataReceived
Fired when audio data is captured:
```csharp
audioTeeService.DataReceived += (sender, chunk) =>
{
    ReadOnlyMemory<byte> audioData = chunk.Data;
    DateTime timestamp = chunk.Timestamp;
    // Process raw PCM audio data
};
```

### LogReceived
Fired when the AudioTee binary sends log messages:
```csharp
audioTeeService.LogReceived += (sender, log) =>
{
    Console.WriteLine($"[{log.MessageType}] {log.Message}");
};
```

### Started/Stopped
Capture lifecycle events:
```csharp
audioTeeService.Started += (sender, e) => Console.WriteLine("Capture started");
audioTeeService.Stopped += (sender, e) => Console.WriteLine("Capture stopped");
```

### ErrorOccurred
Error handling:
```csharp
audioTeeService.ErrorOccurred += (sender, error) =>
{
    Console.WriteLine($"AudioTee error: {error.Message}");
};
```

## Integration with Transcription

Use `AudioTeeTranscriptionService` for automatic transcription:

```csharp
var transcriptionService = new AudioTeeTranscriptionService(audioOptions);

transcriptionService.MessageGenerated += message =>
{
    Console.WriteLine($"Transcription: {message}");
};

transcriptionService.StatusChanged += status =>
{
    Console.WriteLine($"Status: {status}");
};

await transcriptionService.StartProcessingAsync("en"); // English
await Task.Delay(30000); // Run for 30 seconds
await transcriptionService.StopProcessingAsync();
```

## Binary Path Resolution

The service automatically locates the AudioTee binary in the following order:

1. **macOS App Bundle**: `Contents/Resources/libs/audioteejs/bin/audiotee`
2. **Development Build**: `libs/audioteejs/bin/audiotee`
3. **Current Directory**: Relative to working directory
4. **PATH**: System PATH environment variable

## Error Handling

The wrapper provides comprehensive error handling:

- **Process Errors**: AudioTee binary startup/runtime issues
- **Permission Errors**: macOS audio recording permissions
- **IO Errors**: File system and stream processing issues
- **Cancellation**: Graceful shutdown on cancellation tokens

## Threading

The service is thread-safe and uses:
- Background tasks for stdout/stderr processing
- Proper cancellation token handling
- Lock-free event dispatching
- Automatic cleanup on disposal

## Performance Considerations

- **Memory**: Uses pooled buffers and `ReadOnlyMemory<byte>` for zero-copy operations
- **CPU**: Minimal overhead, most processing happens in AudioTee binary
- **IO**: Asynchronous stream processing to avoid blocking
- **Events**: Events are fired on background threads, use `Dispatcher.BeginInvoke()` for UI updates

## Project Setup

The AudioTee binary is automatically included in builds via the `.csproj` configuration:

```xml
<!-- Copy AudioTee binary to output directory -->
<ItemGroup>
  <None Include="libs/audioteejs/bin/audiotee" 
        CopyToOutputDirectory="PreserveNewest"
        TargetPath="libs/audioteejs/bin/audiotee" />
</ItemGroup>

<!-- For macOS app bundles -->
<ItemGroup Condition="$([MSBuild]::IsOSPlatform('OSX'))">
  <BundleResource Include="libs/audioteejs/bin/audiotee">
    <LogicalName>libs/audioteejs/bin/audiotee</LogicalName>
  </BundleResource>
</ItemGroup>
```

## Requirements

- **macOS 14.2+** - Required by AudioTee binary
- **Audio Recording Permissions** - App must request microphone permissions
- **AudioTee Binary** - 600KB universal binary included in project
- **.NET 9.0** - Modern C# features and performance

## Extension Methods

Convenience methods for common scenarios:

```csharp
// Create service optimized for speech recognition
var speechService = AudioTeeExtensions.CreateForSpeechRecognition();

// Create service for high-quality recording
var hqService = AudioTeeExtensions.CreateForHighQuality();
```

## Testing

Use `AudioTeeTest` class for development and debugging:

```csharp
// Test binary resolution
await AudioTeeTest.TestBinaryPath();

// Test service creation
await AudioTeeTest.TestServiceCreation();

// Test event setup
await AudioTeeTest.TestServiceEvents();
```

This wrapper provides a clean, idiomatic C# interface to the AudioTee system audio capture functionality, making it easy to integrate real-time audio processing into your Avalonia applications.
