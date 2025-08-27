// macOS implementation of IHotKeyService using Carbon framework
// This file is conditionally compiled only on macOS targets.

#if MACOS || OSX || MACCATALYST

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AvaloniaApp
{
    public class HotKeyServiceMac : IHotKeyService
    {
        private readonly Dictionary<uint, (EventHotKeyRef hotKeyRef, Action action)> _registeredHotKeys = new Dictionary<uint, (EventHotKeyRef, Action)>();
        private uint _currentId = 0;
        private bool _isDisposed = false;
        private IntPtr _eventHandler;
        private EventTargetRef _eventTarget;

        public HotKeyServiceMac(Window window)
        {
            // Install Carbon event handler for hotkeys
            InstallApplicationEventHandler();
        }

        private void InstallApplicationEventHandler()
        {
            // Create event handler UPP (Universal Procedure Pointer)
            _eventHandler = Marshal.GetFunctionPointerForDelegate<EventHandlerProcPtr>(EventHandler);
            
            // Get application event target
            _eventTarget = GetApplicationEventTarget();
            
            // Define event types we want to handle
            var eventTypes = new EventTypeSpec[]
            {
                new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyPressed }
            };

            // Install event handler
            var status = InstallEventHandler(_eventTarget, _eventHandler, 1, eventTypes, IntPtr.Zero, out _);
            if (status != 0)
            {
                throw new InvalidOperationException($"Failed to install Carbon event handler. Status: {status}");
            }
        }

        public int RegisterGlobalHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(HotKeyServiceMac));
            
            uint id = ++_currentId;
            uint keyCode = KeyToMacKeyCode(key);
            uint modifierFlags = ConvertModifiers(modifiers);

            var hotKeyId = new EventHotKeyID
            {
                signature = OSTypeFromString("HTKY"),  // 'HTKY' signature
                id = id
            };

            var status = RegisterEventHotKey(keyCode, modifierFlags, hotKeyId, _eventTarget, 0, out EventHotKeyRef hotKeyRef);
            
            if (status == 0) // noErr
            {
                _registeredHotKeys.Add(id, (hotKeyRef, action));
                return (int)id;
            }
            else
            {
                throw new InvalidOperationException($"Failed to register hotkey. Carbon status: {status}");
            }
        }

        public void UnregisterGlobalHotKey(int id)
        {
            if (_isDisposed) return;

            uint uintId = (uint)id;
            if (_registeredHotKeys.TryGetValue(uintId, out var hotKeyData))
            {
                UnregisterEventHotKey(hotKeyData.hotKeyRef);
                _registeredHotKeys.Remove(uintId);
            }
        }

        private int EventHandler(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
        {
            // Get event class and kind
            uint eventClass = GetEventClass(theEvent);
            uint eventKind = GetEventKind(theEvent);

            if (eventClass == kEventClassKeyboard && eventKind == kEventHotKeyPressed)
            {
                // Get the hotkey ID from the event
                var hotKeyId = new EventHotKeyID();
                var status = GetEventParameter(theEvent, kEventParamDirectObject, typeEventHotKeyID,
                    IntPtr.Zero, (uint)Marshal.SizeOf<EventHotKeyID>(), IntPtr.Zero, ref hotKeyId);

                if (status == 0 && _registeredHotKeys.TryGetValue(hotKeyId.id, out var hotKeyData))
                {
                    // Invoke the associated action
                    hotKeyData.action?.Invoke();
                    return 0; // Event handled
                }
            }

            // Pass event to next handler
            return CallNextEventHandler(nextHandler, theEvent);
        }

        private static uint KeyToMacKeyCode(Key key)
        {
            // Map Avalonia Key to macOS virtual key codes
            switch (key)
            {
                case Key.A: return 0x00; // kVK_ANSI_A
                case Key.B: return 0x0B; // kVK_ANSI_B
                case Key.C: return 0x08; // kVK_ANSI_C
                case Key.D: return 0x02; // kVK_ANSI_D
                case Key.E: return 0x0E; // kVK_ANSI_E
                case Key.F: return 0x03; // kVK_ANSI_F
                case Key.G: return 0x05; // kVK_ANSI_G
                case Key.H: return 0x04; // kVK_ANSI_H
                case Key.I: return 0x22; // kVK_ANSI_I
                case Key.J: return 0x26; // kVK_ANSI_J
                case Key.K: return 0x28; // kVK_ANSI_K
                case Key.L: return 0x25; // kVK_ANSI_L
                case Key.M: return 0x2E; // kVK_ANSI_M
                case Key.N: return 0x2D; // kVK_ANSI_N
                case Key.O: return 0x1F; // kVK_ANSI_O
                case Key.P: return 0x23; // kVK_ANSI_P
                case Key.Q: return 0x0C; // kVK_ANSI_Q
                case Key.R: return 0x0F; // kVK_ANSI_R
                case Key.S: return 0x01; // kVK_ANSI_S
                case Key.T: return 0x11; // kVK_ANSI_T
                case Key.U: return 0x20; // kVK_ANSI_U
                case Key.V: return 0x09; // kVK_ANSI_V
                case Key.W: return 0x0D; // kVK_ANSI_W
                case Key.X: return 0x07; // kVK_ANSI_X
                case Key.Y: return 0x10; // kVK_ANSI_Y
                case Key.Z: return 0x06; // kVK_ANSI_Z
                case Key.OemQuestion: return 0x2C; // kVK_ANSI_Slash (/)
                // Add more key mappings as needed
                default:
                    throw new NotSupportedException($"Key '{key}' is not supported on macOS");
            }
        }

        private static uint ConvertModifiers(KeyModifiers modifiers)
        {
            uint macModifiers = 0;
            
            if (modifiers.HasFlag(KeyModifiers.Shift))
                macModifiers |= shiftKey;
            if (modifiers.HasFlag(KeyModifiers.Control))
                macModifiers |= controlKey;
            if (modifiers.HasFlag(KeyModifiers.Alt))
                macModifiers |= optionKey;
            if (modifiers.HasFlag(KeyModifiers.Meta))
                macModifiers |= cmdKey;
                
            return macModifiers;
        }

        private static uint OSTypeFromString(string str)
        {
            if (str.Length != 4)
                throw new ArgumentException("OSType string must be exactly 4 characters");
                
            return (uint)((str[0] << 24) | (str[1] << 16) | (str[2] << 8) | str[3]);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                // Unregister all hotkeys
                foreach (var kvp in _registeredHotKeys)
                {
                    UnregisterEventHotKey(kvp.Value.hotKeyRef);
                }
                
                _registeredHotKeys.Clear();
                _isDisposed = true;
            }
        }

        // Carbon framework constants
        private const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
        private const uint kEventHotKeyPressed = 5;
        private const uint kEventParamDirectObject = 0x2D2D2D2D; // '----'
        private const uint typeEventHotKeyID = 0x686B6579; // 'hkey'
        
        // Modifier key constants
        private const uint shiftKey = 0x0200;
        private const uint controlKey = 0x1000;
        private const uint optionKey = 0x0800;
        private const uint cmdKey = 0x0100;

        // Carbon framework structures
        [StructLayout(LayoutKind.Sequential)]
        private struct EventHotKeyID
        {
            public uint signature;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventHotKeyRef
        {
            public IntPtr value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventTypeSpec
        {
            public uint eventClass;
            public uint eventKind;
        }

        // Carbon framework type aliases
        private struct EventTargetRef
        {
            public IntPtr value;
        }

        // Event handler delegate
        private delegate int EventHandlerProcPtr(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

        // Carbon framework function imports
        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int RegisterEventHotKey(uint inHotKeyCode, uint inHotKeyModifiers, 
            EventHotKeyID inHotKeyID, EventTargetRef inTarget, uint inOptions, out EventHotKeyRef outRef);

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int UnregisterEventHotKey(EventHotKeyRef inHotKey);

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern EventTargetRef GetApplicationEventTarget();

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int InstallEventHandler(EventTargetRef inTarget, IntPtr inHandler, uint inNumTypes,
            EventTypeSpec[] inList, IntPtr inUserData, out IntPtr outRef);

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern uint GetEventClass(IntPtr inEvent);

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern uint GetEventKind(IntPtr inEvent);

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int GetEventParameter(IntPtr inEvent, uint inName, uint inDesiredType,
            IntPtr outActualType, uint inBufferSize, IntPtr outActualSize, ref EventHotKeyID outData);

        [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
        private static extern int CallNextEventHandler(IntPtr nextHandler, IntPtr theEvent);
    }
}

#endif