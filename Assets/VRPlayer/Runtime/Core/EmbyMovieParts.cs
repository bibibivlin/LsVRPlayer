using System;
using System.Collections.Generic;
using System.Linq;

namespace VRPlayer
{
    // A server-defined multipart movie is independent of the currently browsed library page.
    public sealed class EmbyMovieParts
    {
        public string AccountId { get; }
        public string MainId { get; }
        public IReadOnlyList<EmbyItem> Items { get; }

        private EmbyMovieParts(string accountId, EmbyItem main, EmbyItem[] items)
        {
            AccountId = accountId; MainId = main.Id; Items = Array.AsReadOnly(items);
        }

        public static EmbyMovieParts Create(string accountId, EmbyItem main, IEnumerable<EmbyItem> additional)
        {
            if (string.IsNullOrEmpty(accountId) || main == null || string.IsNullOrEmpty(main.Id) || main.IsBrowsable || main.IsExtra) return null;
            var items = new List<EmbyItem> { main };
            var ids = new HashSet<string>(StringComparer.Ordinal) { main.Id };
            foreach (var part in additional ?? Array.Empty<EmbyItem>())
                if (part != null && !part.IsBrowsable && !part.IsExtra && !string.IsNullOrEmpty(part.Id) && ids.Add(part.Id)) items.Add(part);
            return items.Count > 1 ? new EmbyMovieParts(accountId, main, items.ToArray()) : null;
        }

        public static EmbyMovieParts FromDetails(string accountId, EmbyBrowserState state)
            => state?.page == EmbyPage.Details && !state.loading && state.selected != null
                ? Create(accountId, state.selected, state.parts) : null;

        public int IndexOf(string itemId)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id == itemId) return i;
            return -1;
        }

        public int IndexOfUri(string uri)
            => EmbyIdentity.TryParse(uri, out string accountId, out string itemId) && accountId == AccountId ? IndexOf(itemId) : -1;

        public bool SameMovie(EmbyMovieParts other) => other != null && AccountId == other.AccountId && MainId == other.MainId;
    }
}
