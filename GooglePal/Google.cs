using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace GooglePal
{
    public class Google
    {
        public enum SuggestionType
        {
            Query,
            History,
            Website,
            Calculation
        }
        public class Suggestion
        {
            public required string Query;

            public required SuggestionType Type;

            public required bool IsURL;
        }

        public class Suggestions
        {
            public required string Query;

            public required List<Suggestion> suggestions;
        }

        private HttpClient client = new();


        // [ https://github.com/Daydreamer-riri/CmdPal-WebSearchShortcut/blob/main/CmdPalWebSearchShortcut/WebSearchShortcut/SuggestionsProviders/Google.cs ]
        public virtual async Task<Suggestions> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            string url = $"https://www.google.com/complete/search?output=chrome&q={HttpUtility.UrlEncode(query)}";

            await using var response = await client.GetStreamAsync(url, cancellationToken);

            var json = await JsonDocument.ParseAsync(response, cancellationToken: cancellationToken);

            // deserialise

            var suggestionQueries = json.RootElement.EnumerateArray().ElementAt(1);
            var suggestionType = json.RootElement.EnumerateArray().ElementAt(4).GetProperty("google:suggesttype");

            // format query strings
            string[] queryTitles = [
                ..suggestionQueries
                    .EnumerateArray()
                    .Select(o => o.GetString())
                    .Where(s => s != null)
                    .Select(s => s!)
            ];

            // format query types
            SuggestionType[] queryTypes = [
                ..suggestionType
                    .EnumerateArray()
                    .Select(o => o.GetString())
                    .Where(s => s != null)
                    .Select(s => {
                        // convert into type
                        switch(s)
                        {
                            case "QUERY": return SuggestionType.Query;
                            case "NAVIGATION": return SuggestionType.Website;
                            case "CALCULATOR": return SuggestionType.Calculation;

                            default: return SuggestionType.Query;
                        }
                    })
            ];

            List<Suggestion> suggestions = [];

            // create suggestions from results
            foreach ( var _query in queryTitles.Index())
            {
                suggestions.Add(new Suggestion() {
                    Query = _query.Item,
                    Type = queryTypes.ElementAt(_query.Index),
                    IsURL = queryTypes.ElementAt(_query.Index) == SuggestionType.Website
                });
            }


            return new Suggestions() { Query = query, suggestions = suggestions };
        }
    }
}
