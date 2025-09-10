using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static GooglePal.Google;

namespace GooglePal.Components
{
    partial class SuggestionList
    {
        // suggestions
        public ListItem[] Items = new ListItem[6];

        private int ItemsLength = 6;

        private string currQuery = string.Empty;

        private ListItem Query = new();

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

            Items = new ListItem[length];
            ItemsLength = length;
        }

        public void Update(string query)
        {
            currQuery = query;

            // show default page
            if (string.IsNullOrEmpty(query))
            {
                ListItem[] defaultItems = CreateDefaultPage.Invoke();

                // truncate
                if (defaultItems.Length > ItemsLength) defaultItems = defaultItems.Take(ItemsLength).ToArray();

                // hard refresh
                Items = defaultItems;

                UpdateUI.Invoke();

                return;
            }

            // don't update if top result is query
            if (Items.FirstOrDefault()?.Title == query) return;

            int queryPosition = 0;

            // if the important suggestion is still valid, keep it
            if (Items.FirstOrDefault()?.Title != Query.Title && 
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

        public void Update(string query, ListItem? above, ListItem[] below)
        {
            if (string.IsNullOrEmpty(query)) return;

            // only keep suggestions that will be rendered
            if (below.Length > ItemsLength - 1) below = below.Take(ItemsLength - 1).ToArray();


            // add any missing indicies
            while (Items.Length < ItemsLength)
                Items = Items.Append(new()).ToArray();

            int updatePosition = 1;

            // update suggestions
            foreach (var suggestion in below.Index())
            {
                // skip out of range
                if (suggestion.Index >= ItemsLength - 1) continue;

                // speed through if out of date
                if (query != currQuery)
                    // too fast normally, causes biting
                    Items[suggestion.Index + 1] = suggestion.Item;

                // update text
                UpdateListItemSilent(index: suggestion.Index + 1, newItem: suggestion.Item);

                updatePosition = updatePosition + 2;
            }

            // remove any ListItems that wheren't updated
            Items = Items.Take(updatePosition).ToArray();

            // add above as top result
            if (above != null)
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
            // return if invalid location
            if (index > Items.Length - 1 || index < 0) return;

            if (Items[index] == null)
            {
                Items[index] = newItem;
                return;
            }

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
    }
}
