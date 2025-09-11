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
using static GooglePal.Google;

namespace GooglePal;


internal sealed partial class GooglePalPage : DynamicListPage
{
    // suggestions
    public int MaxPageItems = 6;

    public SuggestionList Suggestions;

    public Chrome chrome = new();

    public bool ShowLoading = true;

    // chrome profile
    public string ProfileConfigPath = Path.Combine(
        Path.GetDirectoryName(System.AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(),
        "profiles.json"
    );

    public List<Chrome.Profile> Profiles;
    public string DefaultProfile;

    public bool OpenInNewWindow;

    // history
    public static int LevenshteinMaxDistance = 3;

    // update UI when search bar changes
    public async override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // change main search text
        Suggestions.UpdateQuery(newSearch);

        // update suggestions
        if (!string.IsNullOrEmpty(newSearch) && MaxPageItems > 1) UpdateSuggestions(newSearch);
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

        // create page list
        Suggestions = new(
            length: MaxPageItems,
            createListItem: CreateSearchListItem,
            updateUI: () => RaiseItemsChanged(totalItems: Suggestions?.Items.Length ?? 1),
            createDefaultPage: () => [
                CreateDefaultSearchListItem(),
                ..chrome.History.TakeLast(5).Reverse().Select(CreateSearchListItem)
            ]
        );
    }

    public override IListItem[] GetItems() => Suggestions.Items;

    private ListItem CreateSearchListItem(Suggestion searchItem)
    {
        List<CommandContextItem> otherCommands = [];

        // add "Copy to clipboard"
        if (searchItem.Type != SuggestionType.Calculation)
            otherCommands.Add(new(
                    name: $"Copy \"{searchItem.Query}\" to clipboard",
                    title: $"Copy \"{searchItem.Query}\" to clipboard",
                    action: () =>
                    {
                        // set clipboard to query
                        ClipboardHelper.SetText(searchItem.Query);
                    },
                    result: CommandResult.ShowToast(new ToastArgs() { Message = "Copied to clipboard", Result = CommandResult.KeepOpen() })
                )
            {
                Icon = SearchItem.CopyIcon,
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                        ctrl: true,
                        vkey: (int)VirtualKey.C
                    )
            });

        // add "Remove from history"
        if (searchItem.Type == SuggestionType.History)
            otherCommands.Add(new(
                    name: "Remove from history",
                    title: "Remove from history",
                    action: () =>
                    {
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
            });

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
                    newWindow: OpenInNewWindow,
                    addToHistory: searchItem.Type != SuggestionType.Calculation
                ));

                // reset page
                Suggestions.UpdateQuery("");

                // save profiles
                Chrome.SaveProfiles(Profiles, ProfileConfigPath);
            },
            otherSubcommands: otherCommands.ToArray()
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
                    newWindow: OpenInNewWindow,
                    addToHistory: false
                );

                // reset page
                Suggestions.UpdateQuery("");

                // save profiles
                Chrome.SaveProfiles(Profiles, ProfileConfigPath);
            },
            suggestText: false,
            verb: "Open Chrome"
        );
    }


    public async Task UpdateSuggestions(string query)
    {
        // loading indicator
        if (!IsLoading && ShowLoading) IsLoading = true;

        // update suggestions
        List<Suggestion> suggestions = await chrome.GetSuggestionsAsync(query);


        // find relevant history
        Suggestion[] historyItems = RelevantFromHistory(query);

        Suggestion? historyImportant = null;

        if (historyItems.Length > 0 && suggestions.Count > 0)
        {
            // remove history suggestions
            suggestions.RemoveAll(suggestion => historyItems.Any(history => history.Query == suggestion.Query));


            // find exact matching URLs to place above user query
            historyImportant = historyItems
                .Where(item =>
                    item.IsURL == true &&
                    Levenshtein.startsWithLevenshtein(query, item.Query) == 0
                )
                .OrderByDescending(item => Levenshtein.startsWithLevenshtein(query, item.Query))
                .FirstOrDefault();

            // remove important items from regular & only take most similar
            historyItems = historyItems
                .Where(item => item.Query != historyImportant?.Query)
                .OrderByDescending(item => Levenshtein.startsWithLevenshtein(query, item.Query))
                .Take(2)
                .ToArray();

            // add less relevant urls as suggestions
            suggestions.InsertRange(
                index: 0,
                historyItems
            );
        }

        // convert to ListItems & to suggestions
        Suggestions.UpdateSuggestions(
            query: query,
            above: historyImportant != null ? CreateSearchListItem(historyImportant) : null,
            below: suggestions.Select(CreateSearchListItem).ToArray()
        );

        // loading indicator
        if (query == SearchText && ShowLoading) IsLoading = false;
    }

    public void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
    {
        OpenInNewWindow = eventArgs.OpenInNewBrowser;
        ShowLoading = eventArgs.ShowLoading;

        // suggestions + query
        MaxPageItems = eventArgs.SuggestionLength + 1;

        Suggestions.UpdateLength(MaxPageItems);


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
            .Where(item => Levenshtein.startsWithLevenshtein(
                query,
                item.Query) < LevenshteinMaxDistance
            )
            .OrderByDescending(item => Levenshtein.startsWithLevenshtein(
                query,
                item.Query
            ))
            .ToArray();

        return t;
    }

}