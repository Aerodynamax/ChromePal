// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    public string Data { get; set; }
}

internal sealed partial class GooglePalPage : DynamicListPage
{

    // required for getting user input
    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged(UIItems);

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

    public int UIItems;

    [RequiresUnreferencedCode("Calls GooglePal.GooglePalPage.GetSuggestions(String)")]
    public override IListItem[] GetItems()
    {
        // show generic text
        if (string.IsNullOrEmpty(SearchText))
        {
            return [
                new ListItem(new NoOpCommand()) { Title = "Start typing ..." },
            ];
        }

        // format suggestions into command palette list items
        List<ListItem> suggestions = [

            // always show actual option
            new ListItem(
                new OpenUrlCommand($"https://www.google.com/search?q={HttpUtility.UrlEncode(SearchText)}") { Name = "Search", Icon = SearchIcon }

            )
            { Title = $"{SearchText}", Subtitle = "Search" }
        ];

        // get suggestions, convert them into list items, then add them to the suggestions
        GetSuggestions(SearchText)
            .ConvertAll(suggestion =>
                new ListItem(
                    new OpenUrlCommand($"https://www.google.com/search?q={HttpUtility.UrlEncode(suggestion)}") { Name = "Search", Icon = SearchIcon }

                )
                { Title = $"{suggestion}", Subtitle = "Search" }
            )
            .ForEach(item => suggestions.Add(item));

        UIItems = suggestions.Count;

        // show search options
        return suggestions.ToArray();
    }
}