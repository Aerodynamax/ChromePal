using GooglePal.Pages;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection.Metadata.Ecma335;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using static GooglePal.Components.ForceShowWindow;

namespace GooglePal
{
    internal class Chrome : Google
    {
        public class Profile
        {
            public required string Name { get; set; }
            public required string Folder { get; set; }
            public required string ProfilePicturePath { get; set; }
            public required string TaskbarIconPath { get; set; }
        }

        // profiles
        private string ProfileConfigPath = default!;

        public List<Profile> Profiles = [];


        // history
        private string HistoryConfigPath = default!;
        private int HistoryLength = -1;

        public List<Suggestion> History = [];


        // json
        JsonSerializerSettings JsonOptions = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.None,
            DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate
        };


        // suggestions
        bool IncludeKnowledgePanels = true;
        bool KnowledgePanelImages;
        
        public void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
        {
            //  // invalidate cache if required
            //  if (eventArgs.IncludeKnowlegdePanels != IncludeKnowledgePanels)
            //      CachedQueries.Clear();

            // settings
            IncludeKnowledgePanels = eventArgs.IncludeKnowlegdePanels;
            KnowledgePanelImages = eventArgs.KnowlegdePanelImages;

            // configs
            ProfileConfigPath = Path.Combine(eventArgs.BaseConfigPath, "profiles.json");
            HistoryConfigPath = Path.Combine(eventArgs.BaseConfigPath, "history.json");

            // load profiles
            Profiles = LoadProfiles();


            // load history if first time
            if (HistoryLength == -1) History = LoadHistory(eventArgs.HistoryLength);

            // update history length
            HistoryLength = eventArgs.HistoryLength;

            // save
            SaveHistory();
        }

        public override async Task<List<Suggestion>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            List<Suggestion> suggestions = await base.GetSuggestionsAsync(
                query: query,
                cancellationToken: cancellationToken
            );

            // process suggestions
            foreach (Suggestion suggestion in suggestions)
                {
                    // change type to history if suggestion in history
                    if (History.Any(item => item.Query == suggestion.Query))
                        suggestion.Type = SuggestionType.History;

                // remove knowledge panels if not required
                // (since the request still needs to happen for definitions, this doesn't needlessly send requests)
                if (!IncludeKnowledgePanels && suggestion.Type == SuggestionType.KnowledgePanel)
                    suggestion.Type = SuggestionType.Query;

                // remove icons from knowledge panels if not required
                if (!KnowledgePanelImages && suggestion.Type == SuggestionType.KnowledgePanel)
                        suggestion.Icon = null;
            }

