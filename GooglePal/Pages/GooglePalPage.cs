// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GooglePal.Pages;
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

public class CachedItem
{
    public required string URL { get; set; }
    public required bool IsWebsite {  get; set; }
}

internal sealed partial class GooglePalPage : DynamicListPage
{
    // suggestions
    public List<string> Suggestions = [];

    public bool typingState = true;

    // rate limiting
    public int RatelimitTimeMS = 300;
    public int OutOfDateLength = 3;

    private string previousSearchQuery = "";

    public List<CachedItem> CachedIsWebsite = new();


    // chrome profile
    public string ProfileConfigPath = Path.Combine(
        Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(),
        "profiles.json"
    );

    public List<Chrome.Profile> Profiles;
    public string DefaultProfile;

    // history
    public string HistoryConfigPath = Path.Combine(
        Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(),
        "history.json"
    );
    public int HistoryLength = 999;

    public int LevenshteinMaxDistance = 3;

    public List<string> History;

    // update UI when search bar changes
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged(Suggestions.Count + 1);

        // don't query if empty
        if (string.IsNullOrEmpty(SearchText.Trim())) return;

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
                requireNoChangeTimeMS: 0)
            );

    }

    // icon
    private static IconInfo SearchIcon = new IconInfo("\uE721");
    private static IconInfo VisitIcon = new IconInfo("\uE774");
    private static IconInfo HistoryIcon = new IconInfo("\uE81C");


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
        Name = "Search with Chrome";

        // load profiles
        Profiles = Chrome.LoadProfiles(ProfileConfigPath);

        DefaultProfile = Profiles.FirstOrDefault()?.Name ?? "Last opened";

        // load history
        History = Chrome.LoadHistory(HistoryConfigPath, HistoryLength);
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
        queries.AddRange(Suggestions.Select(CreateSearchListItem).Where(suggestion => suggestion.Title.Trim() != SearchText.Trim()));

        // show search options
        return queries.ToArray();
    }

    private ListItem CreateSearchListItem(string query)
    {
        bool isURL = IsValidDomainName(query);

        // Create context menu items for each Chrome profile
        var moreCommands = Profiles.Select(profile =>
            new CommandContextItem(
                name: isURL ? $"Visit ({profile.Name})" : $"Search ({profile.Name})",
                title: isURL ? $"Visit as: {profile.Name}" : $"Search as: {profile.Name}",
                action: () => {

                    // change default
                    DefaultProfile = profile.Name;

                    // move the default profile to the top
                    Profiles = Profiles
                        .OrderByDescending(_profile => _profile.Name == DefaultProfile)
                        .ToList();

                    // add to history
                    History.Add(query);

                    // remove first if out of range
                    if (History.Count > HistoryLength)
                        History = History.Skip(History.Count - HistoryLength).ToList();


                    Chrome.OpenUrlInChromeProfile(
                        url: isURL ? query : $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}",
                        profileName: profile.Name
                    );

                    // save history
                    Chrome.SaveHistory(History, HistoryConfigPath);

                    // save profiles
                    Chrome.SaveProfiles(Profiles, ProfileConfigPath);
                }
            )
            {
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Number1 + Profiles.IndexOf(profile)
                ),
                Icon = new IconInfo(profile.ProfilePicturePath)
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

        ListItem listItem = new ListItem(
            new AnonymousCommand(() => {

                // add to history
                History.Add(query);

                // remove first if out of range
                if (History.Count > HistoryLength)
                    History = History.Skip(History.Count - HistoryLength).ToList();


                Chrome.OpenUrlInChromeProfile(
                    url: isURL ? query : $"https://www.google.com/search?q={HttpUtility.UrlEncode(query)}",
                    profileName: DefaultProfile
                );

                // save history
                Chrome.SaveHistory(History, HistoryConfigPath);

            })
            {
                Name = isURL ? $"Visit ({DefaultProfile})" : $"Search ({DefaultProfile})",
                Icon = History.Contains(query) ? HistoryIcon : ( isURL ? VisitIcon : SearchIcon )
            }

        )
        { Title = $"{query}", Subtitle = isURL ? $"Visit as: {DefaultProfile}" : $"Search as: {DefaultProfile}", MoreCommands = moreCommands };


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
                ),
                Icon = new IconInfo(profile.ProfilePicturePath)
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
            new AnonymousCommand(() => Chrome.OpenUrlInChromeProfile(
                        url: "chrome://newtab",
                        profileName: DefaultProfile
                    ))
            { Name = $"Open Chrome ({DefaultProfile})", Icon = SearchIcon }

        )
        { Title = "Search Google or type a URL", Subtitle = $"Open as: {DefaultProfile}", MoreCommands = moreCommands };


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

        // update suggestions (not including values that are the search text)
        Suggestions = GetSuggestions(SearchText.Trim())
            .Where(suggestion => suggestion.Trim() != SearchText.Trim())
            .ToList();

        // add history
        Suggestions.InsertRange(
            index: 0,
            // add items that are valid and aren't currently in suggestions
            collection: History.Where(item =>
                (
                    ComputeDistanceLevenshtein(SearchText, item) <= LevenshteinMaxDistance ||
                    item.StartsWith(SearchText.Trim(), StringComparison.InvariantCultureIgnoreCase)
                ) &&
                !Suggestions.Contains(item)
            )
        );

        // update UI
        RaiseItemsChanged(Suggestions.Count + 1);
    }


    public void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
    {
        HistoryLength = eventArgs.HistoryLength;

        Debug.WriteLine($"History updated: {eventArgs.HistoryLength}");

        // truncate history if required
        if (History.Count > HistoryLength)
            History = History.Skip(History.Count - HistoryLength).ToList();

        // save
        Chrome.SaveHistory(History, HistoryConfigPath);
    }


    bool IsInHistory(string query)
    {
        return History.Where(item => ComputeDistanceLevenshtein(SearchText, item) <= LevenshteinMaxDistance).Any();
    }


    // https://www.dotnetperls.com/levenshtein
    static int ComputeDistanceLevenshtein(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        // Verify arguments.
        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        // Initialize arrays.
        for (int i = 0; i <= n; d[i, 0] = i++)
        {
        }

        for (int j = 0; j <= m; d[0, j] = j++)
        {
        }

        // Begin looping.
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                // Compute cost.
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
            }
        }
        // Return cost.
        return d[n, m];
    }


    // [https://www.aspsnippets.com/questions/186559/Check-Domain-Name-Exist-or-not-using-C-and-VBNet-in-ASPNet/]
    public bool IsValidDomainName(string domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName)) return false;
        if (domainName.Contains(' ')) return false;
        if (!domainName.Contains('.')) return false;

        if (Uri.IsWellFormedUriString(domainName, UriKind.RelativeOrAbsolute) == false) return false;
        if (Uri.CheckHostName(domainName) == UriHostNameType.Unknown) return false;

        // [https://stackoverflow.com/a/26987741]
        if (!Regex.IsMatch(domainName, "^(((?!-))(xn--|_)?[a-z0-9-]{0,61}[a-z0-9]{1,1}\\.)*(xn--)?([a-z0-9][a-z0-9\\-]{0,60}|[a-z0-9-]{1,30}\\.[a-z]{2,})$")) return false;

        // if in cache
        CachedItem? cachedItem = CachedIsWebsite.Find(item => item.URL.Trim() == domainName.Trim());
        if (cachedItem != null) return cachedItem.IsWebsite;

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

        CachedIsWebsite.Add(new() { URL = domainName, IsWebsite = isDomainExist });

        return isDomainExist;
    }


}