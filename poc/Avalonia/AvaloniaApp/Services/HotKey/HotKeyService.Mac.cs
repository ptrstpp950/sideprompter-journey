// macOS implementation of IHotKeyService using Cocoa NSEvent monitoring and Objective-C runtime
// This file is conditionally compiled only on macOS targets.

#if MACOS || OSX || MACCATALYST

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Foundation;
using HotKeyManager;
// ReSharper disable RedundantDefaultMemberInitializer

namespace AvaloniaApp.Services.HotKey
{
    public class HotKeyServiceMacOptionTwo : NSObject, IHotKeyService
    {
        private readonly JFHotkeyManager _hotkeyManager;
        private Action? _startRecordingAction;
        private Action? _windowCaptureAction;
        private bool _startRecordingRegistered = false;
        private bool _windowCaptureRegistered = false;
        private bool _disposed = false;

        public HotKeyServiceMacOptionTwo(Window _)
        {
            _hotkeyManager = new JFHotkeyManager();
        }

        public void RegisterStartRecordingHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HotKeyServiceMacOptionTwo));

            if (_startRecordingRegistered)
                throw new InvalidOperationException("Start recording hotkey is already registered");

            _startRecordingAction = action;

            try
            {
                // Convert Avalonia Key and KeyModifiers to macOS equivalents
                var macModifiers = ConvertToMacModifiers(modifiers);
                var keyCode = ConvertToMacKeyCode(key);

                var selector = new ObjCRuntime.Selector(nameof(OnStartRecordingExecuted));
                _hotkeyManager.BindKeyRef(keyCode, macModifiers, this, selector);

                _startRecordingRegistered = true;
            }
            catch (Exception ex)
            {
                _startRecordingAction = null;
                throw new InvalidOperationException(
                    $"Failed to register start recording hotkey {key}+{modifiers}: {ex.Message}", ex);
            }
        }

        public void RegisterWindowCaptureHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HotKeyServiceMacOptionTwo));

            if (_windowCaptureRegistered)
                throw new InvalidOperationException("Window capture hotkey is already registered");

            _windowCaptureAction = action;

            try
            {
                // Convert Avalonia Key and KeyModifiers to macOS equivalents
                var macModifiers = ConvertToMacModifiers(modifiers);
                var keyCode = ConvertToMacKeyCode(key);

                var selector = new ObjCRuntime.Selector(nameof(OnWindowCaptureExecuted));
                _hotkeyManager.BindKeyRef(keyCode, macModifiers, this, selector);

                _windowCaptureRegistered = true;
            }
            catch (Exception ex)
            {
                _windowCaptureAction = null;
                throw new InvalidOperationException(
                    $"Failed to register window capture hotkey {key}+{modifiers}: {ex.Message}", ex);
            }
        }

        public void UnregisterStartRecordingHotKey()
        {
            if (_disposed)
                return;

            if (_startRecordingRegistered)
            {
                _startRecordingAction = null;
                _startRecordingRegistered = false;
                // Note: JFHotkeyManager might not have an unbind method
                // You may need to implement this based on the actual API
            }
        }

        public void UnregisterWindowCaptureHotKey()
        {
            if (_disposed)
                return;

            if (_windowCaptureRegistered)
            {
                _windowCaptureAction = null;
                _windowCaptureRegistered = false;
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
                Key.Space => 49, // Space bar
                Key.OemQuestion => 44, // Forward slash key (/)
                Key.Enter => 36, // Return key
                Key.Escape => 53, // Escape key
                Key.Tab => 48, // Tab key
                Key.OemPeriod => 47,
                Key.A => 0, // A key
                Key.S => 1, // S key
                Key.D => 2, // D key
                Key.F => 3, // F key
                // Add more key mappings as needed
                _ => throw new NotSupportedException(
                    $"Key {key} is not supported in this implementation. Please add the mapping for this key.")
            };
        }

        [Export(nameof(OnStartRecordingExecuted))]
        void OnStartRecordingExecuted()
        {
            try
            {
                _startRecordingAction?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing start recording hotkey action: {ex.Message}");
            }
        }

        [Export(nameof(OnWindowCaptureExecuted))]
        void OnWindowCaptureExecuted()
        {
            try
            {
                _windowCaptureAction?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing window capture hotkey action: {ex.Message}");
            }
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) 
                return;
            
            if (disposing)
            {
                // Unregister all hotkeys
                UnregisterStartRecordingHotKey();
                UnregisterWindowCaptureHotKey();

                _hotkeyManager.Dispose();
            }

            _disposed = true;
        }
    }
}

#endif