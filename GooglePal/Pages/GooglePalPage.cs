// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
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
    public string ProfileConfigPath = Path.Combine(
        Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(),
        "profiles.json"
    );

    public List<Chrome.Profile> Profiles;
    public string DefaultProfile;

    // update UI when search bar changes
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged(Suggestions.Count + 1);

        // don't query if empty
        if (string.IsNullOrEmpty(SearchText.Trim())) return;

        // update suggestions if very out of date
        if (SearchText.Length > previousSearchQuery.Length + OutOfDateLength || SearchText.Length < previousSearchQuery.Length - OutOfDateLength)
            Task.Run(async () => await UpdateSuggestions(
                previousSearchText: SearchText.Trim(),
                requireNoChangeTimeMS: 0)
            );

        // update suggestions thread when user stops typing
        else
            Task.Run(async () => await UpdateSuggestions(
                previousSearchText: SearchText.Trim(),
                requireNoChangeTimeMS: RatelimitTimeMS)
            );

    }

    // icon
    private static IconInfo SearchIcon = new IconInfo("\uE721");
    private static IconInfo VisitIcon = new IconInfo("\uE774");


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

        // load profiles
        Profiles = Chrome.LoadProfiles(ProfileConfigPath);

        DefaultProfile = Profiles.FirstOrDefault()?.Name ?? "Last opened";
    }

    public override IListItem[] GetItems()
    {
        // show generic text if no input available
        if (string.IsNullOrEmpty(SearchText.Trim()))
        {
            return [
                CreateDefaultSearchListItem()
            ];
        }

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
                name: $"Search ({profile.Name})",
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

                    // save profiles
                    Chrome.SaveProfiles(Profiles, ProfileConfigPath);
                }
            )
            {
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Number1 + Profiles.IndexOf(profile)
                )
            }
        ).ToArray();

        // remove first
        if (moreCommands.Length > 0) moreCommands = moreCommands.Skip( 1 ).ToArray();

        // make second profile ctrl+enter
        if (moreCommands.Length > 0)
            moreCommands[0].RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Enter
            );

        ListItem listItem;

        // Create the main list item
        if (IsValidDomainName(query))
        {
            // if url is valid,
            listItem = new ListItem(
                new AnonymousCommand(() => Chrome.OpenUrlInChrome(
                    url: query
                ))
                { Name = $"Visit ({DefaultProfile})", Icon = VisitIcon }
            )
            { Title = $"{query}", Subtitle = $"Search as: {DefaultProfile}", MoreCommands = moreCommands };

        }
        else
        {
            listItem = new ListItem(
                new AnonymousCommand(() => Chrome.OpenUrlInChrome(
                    url: $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}"
                ))
                { Name = $"Search ({DefaultProfile})", Icon = SearchIcon }

            )
            { Title = $"{query}", Subtitle = $"Search as: {DefaultProfile}", MoreCommands = moreCommands };

        }

        return listItem;
    }

    private ListItem CreateDefaultSearchListItem()
    {
        // Create context menu items for each Chrome profile
        var moreCommands = Profiles.Select(profile =>
            new CommandContextItem(
                name: $"Open Chrome ({profile.Name})",
                title: $"Open Chrome as: {profile.Name}",
                action: () => {

                    // change default
                    DefaultProfile = profile.Name;

                    // move the default profile to the top
                    Profiles = Profiles
                        .OrderByDescending(_profile => _profile.Name == DefaultProfile)
                        .ToList();

                    Chrome.OpenUrlInChromeProfile(
                        url: "chrome://newtab",
                        profileName: profile.Name
                    );

                    // save profiles
                    Chrome.SaveProfiles(Profiles, ProfileConfigPath);
                }
            )
            {
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Number1 + Profiles.IndexOf(profile)
                )
            }
        ).ToArray();

        // remove first
        if (moreCommands.Length > 0) moreCommands = moreCommands.Skip(1).ToArray();

        // make second profile ctrl+enter
        if (moreCommands.Length > 0)
            moreCommands[0].RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Enter
            );

        // Create the main list item

        ListItem listItem = new ListItem(
            new AnonymousCommand(() => Chrome.OpenUrlInChrome(
                url: "chrome://newtab"
            ))
            { Name = $"Open Chrome ({DefaultProfile})", Icon = SearchIcon }

        )
        { Title = "Search Google or type a URL", Subtitle = $"Open as: {DefaultProfile}", MoreCommands = moreCommands };


        return listItem;
    }

    // [https://www.aspsnippets.com/questions/186559/Check-Domain-Name-Exist-or-not-using-C-and-VBNet-in-ASPNet/]
    public static bool IsValidDomainName(string domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName) ) return false;
        if (domainName.Contains(' ')) return false;
        if (!domainName.Contains('.')) return false;

        if (Uri.IsWellFormedUriString(domainName, UriKind.RelativeOrAbsolute) == false) return false;
        if (Uri.CheckHostName(domainName) == UriHostNameType.Unknown) return false;

        // [https://stackoverflow.com/a/26987741]
        if (!Regex.IsMatch(domainName, "^(((?!-))(xn--|_)?[a-z0-9-]{0,61}[a-z0-9]{1,1}\\.)*(xn--)?([a-z0-9][a-z0-9\\-]{0,60}|[a-z0-9-]{1,30}\\.[a-z]{2,})$")) return false;

        Debug.WriteLine($"Checking if domain: {domainName}");

        bool isDomainExist = false;
        System.Net.IPHostEntry host;
        try
        {
            host = System.Net.Dns.GetHostEntry(domainName);
            if (host != null)
            {
                isDomainExist = true;
            }
        }
        catch (Exception ex)
        {
            if (ex.Message == "No such host is known")
            {
                isDomainExist = false;
            }
        }

        return isDomainExist;
    }

    [RequiresUnreferencedCode("Calls GooglePal.GooglePalPage.GetSuggestions(String)")]
    public async Task UpdateSuggestions(string previousSearchText, int requireNoChangeTimeMS)
    {
        await Task.Delay(requireNoChangeTimeMS);

        // don't update suggestions if user has typed more
        if (SearchText.Trim() != previousSearchText) return;

        previousSearchQuery = SearchText.Trim();

        Debug.WriteLine("Updating suggestions");

        // update suggestions (not including values that are the search text)
        Suggestions = GetSuggestions(SearchText.Trim())
            .Select(suggestion => suggestion.Trim())
            .Where(suggestion => suggestion != SearchText.Trim())
            .ToList();

        // update UI
        RaiseItemsChanged(Suggestions.Count + 1);
    }
}