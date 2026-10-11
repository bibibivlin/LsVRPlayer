using System;
using System.Collections.Generic;

namespace VRPlayer
{
    // Membership comes from the main item's official SpecialFeatures/LocalTrailers responses.
    public sealed class EmbyMovieExtras
    {
        public string AccountId { get; }
        public string MainId { get; }
        public IReadOnlyList<EmbyItem> Items { get; }

        private EmbyMovieExtras(string accountId, string mainId, EmbyItem[] items)
        {
            AccountId = accountId; MainId = mainId; Items = Array.AsReadOnly(items);
        }

        public static EmbyMovieExtras Create(string accountId, string mainId, IEnumerable<EmbyItem> extras)
        {
            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(mainId)) return null;
            var items = new List<EmbyItem>();
            var ids = new HashSet<string>(StringComparer.Ordinal) { mainId };
            foreach (var extra in extras ?? Array.Empty<EmbyItem>())
                if (extra != null && extra.IsPlayable && !extra.IsAdditionalPart && !string.IsNullOrEmpty(extra.Id) && ids.Add(extra.Id)) items.Add(extra);
            return items.Count > 0 ? new EmbyMovieExtras(accountId, mainId, items.ToArray()) : null;
        }

        public static EmbyMovieExtras FromDetails(string accountId, EmbyBrowserState state)
            => state?.page == EmbyPage.Details && !state.loading && state.selected != null
                ? Create(accountId, state.selected.Id, state.extras) : null;

        public int IndexOf(string itemId)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id == itemId) return i;
            return -1;
        }

        public int IndexOfUri(string uri)
            => EmbyIdentity.TryParse(uri, out string accountId, out string itemId) && accountId == AccountId ? IndexOf(itemId) : -1;
    }
}
