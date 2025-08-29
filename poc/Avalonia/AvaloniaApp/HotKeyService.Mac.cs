// macOS implementation of IHotKeyService using Cocoa NSEvent monitoring and Objective-C runtime
// This file is conditionally compiled only on macOS targets.

#if MACOS || OSX || MACCATALYST

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Foundation;
using HotKeyManager;

namespace AvaloniaApp
{
    public class HotKeyServiceMacOptionTwo : NSObject, IHotKeyService
    {
        private readonly Window _window;
        private readonly JFHotkeyManager _hotkeyManager;
        private readonly Dictionary<int, Action> _registeredHotKeys;
        private int _nextHotKeyId = 1;
        private bool _disposed = false;

        public HotKeyServiceMacOptionTwo(Window window)
        {
            _window = window;
            _hotkeyManager = new JFHotkeyManager();
            _registeredHotKeys = new Dictionary<int, Action>();
        }

        public int RegisterGlobalHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HotKeyServiceMacOptionTwo));

            var hotKeyId = _nextHotKeyId++;
            _registeredHotKeys[hotKeyId] = action;

            try
            {
                // Convert Avalonia Key and KeyModifiers to macOS equivalents
                var macModifiers = ConvertToMacModifiers(modifiers);
                var keyCode = ConvertToMacKeyCode(key);

                // For now, we'll use the generic callback method
                // Note: This is a limitation of the current approach - all hotkeys will trigger the same callback
                // In a production implementation, you might need to use a different strategy
                var selector = new ObjCRuntime.Selector("onHotkeyExecuted");
                _hotkeyManager.BindKeyRef(keyCode, macModifiers, this, selector);

                return hotKeyId;
            }
            catch (Exception ex)
            {
                // If registration fails, remove from our tracking
                _registeredHotKeys.Remove(hotKeyId);
                throw new InvalidOperationException($"Failed to register hotkey {key}+{modifiers}: {ex.Message}", ex);
            }
        }

        public void UnregisterGlobalHotKey(int id)
        {
            if (_disposed)
                return;

            if (_registeredHotKeys.ContainsKey(id))
            {
                _registeredHotKeys.Remove(id);
                // Note: JFHotkeyManager might not have an unbind method
                // You may need to implement this based on the actual API
            }
        }

        private uint ConvertToMacModifiers(KeyModifiers modifiers)
        {
            uint macModifiers = 0;

            // Based on the original code, we know CmdKey and ShiftKey exist
            // Map common modifiers to macOS equivalents
            if (modifiers.HasFlag(KeyModifiers.Control))
                macModifiers |= (uint)EModifierKeys.CmdKey; // On macOS, Ctrl often maps to Cmd
            if (modifiers.HasFlag(KeyModifiers.Shift))
                macModifiers |= (uint)EModifierKeys.ShiftKey;
            if (modifiers.HasFlag(KeyModifiers.Meta))
                macModifiers |= (uint)EModifierKeys.CmdKey; // Meta is typically Cmd on macOS
            if (modifiers.HasFlag(KeyModifiers.Alt))
                macModifiers |= (uint)EModifierKeys.OptionKey;

            return macModifiers;
        }

        private uint ConvertToMacKeyCode(Key key)
        {
            // macOS virtual key codes - this is a simplified mapping
            // You'll need to expand this based on your needs
            return key switch
            {
                Key.Space => 49,                // Space bar
                Key.OemQuestion => 44,          // Forward slash key (/)
                Key.Enter => 36,                // Return key
                Key.Escape => 53,               // Escape key
                Key.Tab => 48,                  // Tab key
                Key.A => 0,                     // A key
                Key.S => 1,                     // S key
                Key.D => 2,                     // D key
                Key.F => 3,                     // F key
                // Add more key mappings as needed
                _ => throw new NotSupportedException($"Key {key} is not supported in this implementation. Please add the mapping for this key.")
            };
        }

        [Export("onHotkeyExecuted")]
        void OnHotkeyExecuted()
        {
            // Since the current HotKeyManager binding doesn't provide a way to identify which specific hotkey was pressed,
            // we have a limitation: all registered hotkeys will trigger all actions
            // This is a simplified implementation - in production you might need a different approach
            
            try
            {
                // Execute all registered actions
                // Note: This is not ideal but works as a starting point
                foreach (var action in _registeredHotKeys.Values.ToList())
                {
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        // Log the error but don't let one action failure stop others
                        Console.WriteLine($"Error executing hotkey action: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnHotkeyExecuted: {ex.Message}");
            }
        }

        // Generic hotkey execution method - in a real implementation, 
        // you'd need a more sophisticated way to route callbacks
        private void ExecuteHotKeyAction(int hotKeyId)
        {
            if (_registeredHotKeys.TryGetValue(hotKeyId, out var action))
            {
                action?.Invoke();
            }
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unregister all hotkeys
                    var hotKeyIds = new List<int>(_registeredHotKeys.Keys);
                    foreach (var id in hotKeyIds)
                    {
                        UnregisterGlobalHotKey(id);
                    }
                    
                    _registeredHotKeys.Clear();
                    _hotkeyManager?.Dispose();
                }
                
                _disposed = true;
            }
        }
    }
}

#endif