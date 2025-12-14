using Continuance.CLI;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Continuance.Roblox.Automation
{
    public static class WindowManager
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_HIDE = 0;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;

        public static async Task ApplyHeadlessModeAsync(int processId)
        {
            IntPtr targetHWnd = await WaitForWindowHandle(processId);

            if (targetHWnd != IntPtr.Zero)
            {
                ShowWindow(targetHWnd, SW_HIDE);
                PostMessage(targetHWnd, WM_SYSCOMMAND, (IntPtr)SC_MINIMIZE, IntPtr.Zero);
                Logger.LogSuccess($"[Headless] Hidden and minimized window for PID {processId}.");
            }
        }

        public static async Task TryRenameWindowAsync(int processId, string newTitle)
        {
            IntPtr targetHWnd = await WaitForWindowHandle(processId);

            if (targetHWnd != IntPtr.Zero)
            {
                SetWindowText(targetHWnd, newTitle);
                Logger.LogSuccess($"Renamed window for PID {processId} to '{newTitle}'.");
            }
            else
            {
                Logger.LogWarning($"Could not find main 'Roblox' window for PID {processId} within timeout.");
            }
        }

        private static async Task<IntPtr> WaitForWindowHandle(int processId)
        {
            IntPtr foundHwnd = IntPtr.Zero;
            var stopwatch = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(45);

            while (stopwatch.Elapsed < timeout)
            {
                EnumWindows((hWnd, lParam) =>
                {
                    _ = GetWindowThreadProcessId(hWnd, out uint windowProcessId);

                    if (windowProcessId == processId)
                    {
                        var sb = new StringBuilder(256);

                        _ = GetWindowText(hWnd, sb, sb.Capacity);

                        if (sb.ToString() == "Roblox" && IsWindowVisible(hWnd))
                        {
                            foundHwnd = hWnd;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                if (foundHwnd != IntPtr.Zero)
                {
                    break;
                }
                await Task.Delay(500);
            }
            return foundHwnd;
        }
    }
}