using System;

namespace AvaloniaApp;

public class AudioChunk(byte[] data, DateTime timestamp)
{
    public Memory<byte> Data { get; } = data;
    public DateTime Timestamp { get; } = timestamp;
}
