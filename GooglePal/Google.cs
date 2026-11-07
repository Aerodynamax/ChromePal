using GooglePal.Components;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

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

            // override equality check for things like FirstOrDefault
            public static bool operator ==(Suggestion? a, Suggestion? b) => a?.Equals(b) == true;
            public static bool operator !=(Suggestion? a, Suggestion? b) => !a?.Equals(b) == true;
            public override bool Equals(object? obj) => obj != null && Equals(obj as Suggestion);
            public bool Equals(Suggestion? other) => !object.ReferenceEquals(other, null) && Query == other.Query;
            public override int GetHashCode() => Query.GetHashCode();

            public override string ToString() => Title ?? Query;
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

            // override equality check for things like FirstOrDefault
            public static bool operator ==(CachedQuery? a, CachedQuery? b) => a?.Equals(b) == true;
            public static bool operator !=(CachedQuery? a, CachedQuery? b) => !a?.Equals(b) == true;
            public override bool Equals(object? obj) => obj != null && Equals(obj as CachedQuery);
            public bool Equals(CachedQuery? other) => !object.ReferenceEquals(other, null) && Query == other.Query;
            public override int GetHashCode() => Query.GetHashCode();

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
            if (cached != null)
                // to list required because history tampers with the object & it's a reference so history items build up
                return cached.Suggestions.ToList();

            List<Suggestion> suggestions = [];
            string url = $"https://www.google.com/complete/search?output=chrome&q={HttpUtility.UrlEncode(query)}";

            // start other suggestions going while main ones are
            Task<List<Suggestion>>? otherSuggestions = null;
            Task<List<Suggestion>>? definitions = null;

            // get Google.com & definitions suggestions (non URLs) before starting the other one bcuz in my mind the network request can be parallelised
            if (suggestionTypes.Contains(SuggestionType.KnowledgePanel))
                otherSuggestions = GetQueriesAdditionalAsync(query: query, cancellationToken: cancellationToken);

            if (suggestionTypes.Contains(SuggestionType.Definition))
                definitions = GetDefinitionsAsync(query: query, maxCount: 2, cancellationToken: cancellationToken);

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

                    // add knowledge panels
                    if (otherSuggestions != null) // && suggestionTypes.Contains(SuggestionType.KnowledgePanel)
                        foreach (var specialSuggestion in otherSuggestions.Result)
                        {
                            int index = suggestions.FindIndex(item => item == specialSuggestion);

                            // replace non-special with special if present
                            if (index != -1)
                                suggestions[index] = specialSuggestion;

                            // also add if query
                            if (specialSuggestion.Query == query)
                                suggestions.Insert(index: 0, item: specialSuggestion);
                        }
                    

                    if (definitions != null) // && suggestionTypes.Contains(SuggestionType.Definition)
                        foreach (var definitionSuggestion in definitions.Result)
                        {
                            int index = suggestions.FindIndex(item => QueryToDefinitionPhrase(item.Query) == definitionSuggestion.Query);
                            
                            // skip failed attempts
                            if (index == -1 && definitionSuggestion.Query != query) continue;

                            //definitionSuggestion.Query = suggestions[index].Query;

                            // add if query
                            if (definitionSuggestion.Query == query)
                                suggestions.Insert(index: 0, item: definitionSuggestion);

                            // replace non-special with special if present (and not already used)
                            else if (index != -1)
                                suggestions[index] = definitionSuggestion;
                        }

                    // promote definitions to top
                    suggestions = suggestions.OrderByDescending(item => item.Type == SuggestionType.Definition).ToList();

                    // add to cache
                    CachedQueries.Add(new()
                    {
                        Query = query,
                        Suggestions = suggestions
                    });

                }
                catch (Exception ex) { Debug.WriteLine($"ERROR: {ex}"); }

            // to list required because history tampers with the object & it's a reference so history items build up
            return suggestions.ToList();
        }

        #region definitions

        // [{"word":"tree trunk","score":291087,"tags":["pron:T R IY1 T R AH1 NG K "],"defs":["n\t(botany) The main structural member of a tree. "]}]
        record DatamuseDefinition(string word, int score, string[] tags, string[]? defs)
        {
            public Suggestion? ToSuggestion()
            {
                // get pronounciation
                string pronounciation = this.tags.FirstOrDefault(text => text.StartsWith("ipa_pron:", StringComparison.CurrentCulture), "ipa_pron:").Replace("ipa_pron:", "");
                if (!string.IsNullOrEmpty(pronounciation))
                {
                    pronounciation = $"/{pronounciation}/";
                }

                Suggestion sug = new Suggestion()
                {
                    Query = this.word,
                    Type = SuggestionType.Definition,
                    IsURL = false,

                    Title = string.IsNullOrEmpty(this.word) ? null : $"{this.word} {pronounciation}",
                    Description = this.defs?[0].Split('\t', StringSplitOptions.TrimEntries)[1]
                };

                // only return if good
                if (string.IsNullOrEmpty(sug.Title) || string.IsNullOrEmpty(sug.Description))
                    return null;
                return sug;
            }
        }

        private record CachedDatamuseDefinition(string query, DatamuseDefinition[] results);

        private static List<CachedDatamuseDefinition> cachedDefinitionQueries = [];

        public virtual async Task<List<Suggestion>> GetDefinitionsAsync(string query, int maxCount = 5, CancellationToken cancellationToken = default)
        {
            query = QueryToDefinitionPhrase(query) ?? "";
            
            // remove bad queries
            if (string.IsNullOrEmpty(query))
                return [];


            // use known query results
            List<DatamuseDefinition> results = GetCachedSimilarDefinitions(query, maxCount);

            if (results.Count >= maxCount)
                return [.. results.Select(item => item.ToSuggestion()).Where(item => item is not null)];


            // fill with new results
            maxCount -= results.Count;

            string url = $"https://api.datamuse.com/words?md=rd&ipa=1&max={maxCount}&sp={HttpUtility.UrlEncode(query)}";

            try
            {
                // get new from API
                HttpContent response = (await client.GetAsync(url, cancellationToken)).Content;
                string resp = (await response.ReadAsStringAsync(cancellationToken));
                results.AddRange(JsonConvert.DeserializeObject<DatamuseDefinition[]>(resp) ?? []);

                // cache
                cachedDefinitionQueries.Add(new(query, results.ToArray()));
                
            }
            catch (Exception ex) { Debug.WriteLine($"ERROR: {ex}"); }

            // return old + new (if available)
            return [.. results.Select(item => item.ToSuggestion()).Where(item => item is not null)];
        }

        private static List<DatamuseDefinition> GetCachedSimilarDefinitions(string word, int maxCount = 5)
        {
            IEnumerable<DatamuseDefinition> cachedDefinitions = cachedDefinitionQueries.SelectMany(cacheItem => cacheItem.results);

            List<DatamuseDefinition> results = [];

            if (cachedDefinitionQueries.Count > 0)
            {
                foreach (var cachedDef in cachedDefinitions)
                {
                    if (Levenshtein.ComputeDistanceLevenshtein(word, cachedDef.word) < 2)
                        results.Add(cachedDef);

                    if (results.Count > maxCount)
                        break;
                }
            }

            return results;
        }


        private static string? QueryToDefinitionPhrase(string query)
        {
            if (query.StartsWith("def", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(3);
            else if (query.StartsWith("define", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(6);
            else if (query.StartsWith("what is", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(7);
            else if (query.StartsWith("what was", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(8);
            else if (query.StartsWith("what does", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(9);
            else if (query.StartsWith("meaning of", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(10);

            if (query.EndsWith("mean", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(0, query.Length - 4);
            else if (query.EndsWith("means", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(0, query.Length - 5);
            else if (query.EndsWith("meaning", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(0, query.Length - 7);
            else if (query.EndsWith("definition", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(0, query.Length - 11);
            else if (query.EndsWith("define", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(0, query.Length - 6);
            else if (query.EndsWith("def", StringComparison.CurrentCultureIgnoreCase))
                query = query.Substring(0, query.Length - 3);

            query = query.Trim();

            if (string.IsNullOrEmpty(query)) return null;

            return query;
        }

        #endregion definitions

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

                    // definitions have been fully removed by Google for quite some time now :(
                    //// definitions
                    //if (suggestion.LastOrDefault().TryGetProperty("ansa", value: out JsonElement definition))
                    //    suggestions.Add(ParseDefinition(definition));

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

            // TODO: fix this not getting the subtext for "OpenAI"

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

            // allow IP addresses
            // DnsSafeHost will extend unfinished ip addresses (e.g., 10.0 => 10.0.0.0)
            if (url.Contains(host) && IsValidIP(host))
            {
                return true;
            }

            // The TLD (last part) should not be numeric and should generally be at least two letters long.
            string tld = hostParts.Last();
            if (tld.Length < 2 || tld.Any(char.IsDigit))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a IP string is valid <br>
        /// FROM: [https://stackoverflow.com/a/78374757]
        /// </summary>
        /// <param name="ip">The IP to validate</param>
        /// <returns>True if the IP is valid, false otherwise</returns>
        public static bool IsValidIP(string ipAddress)
        {
            return IPAddress.TryParse(ipAddress, out IPAddress? ip) && ip.ToString() == ipAddress;
        }

    }
}
