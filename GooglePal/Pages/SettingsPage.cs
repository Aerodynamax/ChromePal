using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GooglePal.Pages;

public class SettingsChangedEventArgs : EventArgs
{
    public bool OpenInNewBrowser { get; }
    public int HistoryLength { get; }

    public SettingsChangedEventArgs(bool openInNewBrowser, int historyLength)
    {
        OpenInNewBrowser = openInNewBrowser;
        HistoryLength = historyLength;
    }
}


// [https://github.com/tanchekwei/VisualStudioCodeForCommandPalette/blob/main/WorkspaceLauncherForVSCode/Classes/SettingsManager.cs]
public class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "ChromePal";

    public event EventHandler<SettingsChangedEventArgs>? OnSettingsSaved;

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private readonly ToggleSetting _OpenInNewBrowser = new(
        key: nameof(OpenInNewBrowser),
        label: "Open in new browser",
        description: "Open queries in new browser.",
        defaultValue: false
    );
    public bool OpenInNewBrowser => _OpenInNewBrowser.Value;


    // TODO: add static profile options

    private readonly ToggleSetting _ProfilesStatic = new(
        key: nameof(ProfilesStatic),
        label: "Static profile list",
        description: "Keep profile list static.",
        defaultValue: false
    );
    public bool ProfilesStatic => _ProfilesStatic.Value;

    private readonly ChoiceSetSetting _ProfileList = new(
        key: Namespaced(nameof(ProfileList)),
        label: "Profile List",
        description: "Set the list of profiles.",
        choices: [
            new ChoiceSetSetting.Choice("TODO", "0")
            ]
    );
    public int ProfileList
    {
        get
        {
            if (int.TryParse(_ProfileList.Value, out var result))
            {
                return result;
            }
            // default
            return 25;
        }
    }



    private readonly ChoiceSetSetting _HistoryLength = new(
        key: Namespaced(nameof(HistoryLength)),
        label: "History Length",
        description: "Set the max seach history length.",
        choices: [
            new ChoiceSetSetting.Choice("None", "0"),
            new ChoiceSetSetting.Choice("5",    "5"),
            new ChoiceSetSetting.Choice("10",  "10"),
            new ChoiceSetSetting.Choice("25",  "25"),
            new ChoiceSetSetting.Choice("50",  "50"),
            new ChoiceSetSetting.Choice("Infinite",  "99999"),
        ]
    );
    public int HistoryLength
    {
        get
        {
            if (int.TryParse(_HistoryLength.Value, out var result))
            {
                return result;
            }
            // default
            return 25;
        }
    }


    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_OpenInNewBrowser);
        Settings.Add(_HistoryLength);

        LoadSettings();

        Settings.SettingsChanged += (s, a) =>
        {
            OnSettingsSaved?.Invoke(this, new(OpenInNewBrowser, HistoryLength));
            
            SaveSettings();
        };
    }

    public static readonly string settingsConfigDirectory = Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();

    internal static string SettingsJsonPath()
    {
        Directory.CreateDirectory(settingsConfigDirectory);
        return Path.Combine(settingsConfigDirectory, "settings.json");
    }

    // send history update (used in initialisation)
    public void SendSavedUpdateEvent()
    {
        OnSettingsSaved?.Invoke(this, new(OpenInNewBrowser, HistoryLength));
    }
}
