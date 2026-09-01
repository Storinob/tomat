using System;
using System.Runtime.InteropServices;
using System.Text;

namespace tomat
{
    public static class NativeOsdHider
    {
        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_HIDEWINDOW = 0x0080;
        private static WinEventDelegate? _hookDelegate;
        private static IntPtr _hookHandle = IntPtr.Zero;
        private delegate void WinEventDelegate(
            IntPtr hWinEventHook, 
            uint eventType, 
            IntPtr hwnd, 
            int idObject, 
            int idChild, 
            uint dwEventThread, 
            uint dwmsEventTime);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, 
            uint eventMax, 
            IntPtr hmodWinEventProc, 
            WinEventDelegate lpfnWinEventProc, 
            uint idProcess, 
            uint idThread, 
            uint dwFlags);

        public static void StartMonitoring()
        {
            if (_hookHandle != IntPtr.Zero) return;
            _hookDelegate = WinEventProc;
            _hookHandle = SetWinEventHook(
                EVENT_OBJECT_SHOW, 
                EVENT_OBJECT_LOCATIONCHANGE, 
                IntPtr.Zero, 
                _hookDelegate, 
                0, 
                0, 
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            HideNativeOsd();
        }
        private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != 0) return; // 0 = OBJID_WINDOW

            StringBuilder className = new(256);
            if (GetClassName(hwnd, className, className.Capacity) > 0)
            {
                if (className.ToString() == "NativeHWNDHost")
                {
                    ForceHideWindow(hwnd);
                }
            }
        }
        private static void HideNativeOsd()
        {
            IntPtr hwnd = FindWindow("NativeHWNDHost", null);
            if (hwnd != IntPtr.Zero)
            {
                ForceHideWindow(hwnd);
            }
        }
        private static void ForceHideWindow(IntPtr hwnd)
        {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
        }
    }
}