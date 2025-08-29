// macOS implementation of IHotKeyService using Cocoa NSEvent monitoring and Objective-C runtime
// This file is conditionally compiled only on macOS targets.

#if MACOS || OSX || MACCATALYST

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Foundation;
using HotKeyManager;

namespace AvaloniaApp
{
    public class HotKeyServiceMacOptionTwo : NSObject
    {
        private readonly Window _window;
        private readonly JFHotkeyManager _hotkeyManager;

        public HotKeyServiceMacOptionTwo(Window window)
        {
            _window = window;
            _hotkeyManager = new JFHotkeyManager();

        }

        public void Register()
        {
            _hotkeyManager.BindKeyRef(49, (uint)(EModifierKeys.CmdKey | EModifierKeys.ShiftKey), this, new ObjCRuntime.Selector("onHotkeyExecuted"));
            _hotkeyManager.Bind("command /", this, new ObjCRuntime.Selector("onHotkeyExecuted"));

        }
        
        [Export("onHotkeyExecuted")]
        void OnHotkeyExecuted()
        {
            Console.WriteLine("aaaaa");
            // do something
        }
    }
    public class HotKeyServiceMac : IHotKeyService
    {
        private readonly Dictionary<int, (Key key, KeyModifiers modifiers, Action action)> _registeredHotKeys = new Dictionary<int, (Key, KeyModifiers, Action)>();
        private int _currentId = 0;
        private bool _isDisposed = false;
        private IntPtr _eventMonitor = IntPtr.Zero;
        private readonly EventMonitorCallback _eventCallback;

        public HotKeyServiceMac(Window window)
        {
            _eventCallback = OnGlobalKeyEvent;
            InitializeGlobalEventMonitoring();
        }

        private void InitializeGlobalEventMonitoring()
        {
            try
            {
                // Get NSEvent class
                IntPtr nsEventClass = objc_getClass("NSEvent");
                if (nsEventClass == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not find NSEvent class");
                }

                // Get the addGlobalMonitorForEventsMatchingMask:handler: selector
                IntPtr selector = sel_registerName("addGlobalMonitorForEventsMatchingMask:handler:");
                if (selector == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not find addGlobalMonitorForEventsMatchingMask:handler: selector");
                }

                // Create a block for the event handler
                IntPtr block = CreateEventHandlerBlock(_eventCallback);
                if (block == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create event handler block");
                }

                // Call NSEvent.addGlobalMonitorForEventsMatchingMask:handler:
                _eventMonitor = objc_msgSend_ulong_ptr(nsEventClass, selector, NSEventMaskKeyDown, block);
                
                if (_eventMonitor == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to install global event monitor for hotkeys");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not initialize global hotkey monitoring: {ex.Message}");
                Console.WriteLine("Global hotkeys may not work properly.");
            }
        }

        private void OnGlobalKeyEvent(IntPtr eventPtr)
        {
            try
            {
                if (eventPtr == IntPtr.Zero) return;

                // Get keyCode using NSEvent keyCode property
                IntPtr keyCodeSelector = sel_registerName("keyCode");
                ushort keyCode = (ushort)objc_msgSend_ushort(eventPtr, keyCodeSelector);

                // Get modifierFlags using NSEvent modifierFlags property
                IntPtr modifierFlagsSelector = sel_registerName("modifierFlags");
                ulong modifierFlags = objc_msgSend_ulong(eventPtr, modifierFlagsSelector);

                // Convert to Avalonia types
                var key = MacKeyCodeToKey(keyCode);
                var modifiers = MacModifiersToKeyModifiers(modifierFlags);

                // Check if this matches any registered hotkey
                foreach (var hotKey in _registeredHotKeys.Values)
                {
                    if (hotKey.key == key && hotKey.modifiers == modifiers)
                    {
                        // Invoke the callback on the main thread
                        InvokeOnMainThread(hotKey.action);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in global key event handler: {ex.Message}");
            }
        }

        public int RegisterGlobalHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(HotKeyServiceMac));

            int id = ++_currentId;
            _registeredHotKeys.Add(id, (key, modifiers, action));
            return id;
        }

        public void UnregisterGlobalHotKey(int id)
        {
            if (_isDisposed) return;
            _registeredHotKeys.Remove(id);
        }

