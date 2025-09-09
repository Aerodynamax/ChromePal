using Microsoft.CommandPalette.Extensions.Toolkit;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static GooglePal.Google;

namespace GooglePal.Components
{
    internal class SuggestionList
    {
        // suggestions
        public List<ListItem> Items = new();


        private List<ListItem> Above = new();
        private ListItem       Main  = new();
        private List<ListItem> Below = new();

        //private ListItem TopMost = new();


        private Action UpdateUI;

        private Func<Suggestion, ListItem> CreateListItem;

        private ActionReturn<List<ListItem>> CreateDefaultPage;

        // cancel moving Above to top updates
        private CancellationTokenSource CancelUpdate = new();

        // general delegate for actions that return a value
        public delegate TResult ActionReturn<TResult>();

        public SuggestionList(Func<Suggestion, ListItem> createListItem, Action updateUI, ActionReturn<List<ListItem>> createDefaultPage)
        {
            UpdateUI = updateUI;
            CreateListItem = createListItem;
            CreateDefaultPage = createDefaultPage;

            Main.Title = "Loading ...";

            Items = [.. Above, Main, .. Below];
        }

        public void Update(string query)
        {
            // cancel any pending text suggestion updates
            CancelUpdate.Cancel();
            CancelUpdate = new CancellationTokenSource();

            // show default page
            if (string.IsNullOrEmpty(query))
            {
                var items = CreateDefaultPage.Invoke();

                Items = items;

                UpdateUI.Invoke();

                return;
            }

            // remove gittering
            if (Above.Count > 0) Above[0].TextToSuggest = query;

            Main.Title = query;
            Main.TextToSuggest = query;

            // if Above is still equal keep on top
            Items = [Main, .. Above, .. Below];

            UpdateUI.Invoke();

            // get other properties (setting Main directly hard refreshes the UI)
            ListItem copyFrom = CreateListItem.Invoke(CreateSuggestion(query));

            Main.Icon = copyFrom.Icon;
            Main.Subtitle = copyFrom.Subtitle;
            Main.Command = copyFrom.Command;
            Main.MoreCommands = copyFrom.MoreCommands;
        }

        public void Update(string query, ListItem[] above, ListItem[] below)
        {
            // remove gittering
            if (above.Length > 0) above[0].TextToSuggest = query;

            Above = above.ToList();
            Below = below.ToList();

            Items = [Main, .. Above, .. Below];

            UpdateUI.Invoke();

            if (Above.Count == 0) return;

            Task.Run(async () =>
            {
                ListItem[] preItems = new ListItem[Items.Count];
                Items.CopyTo(0, preItems, 0, Items.Count);

                // wait for no user input
                await Task.Delay(500);

                // get the item whose text suggest was changed
                ListItem? first = Above.FirstOrDefault(item => item.TextToSuggest == query);

                // if UI hasn't changed
                if (first != null && Main.Title == query &&
                    preItems.SequenceEqual(Items) &&
                    preItems.SequenceEqual([Main, .. Above, .. Below])
                    )
                {
                    // TODO: SWITCH TO CHANGING MAIN DIRECTLY TO FIX/REDUCE ANY OTHER JITTERING
                    // i don't know why this works
                    ListItem top = Items[0];

                    Items.RemoveAt(0);

                    Items[0].TextToSuggest = Items[0].Title;

                    Items.Insert(1, top);

                    Items = Items.DistinctBy(item => item.Title).ToList();

                    UpdateUI.Invoke();
                }
            }, CancelUpdate.Token);
        }
    }
}
