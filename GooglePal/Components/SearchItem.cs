using ABI.Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Windows.System;

namespace GooglePal.Components
{
    internal partial class SearchItem : ListItem
    {

        // icons
        public static IconInfo SearchIcon = new IconInfo("\uE721");
        public static IconInfo VisitIcon = new IconInfo("\uE774");
        public static IconInfo HistoryIcon = new IconInfo("\uE81C");
        public static IconInfo CalculatorIcon = new IconInfo("\uE8EF");
        public static IconInfo DeleteIcon = new IconInfo("\uE74D");
        public static IconInfo CopyIcon = new IconInfo("\uE8C8");
        public static IconInfo DefinitionIcon = new IconInfo("\uE82D");
        public static IconInfo KnowledgeIcon = new IconInfo("\uE946");

        private Action<string> SearchCommand;

        private string? Verb;

        private bool SuggestText = true;

        public SearchItem(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered, Action<string> searchCommand, CommandContextItem[]? otherSubcommands = null)
            : this(
                  searchItem: searchItem,
                  profilesOrdered: profilesOrdered,
                  searchCommand: searchCommand,
                  otherSubcommands: otherSubcommands,
                  suggestText: true
            )
            { }

        public SearchItem(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered, Action<string> searchCommand, CommandContextItem[]? otherSubcommands = null, bool suggestText = true, string? verb = null)
        {
            Verb = verb;
            SearchCommand = searchCommand;

            SuggestText = suggestText;

            switch (searchItem.Type)
            {
                case Google.SuggestionType.Website:
                    InitLink(searchItem: searchItem, profilesOrdered: profilesOrdered, otherSubcommands: otherSubcommands); break;

                case Google.SuggestionType.Definition:
                    InitDefine(searchItem: searchItem, profilesOrdered: profilesOrdered, otherSubcommands: otherSubcommands); break;

                case Google.SuggestionType.KnowledgePanel:
                    InitSpecial(searchItem: searchItem, profilesOrdered: profilesOrdered, otherSubcommands: otherSubcommands); break;

                case Google.SuggestionType.Calculation:
                    InitCalc(searchItem: searchItem, profilesOrdered: profilesOrdered, otherSubcommands: otherSubcommands); break;

                default:
                    InitLink(searchItem: searchItem, profilesOrdered: profilesOrdered, otherSubcommands: otherSubcommands); break;
            }
        }

        private void InitLink(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered, CommandContextItem[]? otherSubcommands = null)
        {
            // set tilte (unless)
            Title = !string.IsNullOrEmpty(Verb) ? Verb : searchItem.Query;

            Subtitle = $"{(searchItem.IsURL ? "Visit" : "Search")} as: {profilesOrdered[0].Name}";

            // set suggestion text (unless custom "Open Chrome" button)
            if (SuggestText) TextToSuggest = searchItem.Query;

            // set icon to url or search
            // or history (takes priority)
            var icon = (searchItem.Type == Google.SuggestionType.History) ? HistoryIcon : (searchItem.IsURL) ? VisitIcon : SearchIcon;
            Icon = icon;

            CommandContextItem[] moreCommands = CreateCommands(
                searchItem: searchItem,
                profilesOrdered: profilesOrdered
            );

            // remove first
            if (moreCommands.Length > 0) moreCommands = moreCommands.Skip(1).ToArray();

            // make second profile ctrl+enter
            if (moreCommands.Length > 0) moreCommands[0].RequestedShortcut = KeyChordHelpers.FromModifiers(
                ctrl: true,
                vkey: (int)VirtualKey.Enter
            );


            // add secondary & provided commands
            MoreCommands = [.. moreCommands, .. otherSubcommands ?? []];

            // create main command
            Command = new AnonymousCommand(() => SearchCommand(profilesOrdered[0].Name))
            {
                Name = $"{(!string.IsNullOrEmpty(Verb) ? Verb : (searchItem.IsURL ? "Visit" : "Search"))} ({profilesOrdered[0].Name})",
                Icon = icon
            };
        }

        private void InitDefine(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered, CommandContextItem[]? otherSubcommands = null)
        {
            // based off query
            InitLink(searchItem: searchItem, profilesOrdered: profilesOrdered, otherSubcommands: otherSubcommands);

            Title = searchItem.Title ?? searchItem.Query;
            Subtitle = $"{(searchItem.IsURL ? "Visit" : "Search")} as: {profilesOrdered[0].Name} • {searchItem.Description}";
            TextToSuggest = searchItem.Query;
            Icon = DefinitionIcon;

        }

        private void InitSpecial(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered, CommandContextItem[]? otherSubcommands = null)
        {
            // based off query
            InitLink(searchItem: searchItem, profilesOrdered: profilesOrdered, otherSubcommands: otherSubcommands);


            Title = searchItem.Title ?? searchItem.Query;

            // add description if present
            if (!string.IsNullOrEmpty(searchItem.Description))
                Subtitle = $"{(searchItem.IsURL ? "Visit" : "Search")} as: {profilesOrdered[0].Name} - {searchItem.Description}";
            else
                Subtitle = $"{(searchItem.IsURL ? "Visit" : "Search")} as: {profilesOrdered[0].Name}";

            TextToSuggest = searchItem.Query;
            Icon = searchItem.Icon ?? KnowledgeIcon;

        }

        private void InitCalc(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered, CommandContextItem[]? otherSubcommands = null)
        {
            // remove "= " from result
            string copyText = searchItem.Query.TrimStart("= ".ToArray());
            string copySubtitle = $"Copy \"{copyText}\" to clipboard";


            Title = searchItem.Query;
            Subtitle = copySubtitle;
            TextToSuggest = copyText;

            Icon = CalculatorIcon;

            // create sub commands
            CommandContextItem[] moreCommands = CreateCommands(
                searchItem: searchItem,
                profilesOrdered: profilesOrdered
            );

            // make first profile ctrl+enter
            if (moreCommands.Length > 0) moreCommands[0].RequestedShortcut = KeyChordHelpers.FromModifiers(
                ctrl: true,
                vkey: (int)VirtualKey.Enter
            );

            // add user specified commands
            if (otherSubcommands != null && otherSubcommands.Length > 0) moreCommands = moreCommands.Concat(otherSubcommands).ToArray();

            // add secondary commands
            MoreCommands = moreCommands;

            // create main command
            Command = new CopyTextCommand(copyText)
            {
                Name = copySubtitle,
                Icon = CalculatorIcon
            };
        }

        private CommandContextItem[] CreateCommands(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered)
        {
            return profilesOrdered.Select(profile => CreateSubCommand(
                profile: profile,
                isURL: searchItem.IsURL,
                index: profilesOrdered.IndexOf(profile)
            )).ToArray();
        }

        private CommandContextItem CreateSubCommand(Chrome.Profile profile, bool isURL, int index)
        {
            return new CommandContextItem(
                name: isURL ? $"Visit ({profile.Name})" : $"Search ({profile.Name})",
                title: isURL ? $"Visit as: {profile.Name}" : $"Search as: {profile.Name}",
                action: () => SearchCommand(profile.Name)
            )
            {
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true,
                    vkey: (int)VirtualKey.Number1 + index
                ),
                Icon = new IconInfo(profile.ProfilePicturePath)
            };
        }

    }
}
