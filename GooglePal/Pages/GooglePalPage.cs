using GooglePal.Components;
using GooglePal.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
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

    // settings

    public bool OpenInNewWindow;

    // history
    public static int LevenshteinMaxDistance = 3;

    public int historyLength = 5;

    // update UI when search bar changes
    public async override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // reset history length
        if (!string.IsNullOrEmpty(newSearch)) historyLength = MaxPageItems - 1;

        // change main search text
        Suggestions.UpdateQuery(newSearch);

        // update suggestions
        if (!string.IsNullOrEmpty(newSearch) && MaxPageItems > 1) await UpdateSuggestions(newSearch);

        // stop loading
        if (string.IsNullOrEmpty(newSearch) && IsLoading) IsLoading = false;
    }

    public GooglePalPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\GoogleLogo.png");
        Title = "Search Google";
        Name = "Search with Chrome";

        PlaceholderText = "Search Google or type a URL";

        // create page list
        Suggestions = new(
            length: MaxPageItems,
            createListItem: CreateSearchListItem,
            updateUI: () => RaiseItemsChanged(totalItems: Suggestions?.Items.Length ?? 1),
            createDefaultPage: () => [
                CreateDefaultSearchListItem(),
                ..chrome.History.TakeLast(historyLength).Reverse().Select(CreateSearchListItem),
                CreateMoreHistoryListItem()
            ]
        );
    }

    public override IListItem[] GetItems() => Suggestions.Items;

    private ListItem CreateSearchListItem(Suggestion searchItem)
    {
        string url = searchItem.IsURL ? searchItem.Query : $"https://www.google.com/search?q={HttpUtility.UrlEncode(searchItem.Query)}";

        List<CommandContextItem> otherCommands = [];

        // add "Copy to clipboard"
        if (searchItem.Type != SuggestionType.Calculation)
            otherCommands.Add(new(
                    name: $"Copy \"{url}\" to clipboard",
                    title: $"Copy \"{url}\" to clipboard",
                    action: () =>
                    {
                        // set clipboard to URL
                        ClipboardHelper.SetText(url);
                    },
                    result: CommandResult.ShowToast(new ToastArgs() { Message = "Copied URL to clipboard", Result = CommandResult.Hide() })
                )
            {
                Icon = SearchItem.CopyIcon,
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                        ctrl: true,
                        shift: true,
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
            profilesOrdered: chrome.Profiles,
            searchCommand: (string profileName) =>
            {
                // open chrome
                chrome.OpenUrlInChromeProfile(
                    url: url,
                    profileName: profileName,
                    newWindow: OpenInNewWindow,
                    addToHistory: (searchItem.Type <= SuggestionType.History), // Query / Website / History
                    shuffleProfiles: true
                );

                // reset page
                Suggestions.UpdateQuery("");

            },
            otherSubcommands: otherCommands.ToArray()
        );
    }

    private ListItem CreateDefaultSearchListItem()
    {
        return new SearchItem(
            searchItem: new() { Query = "chrome://newtab", IsURL = true, Type = SuggestionType.Website },
            profilesOrdered: chrome.Profiles,
            searchCommand: (string profileName) =>
            {
                // open chrome
                chrome.OpenUrlInChromeProfile(
                    url: "chrome://newtab",
                    profileName: profileName,
                    newWindow: OpenInNewWindow,
                    addToHistory: false,
                    shuffleProfiles: true
                );

                // reset page
                Suggestions.UpdateQuery("");
            },
            suggestText: false,
            verb: "Open Chrome"
        );
    }

    private ListItem? CreateMoreHistoryListItem()
    {
        if (historyLength >= chrome.History.Count) return null;
        return new()
        {
            Title = "Show more history",
            Icon = SearchItem.MoreIcon,
            Command = new AnonymousCommand(() =>
            {
                // double the length
                historyLength *= 2;

                // clamp history length
                if (historyLength > chrome.History.Count) historyLength = chrome.History.Count;

                IsLoading = true;

                // update
                Suggestions.UpdateHomePage();

                IsLoading = false;
            })
            {
                Icon = SearchItem.MoreIcon,
                Result = CommandResult.KeepOpen(),
                Name = "Show more"
            }
        };
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

        // update Chrome properties
        chrome.OnSettingsChanged(sender, eventArgs);

        // suggestions + query
        MaxPageItems = eventArgs.SuggestionLength + 1;

        Suggestions.UpdateLength(MaxPageItems);

        // update page to default
        Suggestions.UpdateHomePage();
    }

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
                //.OrderBy(item => )                                            // TODO: order by recency & search count
                .ToArray();
        }

        Suggestion[] t = chrome.History
            .Where(item => item.Query.Length >= query.Length)
            .Where(item => Levenshtein.startsWithLevenshtein(
                val1: query,
                val2: item.Query,
                similarity: LevenshteinMaxDistance
            ))
            .OrderByDescending(item => Levenshtein.startsWithLevenshtein(
                query,
                item.Query
            ))
            .ToArray();

        return t;
    }

}