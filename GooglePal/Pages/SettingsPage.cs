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
    public int HistoryLength { get; }

    public SettingsChangedEventArgs(SettingsManager.HistoryLengths historyLength)
    {
        if (historyLength == SettingsManager.HistoryLengths.None) HistoryLength = 0;
        if (historyLength == SettingsManager.HistoryLengths.Five) HistoryLength = 5;
        if (historyLength == SettingsManager.HistoryLengths.Ten) HistoryLength = 10;
        if (historyLength == SettingsManager.HistoryLengths.TwentyFive) HistoryLength = 25;
        if (historyLength == SettingsManager.HistoryLengths.Fifty) HistoryLength = 50;
    }
}


// [https://github.com/tanchekwei/VisualStudioCodeForCommandPalette/blob/main/WorkspaceLauncherForVSCode/Classes/SettingsManager.cs]
public class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "ChromePal";

    public event EventHandler<SettingsChangedEventArgs>? OnSettingsSaved;

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    public enum HistoryLengths
    {
        None,
        Five,
        Ten,
        TwentyFive,
        Fifty
    }

    private static readonly List<ChoiceSetSetting.Choice> _historyLengthChoices =
    [
        new ChoiceSetSetting.Choice("None", nameof(HistoryLengths.None)),
        new ChoiceSetSetting.Choice("5", nameof(HistoryLengths.Five)),
        new ChoiceSetSetting.Choice("10", nameof(HistoryLengths.Ten)),
        new ChoiceSetSetting.Choice("25", nameof(HistoryLengths.TwentyFive)),
        new ChoiceSetSetting.Choice("50", nameof(HistoryLengths.Fifty)),
    ];

    private readonly ChoiceSetSetting _historyLength = new(
        Namespaced(nameof(HistoryLength)),
        "History Length",
        "Configure the seach history length.",
        _historyLengthChoices);

    public HistoryLengths HistoryLength
    {
        get
        {
            if (Enum.TryParse<HistoryLengths>(_historyLength.Value, out var result))
            {
                return result;
            }
            return HistoryLengths.TwentyFive;
        }
    }


    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_historyLength);

        LoadSettings();

        Settings.SettingsChanged += (s, a) =>
        {
            OnSettingsSaved?.Invoke(this, new(HistoryLength));
            
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
        OnSettingsSaved?.Invoke(this, new(HistoryLength));
    }
}
