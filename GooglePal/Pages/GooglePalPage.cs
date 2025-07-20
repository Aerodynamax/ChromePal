// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Windows.Data.Xml.Dom;

namespace GooglePal;


// suggestions
// Google suggestions API XML parsing

[XmlRoot(ElementName = "toplevel")]
public class SuggestionsAPIResult
{
    [XmlElement(ElementName = "CompleteSuggestion")]
    public List<Suggestion> CompleteSuggestion { get; set; }

}


[XmlRoot(ElementName = "suggestion")]
public class Suggestion
{

    [XmlAttribute(AttributeName = "data")]
    public required string Data { get; set; }
}

internal sealed partial class GooglePalPage : DynamicListPage
{
    // suggestions
    public List<string> Suggestions = [];

    // rate limiting
    public int RatelimitTimeMS = 300;
    public int OutOfDateLength = 3;

    // required for getting user input                                             +1 to include actual search query 🢆
    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged(Suggestions.Count + 1);

    // icon
    private static IconInfo SearchIcon = IconHelpers.FromRelativePath("Assets\\SearchIconFontAwesome.png");


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
    }

    
    private string previousSearchQuery = "";

    public override IListItem[] GetItems()
    {
        // show generic text if no input available
        if (string.IsNullOrEmpty(SearchText))
        {
            return [
                new ListItem(new NoOpCommand()) { Title = "Start typing ..." },
            ];
        }

        // format suggestions into command palette list items
        List<ListItem> queries = [

            // always show actual option
            new ListItem(
                new OpenUrlCommand($"https://www.google.com/search?q={HttpUtility.UrlEncode(SearchText)}") { Name = "Search", Icon = SearchIcon }

            )
            { Title = $"{SearchText}", Subtitle = "Search" }
        ];


        // update suggestions if very out of date
        if (SearchText.Length > previousSearchQuery.Length + OutOfDateLength || SearchText.Length < previousSearchQuery.Length - OutOfDateLength)
            Task.Run(async () => await UpdateSuggestions(previousSearchText: SearchText, requireNoChangeTimeMS: 0));

        // update suggestions thread
        else if (SearchText != previousSearchQuery)
            Task.Run(async() => await UpdateSuggestions(previousSearchText: SearchText, requireNoChangeTimeMS: RatelimitTimeMS));


        // update previous
        //previousSearchQuery = SearchText;

        // convert suggestions into list items and add to the queries list
        Suggestions
            .ConvertAll(suggestion =>
                new ListItem(
                    new OpenUrlCommand($"https://www.google.com/search?q={HttpUtility.UrlEncode(suggestion)}") { Name = "Search", Icon = SearchIcon }

                )
                { Title = $"{suggestion}", Subtitle = "Search" }
            )
            .ForEach(item => queries.Add(item));

        // show search options
        return queries.ToArray();
    }

    [RequiresUnreferencedCode("Calls GooglePal.GooglePalPage.GetSuggestions(String)")]
    public async Task UpdateSuggestions(string previousSearchText, int requireNoChangeTimeMS)
    {
        await Task.Delay(requireNoChangeTimeMS);

        // don't update suggestions if user has typed more
        if (SearchText != previousSearchText) return;
        if (SearchText == previousSearchQuery) return;

        previousSearchQuery = SearchText;

        Debug.WriteLine("Updating suggestions");

        // update suggestions
        Suggestions = GetSuggestions(SearchText);

        // update UI
        RaiseItemsChanged(Suggestions.Count + 1);
    }
}