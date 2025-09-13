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
    public int SuggestionLength { get; }
    public bool ShowLoading { get; }
    public bool IncludeKnowlegdePanels { get; }
    public bool KnowlegdePanelImages { get; }

    public SettingsChangedEventArgs(bool openInNewBrowser, int historyLength, int suggestionLength, bool showLoading, bool knowlegePanels, bool knowlegeImages)
    {
        OpenInNewBrowser = openInNewBrowser;
        SuggestionLength = suggestionLength;
        HistoryLength = historyLength;
        ShowLoading = showLoading;
        IncludeKnowlegdePanels = knowlegePanels;
        KnowlegdePanelImages = knowlegeImages;
    }
}


// [https://github.com/tanchekwei/VisualStudioCodeForCommandPalette/blob/main/WorkspaceLauncherForVSCode/Classes/SettingsManager.cs]
public class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "ChromePal";

    public event EventHandler<SettingsChangedEventArgs>? OnSettingsSaved;

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";


    // open in new browser
    private readonly ToggleSetting _OpenInNewBrowser = new(
        key: Namespaced(nameof(OpenInNewBrowser)),
        label: "Open in new browser",
        description: "Open queries in a new browser window.",
        defaultValue: false
    );
    public bool OpenInNewBrowser => _OpenInNewBrowser.Value;


    // show loading indicator
    private readonly ToggleSetting _ShowLoading = new(
        key: Namespaced(nameof(ShowLoading)),
        label: "Show loading indicator",
        description: "Enable/Disable the blue continuous loading indicator.",
        defaultValue: true
    );
    public bool ShowLoading => _ShowLoading.Value;


    // show knowledge panels
    private readonly ToggleSetting _IncludeKnowledgePanels = new(
        key: Namespaced(nameof(IncludeKnowledgePanels)),
        label: "Include Knowledge Panel suggestions",
        description: "Enable/Disable Google Knowledge Panels that display short desciptions of relevant topics.",
        defaultValue: true
    );
    public bool IncludeKnowledgePanels => _IncludeKnowledgePanels.Value;

    // show knowledge panel images
    private readonly ToggleSetting _KnowledgePanelImages = new(
        key: Namespaced(nameof(KnowledgePanelImages)),
        label: "Include images in Knowledge Panel suggestions",
        description: "Enable/Disable image icons for Google Knowledge Panel suggestions.",
        defaultValue: false
    );
    public bool KnowledgePanelImages => _KnowledgePanelImages.Value;


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


    // page suggestion amount
    private readonly ChoiceSetSetting _SuggestionLength = new(
        key: Namespaced(nameof(SuggestionLength)),
        label: "Max suggestion count",
        description: "Set the max number of suggestions that can appear in search (not including exact search).",
        choices: [
            new ChoiceSetSetting.Choice("All",  "999"),
            new ChoiceSetSetting.Choice("20",  "20"),
            new ChoiceSetSetting.Choice("10",  "10"),
            new ChoiceSetSetting.Choice("5",   "5"),
            new ChoiceSetSetting.Choice("3",   "3"),
            new ChoiceSetSetting.Choice("None", "0"),
        ]
    );
    public int SuggestionLength
    {
        get
        {
            if (int.TryParse(_SuggestionLength.Value, out var result))
            {
                return result;
            }
            // default
            return 5;
        }
    }


    // history length
    private readonly ChoiceSetSetting _HistoryLength = new(
        key: Namespaced(nameof(HistoryLength)),
        label: "Max history length",
        description: "Set the max seach history length.",
        choices: [
            new ChoiceSetSetting.Choice("Infinite",  "99999"),
            new ChoiceSetSetting.Choice("50",  "50"),
            new ChoiceSetSetting.Choice("25",  "25"),
            new ChoiceSetSetting.Choice("10",  "10"),
            new ChoiceSetSetting.Choice("5",    "5"),
            new ChoiceSetSetting.Choice("None", "0"),
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

        Settings.Add(_SuggestionLength);

        Settings.Add(_IncludeKnowledgePanels);
        Settings.Add(_KnowledgePanelImages);
        
        Settings.Add(_HistoryLength);
        
        Settings.Add(_ShowLoading);
        
        Settings.Add(_OpenInNewBrowser);


        LoadSettings();

        Settings.SettingsChanged += (s, a) =>
        {
            OnSettingsSaved?.Invoke(this, new(OpenInNewBrowser, HistoryLength, SuggestionLength, ShowLoading, IncludeKnowledgePanels, KnowledgePanelImages));
            
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
        OnSettingsSaved?.Invoke(this, new(OpenInNewBrowser, HistoryLength, SuggestionLength, ShowLoading, IncludeKnowledgePanels, KnowledgePanelImages));
    }
}
