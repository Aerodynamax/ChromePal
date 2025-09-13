using Microsoft.CommandPalette.Extensions.Toolkit;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using static GooglePal.Google;

namespace GooglePal
{
    public class Google
    {
        public enum SuggestionType
        {
            Query,
            Website,
            History,
            KnowledgePanel,
            Calculation,
            Definition
        }
        public class Suggestion
        {
            public required string Query {  get; set; }
            public required SuggestionType Type { get; set; }
            public required bool IsURL { get; set; }
            
            // for special suggestions
            public string? Title { get; set; }
            public string? Description { get; set; }
            public IconInfo? Icon { get; set; }
        }

        private HttpClient client = new();

        public Google()
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        }

        // cache suggestions to reduce requests
        protected class CachedQuery
        {
            public required string Query { get; set; }
            public required List<Suggestion> Suggestions { get; set; }
        }

        protected List<CachedQuery> CachedQueries = [];

        public virtual async Task<List<Suggestion>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default) => 
            // return all suggestions by default
            await GetSuggestionsAsync(
                query: query,
                suggestionTypes: [SuggestionType.Query, SuggestionType.Website, SuggestionType.Calculation, SuggestionType.Definition, SuggestionType.KnowledgePanel],
                cancellationToken: cancellationToken
            );


        public virtual async Task<List<Suggestion>> GetSuggestionsAsync(string query, SuggestionType[] suggestionTypes, CancellationToken cancellationToken = default)
        {
            // return from cached suggestions if available
            CachedQuery? cached = CachedQueries.FirstOrDefault(item => item.Query == query);
            if (cached != null) return cached.Suggestions;

            List<Suggestion> suggestions = [];
            string url = $"https://www.google.com/complete/search?output=chrome&q={HttpUtility.UrlEncode(query)}";

            try
            {
                HttpContent response = (await client.GetAsync(url, cancellationToken)).Content;

                // deserialise
                var json = JsonDocument.Parse(response.ReadAsStringAsync(cancellationToken).Result);

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

                // create suggestions from results
                foreach (var _query in queryTitles.Index())
                {
                    SuggestionType type = queryTypes.ElementAt(_query.Index);

                    string queryText = _query.Item;

                    // remove "http://" from links
                    if (type == SuggestionType.Website)
                    {
                        Uri website = new Uri(_query.Item);
                        queryText = website.Host + website.PathAndQuery + website.Fragment;

                        queryText = queryText.TrimEnd('/');
                    }

                    // only add suggestions that are wanted
                    if (suggestionTypes.Contains(type))
                        suggestions.Add(new Suggestion()
                        {
                            Query = queryText,
                            Type = type,
                            IsURL = type == SuggestionType.Website
                        });
                }

                List<Suggestion> otherSuggestions = [];

                // get Google.com suggestions (non URLs)
                if (suggestionTypes.Contains(SuggestionType.KnowledgePanel) || suggestionTypes.Contains(SuggestionType.Definition))
                    otherSuggestions = await GetQueriesAdditionalAsync(query: query, cancellationToken: cancellationToken);

                // add knowledge panels
                if (suggestionTypes.Contains(SuggestionType.KnowledgePanel))
                    foreach (var specialSuggestion in otherSuggestions.Where(item => item.Type != SuggestionType.Definition))
                    {
                        int index = suggestions.FindIndex(item => item.Query == specialSuggestion.Query);

                        // replace non-special with special if present
                        if (index != -1)
                            suggestions[index] = specialSuggestion;

                        // also add if query
                        if (specialSuggestion.Query == query)
                            suggestions.Insert(index: 0, item: specialSuggestion);
                    }

                // add definition
                Suggestion? definition = otherSuggestions.FirstOrDefault(item => item.Type == SuggestionType.Definition);

                if (definition != null && suggestionTypes.Contains(SuggestionType.Definition))
                    suggestions.Insert(index: 0, item: definition);

                // add to cache
                CachedQueries.Add(new()
                {
                    Query = query,
                    Suggestions = suggestions
                });
            }
            catch (Exception ex) { Debug.WriteLine($"ERROR: {ex}"); }

            return suggestions;
        }

        public virtual async Task<List<Suggestion>> GetQueriesAdditionalAsync(string query, CancellationToken cancellationToken = default)
        {
            string url = $"https://www.google.com/complete/search?client=gws-wiz&q={HttpUtility.UrlEncode(query)}";

            try
            {
                // get API data
                HttpContent response = (await client.GetAsync(url, cancellationToken)).Content;

                string resp = (await response.ReadAsStringAsync(cancellationToken));

                // get the autocomplete suggestions list
                int startIndex = resp.IndexOf('(') + 1;
                int endIndex   = resp.LastIndexOf(')');

                resp = resp.Substring(startIndex: startIndex, length: endIndex - startIndex);

                // deserialise
                var json = JsonDocument.Parse(resp);

                var results = json
                    .RootElement.EnumerateArray()
                    .ElementAt(0).EnumerateArray();


                List<Suggestion> suggestions = [];

                
                foreach ( var result in results )
                {
                    var suggestion = result.EnumerateArray();

                    // special results have 4 items
                    if (suggestion.Count() < 4) continue;

                    // definitions
                    if (suggestion.LastOrDefault().TryGetProperty("ansa", value: out JsonElement definition))
                        suggestions.Add(ParseDefinition(definition));

                    // special results
                    else if (suggestion.LastOrDefault().TryGetProperty("lm", value: out JsonElement specialQuery))
                        suggestions.Add(ParseSpecialSuggestion(result));

                }

                suggestions.RemoveAll(item => item.Query == "");

                return suggestions;
            }
            catch (Exception ex) { Debug.WriteLine($"ERROR: {ex}"); }

            return [];
        }

        /// <summary>
        ///     Creates a Suggestion element from a google.com search definition suggestion.
        /// </summary>
        /// <param name="ansaElement">The property called "ansa" in the API result.</param>
        /// <returns>Suggestion</returns>
        static Suggestion ParseDefinition(JsonElement ansaElement)
        {
            // Example
            //  {
            //      "l": [
            //          {
            //              "il": {
            //                  "at": {
            //                      "t": "\u2022 /rɪˈzɛntm(ə)nt/",
            //                      "tt": 20
            //                  },
            //                  "t": [
            //                      {
            //                          "t": "resen\u003cb\u003etment meaning\u003c\/b\u003e",
            //                          "tt": 8
            //                      }
            //                  ]
            //              }
            //          },
            //          {
            //              "il": {
            //                  "t": [
            //                      {
            //                          "ln": 3,
            //                          "t": "bitter indignation at having been treated unfairly.",
            //                          "tt": 20
            //                      }
            //                  ]
            //              }
            //          }
            //      ]
            //  }

            var suggestion = ansaElement.GetProperty("l").EnumerateArray();

            // the "__ meaning" text
            string query = suggestion.FirstOrDefault()
                .GetProperty("il")
                .GetProperty("t").EnumerateArray()
                .FirstOrDefault()
                .GetProperty("t").GetString()
                ?? string.Empty;

            // remove html tags (bold)
            query = Regex.Replace(query, "<[^>]*>", string.Empty);

            // the "/__/" pronounciation text
            string pronounciation = suggestion.FirstOrDefault()
                .GetProperty("il")
                .GetProperty("at")
                .GetProperty("t").GetString()
                ?? string.Empty;

            return new ()
            {
                Query = query,
                Type = SuggestionType.Definition,
                IsURL = false,

                Title = string.IsNullOrEmpty(query + pronounciation) ? null : $"{query} {pronounciation}",
                Description = suggestion.LastOrDefault().GetProperty("il").GetProperty("t").EnumerateArray().FirstOrDefault().GetProperty("t").GetString()
            };
        }

        /// <summary>
        ///     Creates a Suggestion element from a google.com search special suggestion. <br />
        ///     (e.g. suggestions with Icons / Titles / Descriptions)
        /// </summary>
        /// <param name="queryElement">The array that holds the entire suggestion (search string + main type? + suggestsubtypes + properties).</param>
        /// <returns>Suggestion</returns>
        static Suggestion ParseSpecialSuggestion(JsonElement queryElement)
        {
            // Example element
            //  [
            //      "earth",
            //      46,
            //      [
            //          512,
            //          433
            //      ],
            //      {
            //          "lm": [],
            //          "zh": "Earth",
            //          "zi": "Planet",
            //          "zp": {
            //                  "gs_ssp": "eJzj4tDP1TcwyjI3NGD0Yk1NLCrJAAAmeQSm"
            //          },
            //          "zs": "https://encrypted-tbn0.gstatic.com/images?q\u003dtbn:ANd9GcSEIKrF7NWt8SzqCEuoWveu1oGeOzzXaS6by3-PSryO4w\u0026s\u003d10"
            //      }
            //  ]

            var suggestion = queryElement.EnumerateArray();

            // get title & format
            string query = suggestion.ElementAt(0).GetString() ?? string.Empty;

            // remove html tags (bold)
            query = Regex.Replace(query, "<[^>]*>", string.Empty);

            return new()
            {
                Query = query,
                IsURL = false,
                Type = SuggestionType.KnowledgePanel,

                Title = suggestion.LastOrDefault().GetProperty("zh").GetString(),
                Description = suggestion.LastOrDefault().GetProperty("zi").GetString(),
                Icon = new IconInfo(suggestion.LastOrDefault().GetProperty("zs").GetString())
            };
        }



        /// <summary>
        ///     Creates a Suggestion element from a query.
        ///     If isWebsite isn't specified it will validate the url.
        /// </summary>
        /// <param name="query">The user input to create suggestion from</param>
        /// <param name="isWebsite">Whether the query is a website or not.</param>
        /// <returns>Suggestion</returns>
        public static Suggestion CreateSuggestion(string query, bool isWebsite)
        {
            return new Suggestion()
            {
                Query = query,
                Type = isWebsite ? SuggestionType.Website : SuggestionType.Query,
                IsURL = isWebsite
            };
        }

        /// <summary>
        ///     Creates a Suggestion element from a query.
        ///     If isWebsite isn't specified it will validate the url.
        /// </summary>
        /// <param name="query">The user input to create suggestion from</param>
        /// <param name="isWebsite">Whether the query is a website or not.</param>
        /// <returns>Suggestion</returns>
        public static Suggestion CreateSuggestion(string query)
        {
            bool isWebsite = IsValidURL(query);

            return new Suggestion()
            {
                Query = query,
                Type = isWebsite ? SuggestionType.Website : SuggestionType.Query,
                IsURL = isWebsite
            };
        }

        /// <summary>
        /// Checks if a URL string is valid
        /// </summary>
        /// <param name="url">The URL to validate</param>
        /// <returns>True if the URL is valid, false otherwise</returns>
        public static bool IsValidURL(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // return on obviously malformed URLs
            if (url.Contains(' ')) return false;
            if (!url.Contains('.') && !url.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;


            string urlToParse = url.Trim();

            // add scheme if required
            if (!urlToParse.Contains("://")) urlToParse = "http://" + urlToParse;

            // check if valid (e.g. no spaces) + with scheme
            if (!Uri.TryCreate(urlToParse, UriKind.Absolute, out Uri? uriResult)) return false;


            // only allow http & https
            if (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            // check if well-formed domain name / IP address
            if (uriResult.HostNameType != UriHostNameType.Dns && uriResult.HostNameType != UriHostNameType.IPv4 && uriResult.HostNameType != UriHostNameType.IPv6)
            {
                return false;
            }

            // check if tld has at least two characters.
            string host = uriResult.DnsSafeHost;
            string[] hostParts = host.Split('.');

            // This allows for "localhost" to pass.
            if (hostParts.Length == 1 && host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (hostParts.Length < 2)
            {
                return false;
            }

            // The TLD (last part) should not be numeric and should generally be at least two letters long.
            string tld = hostParts.Last();
            if (tld.Length < 2 || tld.Any(char.IsDigit))
            {
                return false;
            }

            return true;
        }

    }
}
