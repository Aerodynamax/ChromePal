using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GooglePal
{
    public class Chrome
    {
        public class Profile
        {
            public required string Name { get; set; }
            public required string Folder { get; set; }
            public required string ProfilePicturePath { get; set; }
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
        /// <param name="profileName">The display name of the profile.</param>
        /// <param name="url">The URL to open.</param>
        public static void OpenUrlInChromeProfile(string url, string profileName, bool? newWindow = false)
        {
            // get profile

            Profile? _profile = GetChromeProfiles().FirstOrDefault(profile => profile.Name == profileName);

            if (_profile == null) return;


            string chromeExe = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";

            if (!File.Exists(chromeExe)) return;

            // start chrome [ https://stackoverflow.com/a/70151095 ]
            var startInfo = new ProcessStartInfo
            {
                FileName = chromeExe,
                Arguments = $"{((newWindow == true) ? "--new-window" : "")} --profile-directory=\"{_profile.Folder}\" \"{url}\"",
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }

        /// <summary>
        ///     Opens a URL in a Chrome in the last used Chrome profile.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        public static void OpenUrlInChrome(string url, bool? newWindow = false)
        {
            string chromeExe = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";

            if (!File.Exists(chromeExe)) return;

            // start chrome [ https://stackoverflow.com/a/70151095 ]
            var startInfo = new ProcessStartInfo
            {
                FileName = chromeExe,
                Arguments = $"{((newWindow == true) ? "--new-window" : "")} \"{url}\"",
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }

        // https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/how-to
        public static void SaveProfiles(List<Profile> profiles, string filePath)
        {
            JsonSerializerOptions JSONOptions = new JsonSerializerOptions { WriteIndented = true };

            string jsonString = JsonSerializer.Serialize(profiles, JSONOptions);

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

                List<Profile> profiles = JsonSerializer.Deserialize<List<Profile>>(jsonString) ?? [];

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


        public static void SaveHistory(List<string> history, string filePath)
        {
            JsonSerializerOptions JSONOptions = new JsonSerializerOptions { WriteIndented = true };

            string jsonString = JsonSerializer.Serialize(history, JSONOptions);

            Debug.WriteLine($"Saving to: {filePath}");

            File.WriteAllText(filePath, jsonString);
        }

        public static List<string> LoadHistory(string filePath, int maxLength)
        {
            try
            {
                Debug.WriteLine($"Loading history from: {filePath}");

                string jsonString = File.ReadAllText(filePath);

                List<string> history = JsonSerializer.Deserialize<List<string>>(jsonString) ?? [];

                // remove duplicates

                history = history.Distinct().ToList();

                // remove first if out of range
                if (history.Count > maxLength)
                    history = history.Skip(history.Count - maxLength).ToList();

                // return profiles
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
