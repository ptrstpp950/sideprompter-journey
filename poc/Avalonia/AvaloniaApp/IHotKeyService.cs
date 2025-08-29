
using System;
using Avalonia.Input;

namespace AvaloniaApp
{
    public interface IHotKeyService : IDisposable
    {
        void RegisterStartRecordingHotKey(Key key, KeyModifiers modifiers, Action action);
        void RegisterWindowCaptureHotKey(Key key, KeyModifiers modifiers, Action action);
        void UnregisterStartRecordingHotKey();
        void UnregisterWindowCaptureHotKey();
    }
}
