using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Windows.System;
using Windows.System.Profile;
using static GooglePal.Chrome;

namespace GooglePal.Components
{
    internal partial class SearchItem : ListItem
    {

        // icons
        public static IconInfo SearchIcon = new IconInfo("\uE721");
        public static IconInfo VisitIcon = new IconInfo("\uE774");
        public static IconInfo HistoryIcon = new IconInfo("\uE81C");
        public static IconInfo CalculatorIcon = new IconInfo("\uE8EF");

        private Action<string> SearchCommand;

        public SearchItem(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered, Action<string> searchCommand)
        {
            if (searchItem == null) throw new ArgumentNullException();

            SearchCommand = searchCommand;

            switch (searchItem.Type)
            {
                case Google.SuggestionType.Website:
                    InitLink(searchItem: searchItem, profilesOrdered: profilesOrdered); break;

                case Google.SuggestionType.Calculation:
                    InitCalc(searchItem: searchItem, profilesOrdered: profilesOrdered); break;

                default:
                    InitLink(searchItem: searchItem, profilesOrdered: profilesOrdered); break;
            }

            if (searchItem.Type == Google.SuggestionType.Query) InitLink(searchItem, profilesOrdered);
        }

        private void InitLink(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered)
        {
            Title = searchItem.Query;
            Subtitle = $"{(searchItem.IsURL ? "Visit" : "Search")} as: {profilesOrdered[0].Name}";
            
            // slows UI down in latest update (cuz it's fixed now)
            TextToSuggest = searchItem.Query;

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

            // add secondary commands
           MoreCommands = moreCommands;

            // create main command
            Command = new AnonymousCommand(() => SearchCommand(profilesOrdered[0].Name))
            {
                Name = $"{(searchItem.IsURL ? "Visit" : "Search")} ({profilesOrdered[0].Name})",
                Icon = icon
            };
        }

        private void InitCalc(Google.Suggestion searchItem, List<Chrome.Profile> profilesOrdered)
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
