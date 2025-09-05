using Interop.UIAutomationClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GooglePal.Components
{
    internal class ForceShowWindow
    {

        // imports
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MAXIMIZE = 3;


        public class TopLevelWindowInfo
        {
            public IntPtr Hwnd { get; set; }
            public string Title { get; set; } = "";
            public string ProcessName { get; set; } = "";
        }


        /// <summary>
        ///     Get all currently active windows (minimised, maximised, windowed, full screen).
        /// </summary>
        /// <returns>A list of open windows</returns>
        public static List<TopLevelWindowInfo> GetTopLevelWindows()
        {
            var windows = new List<TopLevelWindowInfo>();

            EnumWindows((hWnd, lParam) =>
            {
                // don't return chrome's window spawning process as the window
                if (!IsWindowVisible(hWnd))
                    return true; // continue

                // get window title
                var titleBuilder = new StringBuilder(1024);
                _ = GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
                string title = titleBuilder.ToString();

                // get exe file name
                _ = GetWindowThreadProcessId(hWnd, out uint pid);
                string procName = "";

                try
                {
                    procName = Process.GetProcessById((int)pid).ProcessName;
                }
                catch { /* ignore */ }


                // add window to list
                windows.Add(new TopLevelWindowInfo
                {
                    Hwnd = hWnd,
                    Title = title,
                    ProcessName = procName
                });

                return true;

            }, IntPtr.Zero);

            return windows;
        }

        /// <summary>
        ///     Find all currently open windows that belong to a process by its name (filename).
        /// </summary>
        /// <param name="processName">The file name of the process who spawned the windows.</param>
        /// <example>FindProcessWindows("chrome")</example>
        /// <returns>A list of currently open windows.</returns>
        public static List<TopLevelWindowInfo> FindProcessWindows(string processName) =>
            GetTopLevelWindows()
                .Where(w => string.Equals(w.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                .ToList();

        
        /// <summary>
        ///     Maximise window and bring it into focus given its handle
        /// </summary>
        /// <param name="hWnd">handle to the window being focused</param>
        /// <returns>
        ///     A bool where: <br/>
        ///         - True if the window was focussed successfully <br/>
        ///         - False if the window failed to focus.
        /// </returns>
        public static bool ForceFocusWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            // return if already focussed
            if (GetForegroundWindow() == hWnd) return true;

            try
            {
                // full screen
                ShowWindow(hWnd, SW_MAXIMIZE);

                // request focus via the accessibility framework
                IUIAutomationElement element = new CUIAutomation().ElementFromHandle(hWnd);

                if (element != null)
                {
                    // try to set focus
                    try { element.SetFocus(); }

                    catch (Exception ex) { Debug.WriteLine($"SetFocus failed: {ex.Message}"); }
                }

                // return if window has focus
                return GetForegroundWindow() == hWnd;
            }
            catch
            {
                return false;
            }
        }
    }
}
