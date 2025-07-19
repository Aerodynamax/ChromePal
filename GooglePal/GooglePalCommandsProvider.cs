// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GooglePal;

public partial class GooglePalCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public GooglePalCommandsProvider()
    {
        DisplayName = "Search Google";
        Icon = IconHelpers.FromRelativePath("Assets\\GoogleLogo.png");
        _commands = [
            new CommandItem(new GooglePalPage()),
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
