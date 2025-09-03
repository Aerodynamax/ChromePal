using GooglePal.Pages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace GooglePal
{
    public class Chrome : Google
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

        private int LevenshteinMaxDistance = 3;

        public List<Google.Suggestion> History = [];

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
        public static void SaveProfiles(List<Chrome.Profile> profiles, string filePath)
        {
            string jsonString = JsonConvert.SerializeObject(profiles, Formatting.Indented);

            Debug.WriteLine($"Saving to: {filePath}");

            File.WriteAllText(filePath, jsonString);
        }

        public static List<Chrome.Profile> LoadProfiles(string filePath)
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


        public static void SaveHistory(List<Google.Suggestion> history, string filePath)
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

        public static List<Google.Suggestion> LoadHistory(string filePath, int? maxLength = null)
        {
            try
            {
                Debug.WriteLine($"Loading history from: {filePath}");

                string jsonString = File.ReadAllText(filePath);

                List<Google.Suggestion> history = JsonConvert.DeserializeObject<List<Google.Suggestion>>(jsonString) ?? [];

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

        public List<Google.Suggestion> LoadHistory()
        {
            try
            {
                Debug.WriteLine($"Loading history from: {HistoryConfigPath}");

                string jsonString = File.ReadAllText(HistoryConfigPath);

                List<Google.Suggestion> history = JsonConvert.DeserializeObject<List<Google.Suggestion>>(jsonString) ?? [];

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

    }
}