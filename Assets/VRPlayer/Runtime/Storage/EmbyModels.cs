using System;
using System.IO;

namespace VRPlayer
{
    [Serializable] public sealed class EmbyAccount
    {
        public string id, name, serverUrl, serverId, userId, username;
        [NonSerialized] public string accessToken;
        public bool remember = true;
    }

    [Serializable] public sealed class EmbyItemsResult
    {
        public EmbyItem[] Items = Array.Empty<EmbyItem>();
        public int TotalRecordCount, StartIndex;
    }

    [Serializable] public sealed class EmbyItem
    {
        public string Id, Name, Type, ParentId, Overview, OfficialRating, SeriesName, Path, DateModified;
        public string CollectionType, ExtraType, SortName, PremiereDate, EndDate;
        public string SeriesId, SeasonId, SeasonName, SeriesPrimaryImageTag;
        public bool IsFolder;
        public int IndexNumber = -1;
        public int ProductionYear, ParentIndexNumber, IndexNumberEnd, PartCount, SpecialFeatureCount, LocalTrailerCount;
        public float CommunityRating, PrimaryImageAspectRatio;
        public long RunTimeTicks, Size;
        public EmbyPerson[] People = Array.Empty<EmbyPerson>();
        public string[] Genres = Array.Empty<string>(), Tags = Array.Empty<string>(), ProductionLocations = Array.Empty<string>();
        public EmbyNamedItem[] Studios = Array.Empty<EmbyNamedItem>(), GenreItems = Array.Empty<EmbyNamedItem>(), TagItems = Array.Empty<EmbyNamedItem>();
        public EmbyImageTags ImageTags = new EmbyImageTags();
        public string[] BackdropImageTags = Array.Empty<string>();
        public EmbyUserData UserData = new EmbyUserData();
        public EmbyMediaSource[] MediaSources = Array.Empty<EmbyMediaSource>();
        public bool IsPlayable => Type == "Movie" || Type == "Video" || Type == "Episode" || Type == "Trailer" || Type == "MusicVideo";
        public bool IsAdditionalPart => string.Equals(ExtraType, "AdditionalPart", StringComparison.OrdinalIgnoreCase);
        public bool IsExtra => !IsAdditionalPart && (!string.IsNullOrEmpty(ExtraType) || Type == "Trailer");
        public bool IsMovieLibrary => string.Equals(CollectionType, "movies", StringComparison.OrdinalIgnoreCase);
        public bool IsBrowsable => !IsPlayable && (IsFolder || Type == "Series" || Type == "Season" || Type == "BoxSet" ||
            Type == "CollectionFolder" || Type == "Playlist" || Type == "Folder" || Type == "UserView");
    }

    [Serializable] public sealed class EmbyPerson
    {
        public string Id, Name, Role, Type, PrimaryImageTag;
    }

    [Serializable] public sealed class EmbyNamedItem
    {
        public long Id;
        public string Name;
    }

    [Serializable] public sealed class EmbyImageTags
    {
        public string Primary, Thumb, Banner, Disc, Logo;
    }

    [Serializable] public sealed class EmbyUserData
    {
        public long PlaybackPositionTicks;
        public string LastPlayedDate;
        public double PlayedPercentage;
        public bool Played, IsFavorite;
    }

    [Serializable] public sealed class EmbyMediaSource
    {
        public string Id, Name, Path, Protocol, Container, DateModified, Video3DFormat, DirectStreamUrl;
        public long Size, RunTimeTicks;
        public bool SupportsDirectPlay, SupportsDirectStream, IsInfiniteStream, RequiresOpening;
        public EmbyMediaStream[] MediaStreams = Array.Empty<EmbyMediaStream>();
    }

    [Serializable] public sealed class EmbyMediaStream
    {
        public int Index, Width, Height;
        public string Type, Codec, Language, DisplayTitle, DeliveryUrl;
        public bool IsExternal, IsTextSubtitle;
    }

    public sealed class EmbyPlayback
    {
        public EmbyItem item;
        public EmbyMediaSource source;
        public string url, token, playSessionId, playMethod = "DirectStream";
        public long startPositionMs;
        public string SourceFileName
        {
            get
            {
                string path = source?.Path ?? item?.Path;
                if (string.IsNullOrEmpty(path)) return item?.Name ?? "";
                // Emby may serve Windows paths to an Android client.
                path = path.Replace('\\', '/');
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")) path = uri.AbsolutePath;
                return Uri.UnescapeDataString(Path.GetFileName(path));
            }
        }
    }

    public static class EmbyIdentity
    {
        public static string Create(string accountId, string itemId)
        {
            if (!ValidPart(accountId) || !ValidPart(itemId)) throw new ArgumentException("Emby 媒体标识无效");
            return "emby://" + Uri.EscapeDataString(accountId) + "/" + Uri.EscapeDataString(itemId);
        }

        public static bool Parse(string value, out string accountId, out string itemId) => TryParse(value, out accountId, out itemId);

        public static bool TryParse(string value, out string accountId, out string itemId)
        {
            accountId = itemId = "";
            if (string.IsNullOrEmpty(value) || !value.StartsWith("emby://", StringComparison.OrdinalIgnoreCase)) return false;
            string body = value.Substring(7);
            int slash = body.IndexOf('/');
            if (slash <= 0 || slash != body.LastIndexOf('/') || body.IndexOfAny(new[] { '?', '#' }) >= 0) return false;
            try
            {
                string account = Uri.UnescapeDataString(body.Substring(0, slash));
                string item = Uri.UnescapeDataString(body.Substring(slash + 1));
                if (!ValidPart(account) || !ValidPart(item)) return false;
                accountId = account; itemId = item;
                return true;
            }
            catch (UriFormatException) { return false; }
        }

        private static bool ValidPart(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "." || value == "..") return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')) return false;
            return true;
        }
    }
}