            return suggestions;
        }


        /// <summary>
        ///     Gets a list of Chrome's profile names and their respective directory names.
        /// </summary>
        /// <returns>A List of Chrome.Profile objects with the Name and Folder of each profile.</returns>
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

                            if (string.IsNullOrEmpty(displayName))
                                continue;

                            if (property.Value.TryGetProperty("gaia_picture_file_name", out var iconRelativePathElement))
                            {
                                // get profile icon
                                string? iconPath = iconRelativePathElement.GetString();

                                if (iconPath == null)
                                    continue;

                                iconPath = Path.Combine(appDataPath, "Google", "Chrome", "User Data", directoryName, iconPath);

                                string taskbarIconPath = Path.Combine(appDataPath, "Google", "Chrome", "User Data", directoryName, "Google Profile.ico");

                                // add profile
                                profiles.Add(
                                    new()
                                    {
                                        Name = displayName,
                                        Folder = directoryName,
                                        ProfilePicturePath = iconPath ?? defaultProfilePicture,
                                        TaskbarIconPath = taskbarIconPath
                                    }
                                );
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
        /// <param name="addToHistory">Whether to add query to the ChromePal search history (if history enabled).</param>
        public async void OpenUrlInChromeProfile(string url, string profileName, bool newWindow = false, bool addToHistory = true, bool shuffleProfiles = true)
        {
            // invalidate cache as suggestions may change with browsing
            //CachedQueries.Clear();

            // check if chrome installed
            string chromeExe = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";

            if (!File.Exists(chromeExe)) return;

            // get profile
            Profile? profile = Profiles.FirstOrDefault(_profile => _profile.Name == profileName);

            if (profile == null) return;

            // update history and profiles in background
            _ = Task.Run(() =>
            {
                // update profiles
                if (shuffleProfiles)
                {
                    // move the latest profile to the top
                    Profiles = Profiles
                        .OrderByDescending(_profile => _profile.Name == profileName)
                        .ToList();

                    SaveProfiles();
                }

                // add to history if not present & not new tab
                if (addToHistory)
                {
                    // create history element from url
                    Suggestion historyItem;

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
                            Query = url
                                    .Replace("http://", "")
                                    .Replace("https://", ""),
                            Type = SuggestionType.History,
                            IsURL = true
                        };

                    // bump to end of list
                    RemoveFromHistory(historyItem.Query);

                    History.Add(historyItem);
                    SaveHistory();
                }
            });
            
            
            // get schema if port in URL

            int? port = UriGetPort(url);
            // if there is a port and no schema
            if (port != null && !UriHasScheme(url))
            {
                bool isHTTPS = await SupportsTlsAsync(
                    host: url.Split($":{port}").FirstOrDefault() ?? "",
                    port: port ?? 80
                );

                // add correct schema to url (Chrome doesn't open urls with ports if they don't have schema)
                url = ( isHTTPS ? "https://" : "http://" ) + url;
            }


            // start chrome args
            var startInfo = new ProcessStartInfo
            {
                FileName = chromeExe,
                WindowStyle = ProcessWindowStyle.Normal,
                Arguments = $"--start-maximised --profile-directory=\"{profile.Folder}\" {((newWindow == true) ? "--new-window" : "--new-tab")} {(url == "chrome://newtab" ? "" : $"\"{url}\"")}",
                UseShellExecute = true
            };

            // snapshot current chrome window titles
            List<TopLevelWindowInfo> chromeList = FindProcessWindows(processName: "chrome");

            // start chrome
            Process? chromeProcess = Process.Start(startInfo);

            // wait for chrome to open
            chromeProcess?.WaitForInputIdle(1000);


            // only get chrome processes whose title changed
            TopLevelWindowInfo? chromeWindow = null;

            // timeout
            Stopwatch timeout = new();
            timeout.Start();

            // wait until tab is opened (required for new windows)
            while (chromeWindow == null && timeout.ElapsedMilliseconds < 2500)
            {
                chromeWindow = FindProcessWindows(processName: "chrome")
                    .FirstOrDefault(proc => 
                        !chromeList.Any(
                            prevProc => (prevProc.Hwnd == proc.Hwnd && prevProc.Title == proc.Title)
                        )
                    );

                // let the computer actually do its thing
                Thread.Sleep(10);
            }

            timeout.Stop();

            // maximise & focus
            if (chromeWindow != null)
                ForceFocusWindow(
                    hWnd: chromeWindow.Hwnd
                );

        }


        /// <summary>
        ///     Save the current ordering of chrome profiles to disk.
        /// </summary>
        public void SaveProfiles()
        {
            string jsonString = JsonConvert.SerializeObject(Profiles, JsonOptions);

            Debug.WriteLine($"Saving to: {ProfileConfigPath}");

            File.WriteAllText(ProfileConfigPath, jsonString);
        }

        /// <summary>
        ///     Load the current ordering of chrome profiles from disk.
        /// </summary>
        public List<Profile> LoadProfiles()
        {
            var allProfiles = GetChromeProfiles();
            try
            {
                Debug.WriteLine($"Loading profiles from: {ProfileConfigPath}");

                string jsonString = File.ReadAllText(ProfileConfigPath);

                List<Profile> profiles = JsonConvert.DeserializeObject<List<Profile>>(jsonString, JsonOptions) ?? [];

                // add any missing profiles
                profiles.AddRange(
                    allProfiles.Where(profile =>
                        !profiles.Select(knownProfile => knownProfile.Name)
                                 .Contains(profile.Name)
                    )
                );

                // return profiles
                return profiles;


            }
            catch (FileNotFoundException ex)
            {
                Debug.WriteLine($"Failed to load profiles: {ex}");
                // return defaults if none exist
                return allProfiles;
            }
        }

        /// <summary>
        ///     Remove all instances of a given query from ChromePal's history.
        /// </summary>
        /// <param name="query">The exact string to remove.</param>
        public void RemoveFromHistory(string query)
        {
            History.RemoveAll(historyItem => historyItem.Query == query);

            // cache is no longer valid (pointers somthin somthin)
            CachedQueries.Clear();

            SaveHistory();
        }

        /// <summary>
        ///     Save the current (ordered) ChromePal history to disk.
        /// </summary>
        public void SaveHistory()
        {
            string jsonString = JsonConvert.SerializeObject(value: History.Take(HistoryLength), settings: JsonOptions);

            Debug.WriteLine($"Saving to: {HistoryConfigPath}");

            File.WriteAllText(HistoryConfigPath, jsonString);
        }


        public List<Suggestion> LoadHistory() => LoadHistory(HistoryLength);

        /// <summary>
        ///     Load the current (ordered) ChromePal search history from disk.
        /// </summary>
        /// <param name="maxLength">The maximum number of history items to load.</param>
        /// <returns>List<Suggestion> of history items with their properties</returns>
        public List<Suggestion> LoadHistory(int maxLength)
        {
            try
            {
                Debug.WriteLine($"Loading history from: {HistoryConfigPath}");

                string jsonString = File.ReadAllText(HistoryConfigPath);

                List<Suggestion> history = JsonConvert.DeserializeObject<List<Suggestion>>(jsonString, settings: JsonOptions) ?? [];

                // remove duplicates (and only take history length)
                history = history
                    .DistinctBy(item => item.Query)
                    .Take(maxLength)
                    .ToList();

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


        /// <summary>
        ///     Check if URL is https or http.
        /// </summary>
        /// <param name="host">the domain name being checked</param>
        /// <param name="port">the port of the website being visited</param>
        /// <param name="timeoutMs">milliseconds to wait before assuming page is down</param>
        /// <returns></returns>
        public static async Task<bool> SupportsTlsAsync(string host, int port, int timeoutMs = 700)
        {
            try
            {
                using var client = new TcpClient();
                
                // start the connection attempt
                var connectTask = client.ConnectAsync(host, port);

                // attempt to connect with timeout
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask)
                {
                    // attempt SSL Handshake
                    using var sslStream = new SslStream(
                        innerStream: client.GetStream(),
                        leaveInnerStreamOpen: false,
                        // return true to ignore errors (self-signed)
                        userCertificateValidationCallback: (sender, cert, chain, errors) => true
                    );

                    var authTask = sslStream.AuthenticateAsClientAsync(host);

                    // Apply timeout to the handshake as well
                    if (await Task.WhenAny(authTask, Task.Delay(timeoutMs)) == authTask)
                    {
                        await authTask; // Propagate exceptions if failed
                        return true;
                    }
                }
            }
            // if there are any errors in the process, assume http
            catch { }

            return false;
        }

        /// <summary>
        ///     Checks if a provided URI has specified a port
        /// </summary>
        /// <param name="uriString"></param>
        /// <returns></returns>
        public static int? UriGetPort(string uriString)
        {
            Regex regex = new Regex(@":(?<port>\d+)");
            Match match = regex.Match(uriString);

            if (match.Success)
            {
                string portString = match.Groups["port"].Value;
                if (int.TryParse(portString, out int portNumber))
                {
                    return portNumber;
                }
            }

            // No port specified or parsing failed, so it doesn't have a *specified* non-default port
            return null;
        }

        /// <summary>
        ///     Check if URI string contains scheme (e.g. "http://", "https://")
        /// </summary>
        /// <param name="uriString"></param>
        /// <returns></returns>
        public static bool UriHasScheme(string uriString)
        {
            if (string.IsNullOrEmpty(uriString))
            {
                return false;
            }

            // detects a schema regardless of overall URI validity
            Regex schemaRegex = new Regex(@"^[a-zA-Z]+:/?/?");
            return schemaRegex.IsMatch(uriString);
        }
    }
}