using GooglePal.Components;
using GooglePal.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Windows.System;
using static GooglePal.Google;

namespace GooglePal;


internal sealed partial class GooglePalPage : DynamicListPage
{
    // suggestions
    public SuggestionList Suggestions;

    public Chrome chrome = new();

    // chrome profile
    public string ProfileConfigPath = Path.Combine(
        Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(),
        "profiles.json"
    );

    public List<Chrome.Profile> Profiles;
    public string DefaultProfile;

    public bool OpenInNewWindow;

    // history
    public int LevenshteinMaxDistance = 3;

    // defaults
    private ListItem DefaultItem = new();


    // update UI when search bar changes
    public async override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // change main search text
        Suggestions.Update(newSearch);

        // update suggestions
        if (!string.IsNullOrEmpty(newSearch)) UpdateSuggestions(newSearch);
    }

    public GooglePalPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\GoogleLogo.png");
        Title = "Search Google";
        Name = "Search with Chrome";

        PlaceholderText = "Search Google or type a URL";

        // load profiles
        Profiles = Chrome.LoadProfiles(ProfileConfigPath);

        DefaultProfile = Profiles.FirstOrDefault()?.Name ?? "Last opened";

        // create default page item
        DefaultItem = CreateDefaultSearchListItem();

        // create page list
        Suggestions = new(
            createListItem: CreateSearchListItem,
            updateUI: () => RaiseItemsChanged(totalItems: Suggestions?.Items.Count ?? 1),
            createDefaultPage: () => [
                DefaultItem,
                ..chrome.History.TakeLast(5).Reverse().Select(CreateSearchListItem)
            ]
        );
    }

    public override IListItem[] GetItems() => [.. Suggestions.Items];

    private ListItem CreateSearchListItem(Suggestion searchItem)
    {
        return new SearchItem(
            searchItem: searchItem,
            profilesOrdered: Profiles,
            searchCommand: (string profileName) =>
            {

                // change default
                DefaultProfile = profileName;

                // move the default profile to the top
                Profiles = Profiles
                    .OrderByDescending(_profile => _profile.Name == DefaultProfile)
                    .ToList();

                // open chrome & allow UI to hide
                Task.Run(() => chrome.OpenUrlInChromeProfile(
                    url: searchItem.IsURL ? searchItem.Query : $"https://www.google.com/search?q={HttpUtility.UrlEncode(searchItem.Query)}",
                    profileName: profileName,
                    newWindow: OpenInNewWindow
                ));

                // save profiles
                Chrome.SaveProfiles(Profiles, ProfileConfigPath);
            },
            otherSubcommands: (searchItem.Type != SuggestionType.History) ? [] : [
                new(
                    name: "Remove from history",
                    title: "Remove from history",
                    action: () => {
                        chrome.RemoveFromHistory(searchItem.Query);

                        UpdateSearchText(oldSearch: SearchText, newSearch: SearchText);
                    },
                    result: CommandResult.KeepOpen() // don't close Command Palette
                )
                {
                    IsCritical = true,
                    Icon = SearchItem.DeleteIcon,
                    RequestedShortcut = KeyChordHelpers.FromModifiers(
                        ctrl: true,
                        vkey: (int)VirtualKey.Delete
                    )
                }
            ]
        );
    }

    private ListItem CreateDefaultSearchListItem()
    {
        return new SearchItem(
            searchItem: new() { Query = "chrome://newtab", IsURL = true, Type = SuggestionType.Website },
            profilesOrdered: Profiles,
            searchCommand: (string profileName) =>
            {

                // change default
                DefaultProfile = profileName;

                // move the default profile to the top
                Profiles = Profiles
                    .OrderByDescending(_profile => _profile.Name == DefaultProfile)
                    .ToList();

                // open chrome & allow UI to hide
                chrome.OpenUrlInChromeProfile(
                    url: "chrome://newtab",
                    profileName: profileName,
                    newWindow: OpenInNewWindow
                );

                // save profiles
                Chrome.SaveProfiles(Profiles, ProfileConfigPath);
            },
            suggestText: false,
            verb: "Open Chrome"
        );
    }


    public async Task UpdateSuggestions(string query)
    {
        // update suggestions
        List<Suggestion> suggestions = await chrome.GetSuggestionsAsync(query);


        // find relevant history
        Suggestion[] historyItems = RelevantFromHistory(query);

        Suggestion[] historyImportant = [];

        if (historyItems.Length > 0 && suggestions.Count > 0)
        {
            // remove history suggestions
            suggestions.RemoveAll(suggestion => historyItems.Any(history => history.Query == suggestion.Query));


            // find exact matching URLs to place above user query
            historyImportant = historyItems
                .Where(item =>
                    item.IsURL == true &&
                    startsWithLevenshtein(query, item.Query) == 0
                )
                .OrderByDescending(item => startsWithLevenshtein(query, item.Query))
                .ToArray();

            // remove important items from regular & only take most similar
            historyItems = historyItems
                .ExceptBy(historyImportant.Select(item => item.Query), item => item.Query)
                .OrderByDescending(item => startsWithLevenshtein(query, item.Query))
                .Take(2)
                .ToArray();

            // add less relevant urls as suggestions above the user prompt
            suggestions.InsertRange(
                index: 0,
                historyItems
            );
        }

        // convert to ListItems & to suggestions
        Suggestions.Update(
            query: query,
            above: historyImportant.Select(CreateSearchListItem).ToArray(),
            below: suggestions.Select(CreateSearchListItem).ToArray()
        );
    }

    public void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
    {
        OpenInNewWindow = eventArgs.OpenInNewBrowser;

        // update history
        chrome.OnSettingsChanged(sender, eventArgs);

        // update page to default
        if (string.IsNullOrEmpty(SearchText)) UpdateSearchText("", "");

        Debug.WriteLine($"Settings updated: {eventArgs.HistoryLength}");
    }

    // compare the first part of the history query to the current query
    // and order by most similar
    Suggestion[] RelevantFromHistory(string query)
    {
        // don't run for non queries
        if (query.Length < 1) return [];

        // if there aren't enough letters, use exact match
        if (query.Length <= LevenshteinMaxDistance)
        {
            return chrome.History
                .Where(item => item.Query.Length >= query.Length)               // only check history items longer than query
                .Where(item => query == item.Query.Substring(0, query.Length))  // check if the first chars match exactly
                                                                                //.OrderBy(item => ) // TODO: order by recency & search count
                .ToArray();
        }

        Suggestion[] t = chrome.History
            .Where(item => item.Query.Length >= query.Length)
            .Where(item => startsWithLevenshtein(
                query,
                item.Query) < LevenshteinMaxDistance
            )
            .OrderByDescending(item => startsWithLevenshtein(
                query,
                item.Query
            ))
            .ToArray();

        return t;
    }


    public static int startsWithLevenshtein(string a, string b)
    {
        // initialise
        int dist = int.MaxValue;

        // compare the first part of the two
        if (a.Length > b.Length)
            dist = ComputeDistanceLevenshtein(
                b,
                a.Substring(0, b.Length)
            );
        else
            dist = ComputeDistanceLevenshtein(
                a,
                b.Substring(0, a.Length)
            );

        return dist;
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


}