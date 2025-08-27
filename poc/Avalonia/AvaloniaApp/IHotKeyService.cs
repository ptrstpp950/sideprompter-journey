
using System;
using Avalonia.Input;

namespace AvaloniaApp
{
    public interface IHotKeyService : IDisposable
    {
        int RegisterGlobalHotKey(Key key, KeyModifiers modifiers, Action action);
        void UnregisterGlobalHotKey(int id);
    }
}