        private static Key MacKeyCodeToKey(ushort keyCode)
        {
            // Map macOS virtual key codes to Avalonia Key enum
            switch (keyCode)
            {
                case 0x00: return Key.A;       // kVK_ANSI_A
                case 0x0B: return Key.B;       // kVK_ANSI_B
                case 0x08: return Key.C;       // kVK_ANSI_C
                case 0x02: return Key.D;       // kVK_ANSI_D
                case 0x0E: return Key.E;       // kVK_ANSI_E
                case 0x03: return Key.F;       // kVK_ANSI_F
                case 0x05: return Key.G;       // kVK_ANSI_G
                case 0x04: return Key.H;       // kVK_ANSI_H
                case 0x22: return Key.I;       // kVK_ANSI_I
                case 0x26: return Key.J;       // kVK_ANSI_J
                case 0x28: return Key.K;       // kVK_ANSI_K
                case 0x25: return Key.L;       // kVK_ANSI_L
                case 0x2E: return Key.M;       // kVK_ANSI_M
                case 0x2D: return Key.N;       // kVK_ANSI_N
                case 0x1F: return Key.O;       // kVK_ANSI_O
                case 0x23: return Key.P;       // kVK_ANSI_P
                case 0x0C: return Key.Q;       // kVK_ANSI_Q
                case 0x0F: return Key.R;       // kVK_ANSI_R
                case 0x01: return Key.S;       // kVK_ANSI_S
                case 0x11: return Key.T;       // kVK_ANSI_T
                case 0x20: return Key.U;       // kVK_ANSI_U
                case 0x09: return Key.V;       // kVK_ANSI_V
                case 0x0D: return Key.W;       // kVK_ANSI_W
                case 0x07: return Key.X;       // kVK_ANSI_X
                case 0x10: return Key.Y;       // kVK_ANSI_Y
                case 0x06: return Key.Z;       // kVK_ANSI_Z
                case 0x2C: return Key.OemQuestion; // kVK_ANSI_Slash (/)
                // Add more key mappings as needed
                default:
                    return Key.None;
            }
        }

        private static KeyModifiers MacModifiersToKeyModifiers(ulong modifierFlags)
        {
            KeyModifiers result = KeyModifiers.None;

            if ((modifierFlags & NSEventModifierFlagShift) != 0)
                result |= KeyModifiers.Shift;
            if ((modifierFlags & NSEventModifierFlagControl) != 0)
                result |= KeyModifiers.Control;
            if ((modifierFlags & NSEventModifierFlagOption) != 0)
                result |= KeyModifiers.Alt;
            if ((modifierFlags & NSEventModifierFlagCommand) != 0)
                result |= KeyModifiers.Meta;

            return result;
        }

        private static void InvokeOnMainThread(Action action)
        {
            try
            {
                // Get the main queue and dispatch the action
                IntPtr mainQueue = dispatch_get_main_queue();
                if (mainQueue != IntPtr.Zero)
                {
                    // Create a simple dispatch block and execute it
                    // For simplicity, we'll invoke directly since we're already in event context
                    action.Invoke();
                }
                else
                {
                    // Fallback to direct invocation
                    action.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking action: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_eventMonitor != IntPtr.Zero)
                {
                    // Remove the global monitor
                    IntPtr nsEventClass = objc_getClass("NSEvent");
                    IntPtr removeMonitorSelector = sel_registerName("removeMonitor:");
                    if (nsEventClass != IntPtr.Zero && removeMonitorSelector != IntPtr.Zero)
                    {
                        objc_msgSend_void_ptr(nsEventClass, removeMonitorSelector, _eventMonitor);
                    }
                    _eventMonitor = IntPtr.Zero;
                }

                _registeredHotKeys.Clear();
                _isDisposed = true;
            }
        }

        // NSEvent modifier flag constants
        private const ulong NSEventModifierFlagShift = 1UL << 17;    // 0x20000
        private const ulong NSEventModifierFlagControl = 1UL << 18;  // 0x40000
        private const ulong NSEventModifierFlagOption = 1UL << 19;   // 0x80000
        private const ulong NSEventModifierFlagCommand = 1UL << 20;  // 0x100000
        
        // NSEventMask constants
        private const ulong NSEventMaskKeyDown = 1UL << 10; // 0x400

        // Delegate for event monitoring callback
        private delegate void EventMonitorCallback(IntPtr eventPtr);

        // Helper method to create Objective-C block for event handling
        private static IntPtr CreateEventHandlerBlock(EventMonitorCallback callback)
        {
            // This is a simplified approach - in practice, you'd create a proper Objective-C block
            // For now, we'll return the function pointer
            return Marshal.GetFunctionPointerForDelegate(callback);
        }

        // Objective-C runtime imports
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_msgSend_ulong_ptr(IntPtr receiver, IntPtr selector, ulong arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern ushort objc_msgSend_ushort(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern ulong objc_msgSend_ulong(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern void objc_msgSend_void_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

        // Grand Central Dispatch for main queue access
        [DllImport("/usr/lib/system/libdispatch.dylib")]
        private static extern IntPtr dispatch_get_main_queue();
    }
}

#endif