
using System;
using Avalonia.Input;

namespace AvaloniaApp.Services.HotKey
{
    public interface IHotKeyService : IDisposable
    {
        void RegisterAiHelpNeededHotKey(Key key, KeyModifiers modifiers, Action action);
        void RegisterWindowCaptureHotKey(Key key, KeyModifiers modifiers, Action action);
        void UnregisterStartRecordingHotKey();
        void UnregisterWindowCaptureHotKey();
    }
}
