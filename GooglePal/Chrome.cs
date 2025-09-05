using GooglePal.Pages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Interop.UIAutomationClient;

namespace GooglePal
{
    internal class Chrome : Google
    {
        public class Profile
        {
            public required string Name { get; set; }
            public required string Folder { get; set; }
            public required string ProfilePicturePath { get; set; }
        }


        // history
        private string HistoryConfigPath = Path.Combine(
            Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(),
            "history.json"
        );
        private int HistoryLength = 999;

        //private int LevenshteinMaxDistance = 3;

        public List<Suggestion> History;

        public Chrome()
        {
            History = LoadHistory();
        }

        public void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
        {
            // update history length
            HistoryLength = eventArgs.HistoryLength;

            // truncate history if required
            if (History.Count > HistoryLength)
                History = History.Skip(History.Count - HistoryLength).ToList();

            // save
            SaveHistory();
        }

        public override async Task<Suggestions> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            Suggestions suggestions = await base.GetSuggestionsAsync(query, cancellationToken);

            // change type to history if suggestion in history
            foreach (Suggestion suggestion in suggestions.suggestions)
            {
                if (History.Any(item => item.Query == suggestion.Query)) suggestion.Type = SuggestionType.History;
            }

            return suggestions;
        }


        /// <summary>
        ///     Gets a list of Chrome's profile names and their respective directory names.
        /// </summary>
        /// <returns>A List of Chrome.Profile objects with the Name and Folder of the profile.</returns>
        public static List<Profile> GetChromeProfiles()
        {
            string defaultProfilePicture = "\uE77B";

            List<Profile> profiles = [];

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string localStateFilePath = Path.Combine(appDataPath, "Google", "Chrome", "User Data", "Local State");

            if (!File.Exists(localStateFilePath))
            {
                return profiles;
            }

            // read local state file
            string jsonContent = File.ReadAllText(localStateFilePath);

            using (JsonDocument doc = JsonDocument.Parse(jsonContent))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("profile", out var profile) &&
                    profile.TryGetProperty("info_cache", out var infoCache))
                {
                    // enumerate every profile
                    foreach (JsonProperty property in infoCache.EnumerateObject())
                    {
                        // get profile directory
                        string directoryName = property.Name;

                        if (property.Value.TryGetProperty("name", out var nameElement))
                        {
                            // get profile name
                            string? displayName = nameElement.GetString();

                            if (!string.IsNullOrEmpty(displayName))
                            {
                                if (property.Value.TryGetProperty("gaia_picture_file_name", out var iconRelativePathElement))
                                {
                                    // get profile icon
                                    string? iconPath = iconRelativePathElement.GetString();

                                    if (iconPath != null) iconPath = Path.Combine(appDataPath, "Google", "Chrome", "User Data", directoryName, iconPath);

                                    // add profile
                                    profiles.Add(
                                        new() {
                                            Name = displayName,
                                            Folder = directoryName,
                                            ProfilePicturePath = iconPath ?? defaultProfilePicture
                                        }
                                    );
                                }
                            }
                        }
                    }
                }
            }

            return profiles;
        }

        /// <summary>
        ///     Opens a URL in a Chrome profile specified by its display name.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        /// <param name="profileName">The display name of the profile.</param>
        /// <param name="newWindow">Whether to open a new window or a new tab.</param>
        public static void OpenUrlInChromeProfile(string url, string profileName, bool? newWindow = false)
        {
            // check if chrome installed
            string chromeExe = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";

            if (!File.Exists(chromeExe)) return;
            
            // get profile
            Profile? profile = GetChromeProfiles().FirstOrDefault(_profile => _profile.Name == profileName);

            if (profile == null) return;


            // start chrome using start as it's the only way I found to reliably gain focus
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"/c start \"\" \"{chromeExe}\" {((newWindow == true) ? "--new-window " : "")}--profile-directory=\"{profile.Folder}\" \"{url}\"",
                UseShellExecute = true
            };

            Process? chromeProcess = Process.Start(startInfo);
        }


        /// <summary>
        ///     Opens a URL in a Chrome profile specified by its display name.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        /// <param name="profileName">The display name of the profile.</param>
        /// <param name="newWindow">Whether to open a new window or a new tab.</param>
        public void OpenUrlInChromeProfile(string url, string profileName, bool newWindow = false)
        {
            // check if chrome installed
            string chromeExe = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";

            if (!File.Exists(chromeExe)) return;

            // get profile
            Profile? profile = GetChromeProfiles().FirstOrDefault(_profile => _profile.Name == profileName);

            if (profile == null) return;

            // create history element from url
            Google.Suggestion historyItem;

            if (url.StartsWith("https://www.google.com/search?q=", StringComparison.CurrentCulture))
                historyItem = new()
                {
                    Query = HttpUtility.UrlDecode(url.Substring("https://www.google.com/search?q=".Length)),
                    Type = SuggestionType.History,
                    IsURL = false
                };
            else
                historyItem = new()
                {
                    Query = url,
                    Type = SuggestionType.History,
                    IsURL = true
                };

            // add to history
            if (!History.Any(item => item.Query == historyItem.Query))
            {
                History.Add(historyItem);
                SaveHistory();
            }

            // start chrome args
            var startInfo = new ProcessStartInfo
            {
                FileName = chromeExe,
                WindowStyle = ProcessWindowStyle.Maximized,
                Arguments = $"--start-maximised {((newWindow == true) ? "--new-window " : "")}--profile-directory=\"{profile.Folder}\" \"{url}\"",
                UseShellExecute = true
            };

            // snapshot current chrome window titles
            List<TopLevelWindowInfo> chromeList = FindChromeWindows();

            // start chrome
            Process? chromeProcess = Process.Start(startInfo);

            // wait for chrome to open
            chromeProcess?.WaitForInputIdle(1000);


            // only get chrome processes whose title changed
            TopLevelWindowInfo[] chromeWindows = [];

            // wait until tab is opened (required for new windows)
            while (chromeWindows.Length < 1)
            {
                chromeWindows = FindChromeWindows()
                    .Where(proc => !chromeList.Any(prevProc => 
                        (prevProc.Hwnd == proc.Hwnd && prevProc.Title == proc.Title)
                    )).ToArray();
            }

            TopLevelWindowInfo? chromeWindow = chromeWindows.FirstOrDefault();

            // maximise & focus
            if (chromeWindow != null) BringChromeWindowToFront(chromeWindow.Hwnd);
        }


        /// <summary>
        ///     Opens a URL in a Chrome in the last used Chrome profile.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        public static void OpenUrlInChrome(string url, bool? newWindow = false)
        {
            string chromeExe = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";

            if (!File.Exists(chromeExe)) return;

            // start chrome using start as it's the only way I found to reliably gain focus
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"/c start \"\" \"{chromeExe}\" {((newWindow == true) ? "--new-window " : "")}\"{url}\"",
                UseShellExecute = true
            };

            Process? chromeProcess = Process.Start(startInfo);
        }

        // https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/how-to
        public static void SaveProfiles(List<Profile> profiles, string filePath)
        {
            string jsonString = JsonConvert.SerializeObject(profiles, Formatting.Indented);

            Debug.WriteLine($"Saving to: {filePath}");

            File.WriteAllText(filePath, jsonString);
        }

        public static List<Profile> LoadProfiles(string filePath)
        {
            var allProfiles = GetChromeProfiles();
            try
            {
                Debug.WriteLine($"Loading profiles from: {filePath}");

                string jsonString = File.ReadAllText(filePath);

                List<Profile> profiles = JsonConvert.DeserializeObject<List<Profile>>(jsonString) ?? [];

                // add any missing profiles
                profiles.AddRange(
                    allProfiles.Where(profile => 
                        !profiles.Select(knownProfile => knownProfile.Name)
                                 .Contains(profile.Name)
                    )
                );

                // return profiles
                return profiles;


            } catch (FileNotFoundException ex)
            {
                Debug.WriteLine($"Failed to load profiles: {ex}");
                // return defaults if none exist
                return allProfiles;
            }
        }


        public static void SaveHistory(List<Suggestion> history, string filePath)
        {
            string jsonString = JsonConvert.SerializeObject(history, Formatting.Indented);

            Debug.WriteLine($"Saving to: {filePath}");

            File.WriteAllText(filePath, jsonString);
        }
        public void SaveHistory()
        {
            string jsonString = JsonConvert.SerializeObject(History, Formatting.Indented);

            Debug.WriteLine($"Saving to: {HistoryConfigPath}");

            File.WriteAllText(HistoryConfigPath, jsonString);
        }

        public static List<Suggestion> LoadHistory(string filePath, int? maxLength = null)
        {
            try
            {
                Debug.WriteLine($"Loading history from: {filePath}");

                string jsonString = File.ReadAllText(filePath);

                List<Suggestion> history = JsonConvert.DeserializeObject<List<Suggestion>>(jsonString) ?? [];

                // remove duplicates
                history = history.Distinct().ToList();

                // remove first if out of range
                if (history.Count > maxLength)
                    history = history.Skip(history.Count - (maxLength ?? 0)).ToList();

                // return history
                return history;

            }
            catch (FileNotFoundException ex)
            {
                Debug.WriteLine($"Failed to load history: {ex}");
                // return none if none exist
                return [];
            }
        }
        public List<Suggestion> LoadHistory()
        {
            try
            {
                Debug.WriteLine($"Loading history from: {HistoryConfigPath}");

                string jsonString = File.ReadAllText(HistoryConfigPath);

                List<Suggestion> history = JsonConvert.DeserializeObject<List<Suggestion>>(jsonString) ?? [];

                // remove duplicates
                history = history.Distinct().ToList();

                // return history
                return history;

            }
            catch (FileNotFoundException ex)
            {
                Debug.WriteLine($"Failed to load history: {ex}");
                // return none if none exist
                return [];
            }
        }


        // P/Invoke declarations
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClass, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int SW_MAXIMIZE = 3;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        // Helper: enumerate visible top-level windows and return a list with info
        public class TopLevelWindowInfo
        {
            public IntPtr Hwnd { get; set; }
            public string Title { get; set; } = "";
            public string ClassName { get; set; } = "";
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = "";
        }

        public static List<TopLevelWindowInfo> EnumerateTopLevelWindows()
        {
            var list = new List<TopLevelWindowInfo>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true; // continue

                var sb = new StringBuilder(1024);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                var sc = new StringBuilder(256);
                GetClassName(hWnd, sc, sc.Capacity);
                string cls = sc.ToString();

                GetWindowThreadProcessId(hWnd, out uint pid);
                string procName = "";
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    procName = p.ProcessName;
                }
                catch
                {
                    // ignore
                }

                list.Add(new TopLevelWindowInfo
                {
                    Hwnd = hWnd,
                    Title = title,
                    ClassName = cls,
                    ProcessId = (int)pid,
                    ProcessName = procName
                });

                return true;
            }, IntPtr.Zero);

            return list;
        }

        // find candidate Chrome windows. Optionally filter by title substring (page title).
        public static List<TopLevelWindowInfo> FindChromeWindows(string? titleSubstring = null)
        {
            var all = EnumerateTopLevelWindows();
            var chromeWins = all.Where(w => string.Equals(w.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(w.ProcessName, "msedge", StringComparison.OrdinalIgnoreCase))
                                .ToList();

            if (!string.IsNullOrEmpty(titleSubstring))
            {
                // try to match title first (case-insensitive)
                var matching = chromeWins.Where(w => !string.IsNullOrEmpty(w.Title) &&
                                                     w.Title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
                                         .ToList();
                if (matching.Count != 0)
                    return matching;
            }

            // If none matched by title, return all chrome windows (z-order is not guaranteed by EnumWindows,
            // but earlier windows in returned list often are higher in z-order — test and adapt for your case).
            return chromeWins;
        }

        // shit sling until the window is focussed
        public static bool BringChromeWindowToFront(IntPtr hWnd, int timeoutMs = 2000)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                // maximise
                ShowWindow(hWnd, SW_MAXIMIZE); // SW_RESTORE

                // Make topmost then clear topmost
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

                // request focus via the accessibility framework (actually reliable :DDD)
                var automation = new CUIAutomation();
                IUIAutomationElement element = automation.ElementFromHandle(hWnd);
                

                if (element != null)
                {
                    // Try to set focus - this will throw if the automation element doesn't support focus
                    try
                    {
                        element.SetFocus();

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SetFocus failed: {ex.Message}");
                        // Continue — fallback may still have succeeded visually.
                    }
                }

                return true; // topmost trick usually produced a visual result
            }
            catch
            {
                return false;
            }
        }
    }
}