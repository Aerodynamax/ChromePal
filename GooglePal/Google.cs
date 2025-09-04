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
using static System.Net.Mime.MediaTypeNames;

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

        public partial class Suggestions
        {
            public required string Query;

            public required List<Suggestion> suggestions;
        }

        private HttpClient client = new();


        // [ https://github.com/Daydreamer-riri/CmdPal-WebSearchShortcut/blob/main/CmdPalWebSearchShortcut/WebSearchShortcut/SuggestionsProviders/Google.cs ]
        public virtual async Task<Suggestions> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            List<Suggestion> suggestions = [];
            string url = $"https://www.google.com/complete/search?output=chrome&q={HttpUtility.UrlEncode(query)}";

            try
            {
                await using var response = await client.GetStreamAsync(url, cancellationToken);

                // deserialise
                var json = await JsonDocument.ParseAsync(response, cancellationToken: cancellationToken);

                var suggestionQueries = json.RootElement.EnumerateArray().ElementAt(1);
                var suggestionType = json.RootElement.EnumerateArray().ElementAt(4).GetProperty("google:suggesttype");

                // add query as suggestion
                bool queryIsUrl = Google.IsValidURL(query);

                suggestions.Add(
                    new Suggestion()
                    {
                        Query = query,
                        Type = queryIsUrl ? Google.SuggestionType.Website : Google.SuggestionType.Query,
                        IsURL = queryIsUrl
                    }
                );


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
                foreach ( var _query in queryTitles.Index())
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

                    suggestions.Add(new Suggestion() {
                        Query = queryText,
                        Type = type,
                        IsURL = type == SuggestionType.Website
                    });
                }

            }
            catch (Exception ex) { Debug.WriteLine($"ERROR: {ex}"); }

            return new Suggestions() { Query = query, suggestions = suggestions };
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
            if (!urlToParse.Contains( "://")) urlToParse = "http://" + urlToParse;

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
