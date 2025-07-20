// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using Windows.System;
using Windows.System.Profile;
using static GooglePal.Chrome;

namespace GooglePal;



internal sealed partial class GooglePalPage : DynamicListPage
{
    // suggestions
    public List<string> Suggestions = [];

    // rate limiting
    public int RatelimitTimeMS = 300;
    public int OutOfDateLength = 3;

    private string previousSearchQuery = "";


    // chrome profile
    public List<Chrome.Profile> Profiles = Chrome.GetChromeProfiles();
    public string DefaultProfile;

    // required for getting user input                                             +1 to include actual search query 🢆
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged(Suggestions.Count + 1);

        // update suggestions if very out of date
        if (SearchText.Length > previousSearchQuery.Length + OutOfDateLength || SearchText.Length < previousSearchQuery.Length - OutOfDateLength)
            Task.Run(async () => await UpdateSuggestions(
                previousSearchText: SearchText,
                requireNoChangeTimeMS: 0)
            );

        // update suggestions thread when user stops typing
        else
            Task.Run(async () => await UpdateSuggestions(
                previousSearchText: SearchText,
                requireNoChangeTimeMS: RatelimitTimeMS)
            );

    }

    // icon
    private static IconInfo SearchIcon = new IconInfo("\uE721");


    // get search suggestions as a string array
    [RequiresUnreferencedCode("Calls System.Xml.Serialization.XmlSerializer.XmlSerializer(Type)")]
    public static List<string> GetSuggestions(string query)
    {
        HttpClient client = new();

        string url = $"https://suggestqueries.google.com/complete/search?output=toolbar&gl=au&hl=en&q={HttpUtility.UrlEncode(query)}";

        HttpResponseMessage response = client.GetAsync(url).Result;

        string xml = "";

        if (response.IsSuccessStatusCode)
        {
            xml = response.Content.ReadAsStringAsync().Result;
        }

        // deserialise
        XDocument result = XDocument.Parse(xml);

        List<string> suggestions = [];

        foreach (XElement element in result.Descendants("suggestion"))
        {
            string? t = element.Attribute("data")?.Value;

            if (!string.IsNullOrWhiteSpace(t)) suggestions.Add(t);
        }

        return suggestions;
    }

    public GooglePalPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\GoogleLogo.png");
        Title = "Search Google";
        Name = "Search";

        DefaultProfile = Profiles.FirstOrDefault()?.Name ?? "";
    }

    public override IListItem[] GetItems()
    {
        // show generic text if no input available
        if (string.IsNullOrEmpty(SearchText))
        {
            return [
                new ListItem(new NoOpCommand()) { Title = "Start typing ..." },
            ];
        }

        // Create context menu commands for each Chrome profile
        var moreCommands = Profiles.Select(profile =>
            new AnonymousCommand(() => Chrome.OpenUrlInChromeProfile(
                url: $"https://www.google.com/search?q={HttpUtility.UrlEncode(SearchText)}",
                profileName: profile.Name
            ))
            {
                Name = $"Search as: {profile.Name}",
                Icon = SearchIcon
            } as ICommand
        ).ToArray();


        // format suggestions into command palette list items
        List<ListItem> queries = [

            // always show raw search text option
            CreateSearchListItem(SearchText)
        ];


        // convert suggestions into list items and add to the queries list
        queries.AddRange(Suggestions.Select(CreateSearchListItem));

        // show search options
        return queries.ToArray();
    }

    private ListItem CreateSearchListItem(string query)
    {
        // Create context menu items for each Chrome profile
        var moreCommands = Profiles.Select(profile =>
            new CommandContextItem(
                name: $"Search as: {profile.Name}",
                title: $"Search as: {profile.Name}",
                action: () => {

                    // change default
                    DefaultProfile = profile.Name;

                    // move the default profile to the top
                    Profiles = Profiles
                        .OrderByDescending(_profile => _profile.Name == DefaultProfile)
                        .ToList();

                    Chrome.OpenUrlInChromeProfile(
                        url: $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}",
                        profileName: profile.Name
                    );
                }
            )
            { RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Number1 + Profiles.IndexOf(profile)
                )
            }
        ).ToArray();

        // add Open Google button
        moreCommands = moreCommands.Prepend(
            new CommandContextItem(
                name: $"Open Google (as {DefaultProfile})",
                title: "Open Google",
                action: () =>
                {
                    Chrome.OpenUrlInChromeProfile(
                        url: $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}",
                        profileName: DefaultProfile
                    );
                }
            )
            {
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Enter
                )
            }
        ).ToArray();

        // Create the main list item
        var listItem = new ListItem(
                new AnonymousCommand(() => Chrome.OpenUrlInChromeProfile(
                    url: $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}",
                    profileName: DefaultProfile)
                )
                { Name = "Search", Icon = SearchIcon }

        ) { Title = $"{query}", Subtitle = $"Search as: {DefaultProfile}", MoreCommands = moreCommands };

        return listItem;
    }

    [RequiresUnreferencedCode("Calls GooglePal.GooglePalPage.GetSuggestions(String)")]
    public async Task UpdateSuggestions(string previousSearchText, int requireNoChangeTimeMS)
    {
        await Task.Delay(requireNoChangeTimeMS);

        // don't update suggestions if user has typed more
        if (SearchText != previousSearchText) return;

        previousSearchQuery = SearchText;

        Debug.WriteLine("Updating suggestions");

        // update suggestions
        Suggestions = GetSuggestions(SearchText);

        // update UI
        RaiseItemsChanged(Suggestions.Count + 1);
    }
}