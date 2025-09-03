using GooglePal.Components;
using GooglePal.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Windows.System;

namespace GooglePal;

public class CachedItem
{
    public required string URL { get; set; }
    public required bool IsWebsite {  get; set; }
}

internal sealed partial class GooglePalPage : DynamicListPage
{
    // suggestions
    public List<Google.Suggestion> Suggestions = [];

    public Chrome chrome = new();

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
        RaiseItemsChanged(Suggestions.Count + 1);

        // don't query if empty
        if (string.IsNullOrEmpty(SearchText.Trim())) return;

        await UpdateSuggestions(
                previousSearchText: SearchText,
                requireNoChangeTimeMS: 0
        );

    }

    // icons
    private static IconInfo SearchIcon = new IconInfo("\uE721");
    private static IconInfo VisitIcon = new IconInfo("\uE774");
    private static IconInfo HistoryIcon = new IconInfo("\uE81C");
    private static IconInfo CalculatorIcon = new IconInfo("\uE8EF");


    public GooglePalPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\GoogleLogo.png");
        Title = "Search Google";
        Name = "Search with Chrome";

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

        // create user's exact query option
        // [ https://stackoverflow.com/a/7581824 ]
        bool userTypedURL = Google.IsValidURL(SearchText);

        ListItem userQuery = CreateSearchListItem(new() {
            Query = SearchText,
            Type = userTypedURL ? Google.SuggestionType.Website : Google.SuggestionType.Query,
            IsURL = userTypedURL
        });

        // format suggestions into command palette list items
        List<ListItem> queries = [

            // always show raw search text option
            userQuery
        ];


        // convert suggestions into list items and add to the queries list
        queries.AddRange(Suggestions.Select(CreateSearchListItem).Where(suggestion => suggestion.Title.Trim() != SearchText.Trim()));

        // show search options
        return queries.ToArray();
    }

    private ListItem CreateSearchListItem(Google.Suggestion searchItem)
    {
        return new SearchItem(
            searchItem: searchItem,
            profilesOrdered: Profiles,
            searchCommand: (string profileName) => {

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
            }
        );
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

                    chrome.OpenUrlInChromeProfile(
                        url: "chrome://newtab",
                        profileName: profile.Name,
                        newWindow: OpenInNewWindow
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
            new AnonymousCommand(() => chrome.OpenUrlInChromeProfile(
                        url: "chrome://newtab",
                        profileName: DefaultProfile,
                        newWindow: OpenInNewWindow
                    ))
            { Name = $"Open Chrome ({DefaultProfile})", Icon = SearchIcon }

        )
        { Title = "Search Google or type a URL", Subtitle = $"Open as: {DefaultProfile}", MoreCommands = moreCommands };


        return listItem;
    }


    public async Task UpdateSuggestions(string previousSearchText, int requireNoChangeTimeMS)
    {
        await Task.Delay(requireNoChangeTimeMS);

        // don't update suggestions if user has typed more
        if (SearchText != previousSearchText) return;

        previousSearchQuery = SearchText;

        //Debug.WriteLine("Updating suggestions");
        
        // update suggestions (not including values that are the search text)
        Suggestions = (await chrome.GetSuggestionsAsync(SearchText.Trim())).suggestions;

        // add history
        Google.Suggestion[] historyItems = RelevantFromHistory(SearchText);

        // remove history suggestions
        Suggestions.RemoveAll(suggestion => historyItems.Any(history => history.Query == suggestion.Query));

        Suggestions.InsertRange(0, historyItems);

        // update UI
        RaiseItemsChanged(Suggestions.Count + 1);
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
    Google.Suggestion[] RelevantFromHistory(string query)
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

        Google.Suggestion[] t = chrome.History
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