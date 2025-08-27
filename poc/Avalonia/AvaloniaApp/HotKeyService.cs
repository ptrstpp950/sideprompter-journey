#if WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AvaloniaApp
{

    public class HotKeyServiceWindows : IHotKeyService
    {
        private const int WM_HOTKEY = 0x0312;
        
        private readonly Dictionary<int, Action> _registeredHotKeys = new Dictionary<int, Action>();
        private int _currentId = 0;
        private readonly Window _window;
        private bool _isDisposed = false;
        
        // For handling window messages
        private IntPtr _hwnd;
        private IntPtr _prevWndProc;
        private Win32WindowProc? _wndProc;
        
        public HotKeyServiceWindows(Window window)
        {
            _window = window;
            SetupMessageHook();
        }
        
        private void SetupMessageHook()
        {
            if (!OperatingSystem.IsWindows())
                return;
                
            var handle = _window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero)
                return;
                
            _hwnd = handle.Handle;
            _wndProc = WindowProc;
            _prevWndProc = SetWindowLongPtr(_hwnd, -4 /* GWLP_WNDPROC */, Marshal.GetFunctionPointerForDelegate(_wndProc));
        }
        
        private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY && _registeredHotKeys.TryGetValue((int)wParam, out var action))
            {
                action?.Invoke();
                return IntPtr.Zero;
            }
            
            return CallWindowProc(_prevWndProc, hwnd, msg, wParam, lParam);
        }

        public int RegisterGlobalHotKey(Key key, KeyModifiers modifiers, Action action)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(HotKeyServiceWindows));
            if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero)
                throw new PlatformNotSupportedException("Global hotkeys are only supported on Windows.");

            int id = ++_currentId;
            
            uint modifierFlags = 0;
            if (modifiers.HasFlag(KeyModifiers.Alt)) modifierFlags |= MOD_ALT;
            if (modifiers.HasFlag(KeyModifiers.Control)) modifierFlags |= MOD_CONTROL;
            if (modifiers.HasFlag(KeyModifiers.Shift)) modifierFlags |= MOD_SHIFT;
            if (modifiers.HasFlag(KeyModifiers.Meta)) modifierFlags |= MOD_WIN;
            
            uint vk = KeyToVirtualKey(key);

            if (RegisterHotKey(_hwnd, id, modifierFlags, vk))
            {
                _registeredHotKeys.Add(id, action);
                return id;
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to register hot key. Error code: {error}");
            }
        }

        public void UnregisterGlobalHotKey(int id)
        {
            if (_isDisposed || !OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero) 
                return;

            if (_registeredHotKeys.ContainsKey(id))
            {
                UnregisterHotKey(_hwnd, id);
                _registeredHotKeys.Remove(id);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (OperatingSystem.IsWindows() && _hwnd != IntPtr.Zero)
                {
                    // Unregister all hotkeys
                    foreach (var id in _registeredHotKeys.Keys)
                    {
                        UnregisterHotKey(_hwnd, id);
                    }
                    
                    // Restore original window procedure
                    if (_prevWndProc != IntPtr.Zero)
                    {
                        SetWindowLongPtr(_hwnd, -4 /* GWLP_WNDPROC */, _prevWndProc);
                    }
                }
                
                _registeredHotKeys.Clear();
                _isDisposed = true;
            }
        }
        
        private static uint KeyToVirtualKey(Key key)
        {
            // Map from Avalonia Key to Windows Virtual Key codes
            switch (key)
            {
                case Key.OemQuestion:
                    return 0xBF;  // VK_OEM_2 (/?))
                case Key.A:
                    return 0x41;  // VK_A
                case Key.B:
                    return 0x42;  // VK_B
                case Key.C:
                    return 0x43;  // VK_C
                case Key.D:
                    return 0x44;  // VK_D
                case Key.E:
                    return 0x45;  // VK_E
                case Key.F:
                    return 0x46;  // VK_F
                case Key.G:
                    return 0x47;  // VK_G
                case Key.H:
                    return 0x48;  // VK_H
                case Key.I:
                    return 0x49;  // VK_I
                case Key.J:
                    return 0x4A;  // VK_J
                case Key.K:
                    return 0x4B;  // VK_K
                case Key.L:
                    return 0x4C;  // VK_L
                case Key.M:
                    return 0x4D;  // VK_M
                case Key.N:
                    return 0x4E;  // VK_N
                case Key.O:
                    return 0x4F;  // VK_O
                case Key.P:
                    return 0x50;  // VK_P
                case Key.Q:
                    return 0x51;  // VK_Q
                case Key.R:
                    return 0x52;  // VK_R
                case Key.S:
                    return 0x53;  // VK_S
                case Key.T:
                    return 0x54;  // VK_T
                case Key.U:
                    return 0x55;  // VK_U
                case Key.V:
                    return 0x56;  // VK_V
                case Key.W:
                    return 0x57;  // VK_W
                case Key.X:
                    return 0x58;  // VK_X
                case Key.Y:
                    return 0x59;  // VK_Y
                case Key.Z:
                    return 0x5A;  // VK_Z
                default:
                    return (uint)key;  // Most keys have the same value
            }
        }

        // Win32 API constants
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        // Win32 API delegates
        private delegate IntPtr Win32WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Win32 API imports
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
#endif