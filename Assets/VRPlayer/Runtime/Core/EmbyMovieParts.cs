using System;
using System.Collections.Generic;
using System.Globalization;
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

        public EmbyPartResume ProgressFor(EmbyItem item, Func<string, PlaybackRecord> findRecord = null)
        {
            if (item == null || IndexOf(item.Id) < 0) return null;
            string uri = EmbyIdentity.Create(AccountId, item.Id);
            var record = findRecord?.Invoke(uri);
            if (record?.uri != uri) record = null;
            DateTimeOffset serverTime = PlayedTime(item.UserData?.LastPlayedDate), localTime = PlayedTime(record?.lastPlayed);
            long position = Math.Max(0, item.UserData?.PlaybackPositionTicks ?? 0);
            if (localTime > serverTime) position = Math.Min(Math.Max(0, record.position), long.MaxValue / 10000) * 10000;
            if (item.RunTimeTicks > 0) position = Math.Min(position, item.RunTimeTicks);
            return new EmbyPartResume(item, position, localTime > serverTime ? localTime : serverTime);
        }

        public EmbyPartResume LastPlayed(Func<string, PlaybackRecord> findRecord = null)
        {
            EmbyPartResume latest = null, onlyProgress = null;
            int progressCount = 0;
            foreach (var item in Items)
            {
                var candidate = ProgressFor(item, findRecord);
                if (candidate.HasLastPlayed && (latest == null || candidate.LastPlayedAt > latest.LastPlayedAt)) latest = candidate;
                if (candidate.PositionTicks > 0) { onlyProgress = candidate; progressCount++; }
            }
            // A sole saved position is usable without dates, but multiple positions do not identify the last CD.
            return latest ?? (progressCount == 1 ? onlyProgress : null);
        }

        private static DateTimeOffset PlayedTime(string value)
            => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                ? time.ToUniversalTime() : DateTimeOffset.MinValue;
    }

    public sealed class EmbyPartResume
    {
        public EmbyItem Item { get; }
        public long PositionTicks { get; }
        public DateTimeOffset LastPlayedAt { get; }
        public bool HasLastPlayed => LastPlayedAt != DateTimeOffset.MinValue;
        public EmbyPartResume(EmbyItem item, long positionTicks, DateTimeOffset lastPlayedAt)
        { Item = item; PositionTicks = Math.Max(0, positionTicks); LastPlayedAt = lastPlayedAt; }
    }
}
