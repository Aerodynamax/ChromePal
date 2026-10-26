using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Windows.ApplicationModel.VoiceCommands;
using Windows.Devices.Sms;
using static GooglePal.Google;

namespace GooglePal.Components
{
    partial class SuggestionList
    {
        // suggestions
        public ListItem[] Items = [];

        private int ItemsLength = 6;

        private string currQuery = string.Empty;

        private ListItem Query = new();

        private ListItem[] QueryResults = [];
        private ListItem? QueryAbove    = new();

        // functions
        private Action UpdateUI;

        private Func<Suggestion, ListItem> CreateListItem;

        private ActionReturn<ListItem[]> CreateDefaultPage;

        // general delegate for actions that return a value
        public delegate TResult ActionReturn<TResult>();

        public SuggestionList(int length, Func<Suggestion, ListItem> createListItem, Action updateUI, ActionReturn<ListItem[]> createDefaultPage)
        {
            UpdateUI = updateUI;
            CreateListItem = createListItem;
            CreateDefaultPage = createDefaultPage;

            //Items = new ListItem[length];
            ItemsLength = length;
        }
        public void UpdateLength(int length)
        {
            ItemsLength = length;

            // refresh UI
            UpdateSuggestions(query: currQuery, above: QueryAbove, below: QueryResults);
        }

        public void UpdateHomePage()
        {
            ListItem[] items = CreateDefaultPage.Invoke()
                                    .Where(item => item != null)
                                    .ToArray();

            // set length to what is required
            if (Items.Length < items.Length)
                Items = [.. Items, .. new ListItem[items.Length - Items.Length]];

            else if (Items.Length > items.Length)
                Items = [.. Items.Take(items.Length)];


            // update values
            foreach (var item in items.Index())
            {
                // soft update current items
                if (Items.Length > item.Index)
                {
                    if (Items[item.Index] == null)
                        Items[item.Index] = item.Item;

                    else if (!CompareListItem(index: item.Index, newItem: item.Item))
                        UpdateListItemSilent(index: item.Index, newItem: item.Item);
                }
            }

            UpdateUI.Invoke();
        }

        public void UpdateQuery(string query)
        {
            currQuery = query;

            // reset suggestions size if empty
            Items = new ListItem[ItemsLength];

            // show default page
            if (string.IsNullOrEmpty(query))
            {
                // hard refresh
                UpdateHomePage();

                UpdateUI.Invoke();

                return;
            }

            // don't update if top result is query
            if (Items.FirstOrDefault()?.Title == query) return;

            int queryPosition = 0;

            // if the important suggestion is still valid, keep it
            if (Items.FirstOrDefault()?.Title != Query.Title && 
                Items.FirstOrDefault()?.Title.Length > Query.Title.Length &&
                Levenshtein.startsWithLevenshtein(query, Items.FirstOrDefault()?.Title ?? "") == 0
                )
            {
                queryPosition = 1;
            }

            // get other properties (setting Main directly hard refreshes the UI)
            Query = CreateListItem.Invoke(CreateSuggestion(query));

            // soft refesh to prevent biting
            UpdateListItemSilent(index: queryPosition, newItem: Query);

            // update the UI (in theory..)
            UpdateUI.Invoke();
        }

        public void UpdateSuggestions(string query, ListItem? above, ListItem[] below)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(currQuery)) return;

            // store (in case of refreshes)
            QueryResults = below;
            QueryAbove = above;

            // only keep suggestions that will be rendered
            if (below.Length > ItemsLength - 1)
                below = below.Take(ItemsLength - 1).ToArray();

            // reset items list length
            while (Items.Length < ItemsLength)
                Items = Items.Append(new ListItem()).ToArray();

            if (Items.Length > ItemsLength)
                Items = Items.Take(ItemsLength).ToArray();


            // update suggestions
            foreach (var suggestion in below.Index())
            {
                // skip out of range
                if (suggestion.Index >= Items.Length - 1)
                    continue;

                // update text
                Items[suggestion.Index + 1] = suggestion.Item;
            }

            // remove non items
            if (above != null)
                Items = Items.Take(below.Length + 2).ToArray();
            else
                Items = Items.Take(below.Length + 1).ToArray();
            
            //Items = Items.Where(item => item != null).ToArray();

            // add above as top result if we're doing that
            if (above != null && ItemsLength > 1 && query == currQuery)
            {
                // add user query if not exact match
                if (Query.Title != above.Title)
                    below = below.Prepend(Query).ToArray();

                // add above to suggestions
                below = below.Prepend(above).ToArray();

                // truncate length
                below = below.Take(ItemsLength).ToArray();

                // return if not current
                if (query != currQuery) return;

                // hard refresh
                Items = below;

            }

            // update suggestions
            UpdateUI.Invoke();
        }

        public void ClearSuggestions()
        {
            Items = [];
            UpdateUI.Invoke();
        }

        /// <summary>
        ///     Update an item in the list without hard changing it's properties.
        ///     <br /><br />
        ///     Updates done this way won't cause new text suggestions to display <br />
        ///     or move the user from their ListItem (that is, their position will 
        ///     update to wherever the box lands).
        /// </summary>
        /// <param name="index">The position of the item to be overwritten.</param>
        /// <param name="newItem">The item that will replace the current one.</param>
        private void UpdateListItemSilent(int index, ListItem newItem)
        {
            if (Items == null) return;
            if (newItem == null)
                return;

            // return if invalid location
            if (index > Items.Length - 1 || index < 0) return;

            Items[index] ??= new ListItem();

            //if (Items[index] == null)
            //    Items[index] ??= new ListItem();

            // update properties individually
            Items[index].Title = newItem.Title;
            Items[index].Subtitle = newItem.Subtitle;
            Items[index].TextToSuggest = newItem.TextToSuggest;
            Items[index].Command = newItem.Command;
            Items[index].MoreCommands = newItem.MoreCommands;
            Items[index].Icon = newItem.Icon;
            Items[index].Section = newItem.Section;
            Items[index].Details = newItem.Details;
            Items[index].Tags = newItem.Tags;
        }


        /// <summary>
        ///     Compare an item to one at a given index
        /// </summary>
        /// <param name="index">The position of the item to be compared against.</param>
        /// <param name="newItem">The item that will be compared too.</param>
        /// <returns>Bool that is true if the items are equal and false if they aren't</returns>
        private bool CompareListItem(int index, ListItem newItem)
        {
            // return if invalid location
            if (index > Items.Length - 1 || index < 0) return false;

            if (Items[index] == null)
                Items[index] = new();

            // update properties individually
            if (Items[index].Title != newItem.Title) return false;
            if (Items[index].Subtitle != newItem.Subtitle) return false;
            if (Items[index].TextToSuggest != newItem.TextToSuggest) return false;
            if (Items[index].Command != newItem.Command) return false;
            if (Items[index].MoreCommands != newItem.MoreCommands) return false;
            if (Items[index].Icon != newItem.Icon) return false;
            if (Items[index].Section != newItem.Section) return false;
            if (Items[index].Details != newItem.Details) return false;
            if (Items[index].Tags != newItem.Tags) return false;

            return true;
        }
    }
}
