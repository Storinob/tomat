using System;
using System.Runtime.InteropServices;

namespace tomat
{
    public static class NativeOsdHider
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private static WinEventDelegate? _hookDelegate;

        public static void StartMonitoring()
        {
            _hookDelegate = WinEventProc;

            SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

            IntPtr hwnd = FindWindow("NativeHWNDHost", null);
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_HIDE);
            }
        }

        private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            IntPtr targetHwnd = FindWindow("NativeHWNDHost", null);

            if (hwnd == targetHwnd && hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_HIDE);
            }
        }
    }
}