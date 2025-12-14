using Continuance.CLI;
using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Continuance.Roblox.Automation
{
    public static class WindowManager
    {
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]          private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hWnd, string lpString);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern int  GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)]                            private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", SetLastError = true)]                            private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", SetLastError = true)]                            private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll", SetLastError = true)]                            private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]                            private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]                         private static extern bool SystemParametersInfo(int nAction, int nParam, ref RECT rc, int nUpdate);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_HIDE = 0;
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMINNOACTIVE = 7;
        private const int SW_RESTORE = 9;

        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const int SPI_GETWORKAREA = 0x0030;

        public static void TileWindows(List<int> processIds)
        {
            if (processIds == null || processIds.Count == 0) return;

            RECT workArea = new RECT();
            SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0);

            int screenW = workArea.Right - workArea.Left;
            int screenH = workArea.Bottom - workArea.Top;

            int count = processIds.Count;

            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);

            int cellW = screenW / cols;
            int cellH = screenH / rows;

            for (int i = 0; i < count; i++)
            {
                int pid = processIds[i];
                IntPtr hWnd = FindWindowForProcess(pid);

                if (hWnd != IntPtr.Zero)
                {
                    int r = i / cols;
                    int c = i % cols;

                    int x = workArea.Left + (c * cellW);
                    int y = workArea.Top + (r * cellH);

                    ShowWindow(hWnd, SW_RESTORE);

                    MoveWindow(hWnd, x, y, cellW, cellH, true);
                }
            }
        }

        public static void MinimizeWindow(IntPtr hWnd)
        {
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_SHOWMINNOACTIVE);
            }
        }

        private static IntPtr FindWindowForProcess(int processId)
        {
            IntPtr foundHwnd = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                _ = GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                if (windowProcessId == processId)
                {
                    if (IsWindowVisible(hWnd))
                    {
                        var sb = new StringBuilder(256);
                        _ = GetWindowText(hWnd, sb, sb.Capacity);

                        foundHwnd = hWnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return foundHwnd;
        }

        public static async Task<IntPtr> WaitForWindowHandleAsync(int processId, int timeoutSeconds = 60)
        {
            var stopwatch = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            while (stopwatch.Elapsed < timeout)
            {
                IntPtr hWnd = FindWindowForProcess(processId);
                if (hWnd != IntPtr.Zero)
                {
                    var sb = new StringBuilder(256);
                    _ = GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();

                    if (title.Contains("Roblox") || title.Contains("::"))
                    {
                        return hWnd;
                    }
                }
                await Task.Delay(500);
            }
            return IntPtr.Zero;
        }

        public static void ApplyHeadlessToHandle(IntPtr hWnd)
        {
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_HIDE);
                PostMessage(hWnd, WM_SYSCOMMAND, (IntPtr)SC_MINIMIZE, IntPtr.Zero);
            }
        }

        public static void RenameWindowHandle(IntPtr hWnd, string newTitle)
        {
            if (hWnd != IntPtr.Zero)
            {
                SetWindowText(hWnd, newTitle);
            }
        }

        public static async Task MonitorAndRenameWindowAsync(int processId, string newTitle)
        {
            IntPtr initialHwnd = await WaitForWindowHandleAsync(processId);

            if (initialHwnd == IntPtr.Zero)
            {
                Logger.LogWarning($"Could not find window for PID {processId} to rename.");
                return;
            }

            RenameWindowHandle(initialHwnd, newTitle);
            Logger.LogSuccess($"Window renamed to: {Markup.Escape(newTitle)}");

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < 45)
            {
                try
                {
                    Process p = Process.GetProcessById(processId);
                    if (p.HasExited) break;

                    IntPtr currentHwnd = FindWindowForProcess(processId);

                    if (currentHwnd != IntPtr.Zero)
                    {
                        var sb = new StringBuilder(256);
                        _ = GetWindowText(currentHwnd, sb, sb.Capacity);
                        string currentTitle = sb.ToString();

                        if (currentTitle == "Roblox")
                        {
                            RenameWindowHandle(currentHwnd, newTitle);
                        }
                    }
                }
                catch
                {
                    break;
                }

                await Task.Delay(1000);
            }
        }

        public static async Task ApplyHeadlessModeAsync(int processId)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < 10)
            {
                IntPtr hWnd = FindWindowForProcess(processId);
                if (hWnd != IntPtr.Zero)
                {
                    ApplyHeadlessToHandle(hWnd);
                }
                await Task.Delay(1000);
            }
            Logger.LogSuccess($"[Headless] Hidden and minimized window for PID {processId}.");
        }

        public static async Task TryRenameWindowAsync(int processId, string newTitle)
        {
            IntPtr targetHWnd = await WaitForWindowHandleAsync(processId);
            if (targetHWnd != IntPtr.Zero)
            {
                RenameWindowHandle(targetHWnd, newTitle);
                Logger.LogSuccess($"Renamed window for PID {processId} to '{newTitle}'.");
            }
            else
            {
                Logger.LogWarning($"Could not find main 'Roblox' window for PID {processId} within timeout.");
            }
        }
    }
}