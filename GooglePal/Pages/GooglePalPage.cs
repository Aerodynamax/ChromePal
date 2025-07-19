// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.UI.Xaml.Controls;
using System.Web;

namespace GooglePal;

internal sealed partial class GooglePalPage : DynamicListPage
{

    // required for getting user input
    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    public GooglePalPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\GoogleLogo.png");
        Title = "Search Google";
        Name = "Search";
    }

    public override IListItem[] GetItems()
    {
        // show generic text
        if (string.IsNullOrEmpty(SearchText))
        {
            return [
                new ListItem(new NoOpCommand()) { Title = "Start typing ..." },
            ];
        }

        // show search option
        return [
            new ListItem(
                new OpenUrlCommand($"https://www.google.com/search?q={HttpUtility.UrlEncode(SearchText)}") { Name = "Search" }

            ) { Title = $"{SearchText}", Subtitle = "Search" }
        ];
    }
}
