// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GooglePal.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GooglePal;

public partial class GooglePalCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly SettingsManager _settingsManager = new();

    private GooglePalPage SearchPage = new GooglePalPage();

    public GooglePalCommandsProvider()
    {
        // [https://github.com/jiripolasek/ColorsExtension/blob/master/src/JPSoftworks.ColorsExtension/ColorsExtensionCommandsProvider.cs]
        Settings = _settingsManager.Settings;

        // attach to setting changes
        _settingsManager.OnSettingsSaved += SearchPage.OnSettingsChanged;
        // fire event without changing settings
        _settingsManager.SendSavedUpdateEvent();

        DisplayName = "ChromePal";
        Icon = IconHelpers.FromRelativePath("Assets\\GoogleLogo.png");
        _commands = [
            new CommandItem(SearchPage),
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
