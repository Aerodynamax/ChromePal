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
using System.Threading.Tasks;
using System.Web;
using Windows.System;
using static GooglePal.Google;

namespace GooglePal;


internal sealed partial class GooglePalPage : DynamicListPage
{
    // suggestions
    public List<Suggestion> Suggestions = [];

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
    public string HistoryConfigPath = Path.Combine(
        Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(),
        "history.json"
    );
    public int HistoryLength = 999;

    public int LevenshteinMaxDistance = 3;



    // update UI when search bar changes
    public async override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // don't query if empty
        if (!string.IsNullOrEmpty(SearchText))
        {
            await UpdateSuggestions(query: SearchText);

            RaiseItemsChanged(Suggestions.Count);
        }
        else RaiseItemsChanged(1);


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
    }

    public override IListItem[] GetItems()
    {
        // show generic text if no input available
        if (string.IsNullOrEmpty(SearchText.Trim()) || Suggestions.Count < 1)
        {
            return [
                CreateDefaultSearchListItem()
            ];
        }

        // format suggestions into command palette list items
        List<ListItem> queries = Suggestions.Select(CreateSearchListItem).ToList();

        // convert suggestions into list items and add to the queries list
        //queries.AddRange();

        if (queries.Count < 1) return [];

        // remove text complete for the exact query
        var firstItem = queries.FirstOrDefault(query => query.Icon != SearchItem.HistoryIcon || query.Title == SearchText);
        if (firstItem != null) firstItem.TextToSuggest = "";

        return queries.ToArray();
    }

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
            searchCommand: (string profileName) => {

                // change default
                DefaultProfile = profileName;

                // move the default profile to the top
                Profiles = Profiles
                    .OrderByDescending(_profile => _profile.Name == DefaultProfile)
                    .ToList();

                // open chrome & allow UI to hide
                chrome.OpenUrlInChromeProfile(
                    url: "https://google.com",
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

        if (historyItems.Length > 0 && suggestions.Count > 0)
        {   
            // remove history suggestions
            suggestions.RemoveAll(suggestion => historyItems.Any(history => history.Query == suggestion.Query));

            // find exact matching URLs to place above user query
            Suggestion[] historyImportant = historyItems
                .Where(item =>
                    item.IsURL == true &&
                    query.Trim() == item.Query.Trim().Substring(0, query.Trim().Length)
                    || query == item.Query
                )
                .OrderBy(item => query == item.Query)
                .ToArray();

            historyItems = historyItems.Where(historyItem => !historyImportant.Any(historyImp => historyItem.Query == historyImp.Query)).ToArray();

            // add relevant urls as suggestions above the user prompt
            // (if the user prompt still exists!)
            suggestions.InsertRange(
                index: (suggestions.Count == 0) ? 0 : 1,
                historyItems
            );

            if (historyImportant.Length > 0) suggestions.InsertRange(0, historyImportant);
        }

        Suggestions = suggestions;
    }


    public void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
    {
        OpenInNewWindow = eventArgs.OpenInNewBrowser;

        // update history
        chrome.OnSettingsChanged(sender, eventArgs);

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
            .Where(item => ComputeDistanceLevenshtein(
                query,
                item.Query.Substring(0, query.Length)) < LevenshteinMaxDistance
            )
            .OrderBy(item => ComputeDistanceLevenshtein(
                query,
                item.Query.Substring(0, query.Length)
            ))
            .ToArray();

        return t;
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