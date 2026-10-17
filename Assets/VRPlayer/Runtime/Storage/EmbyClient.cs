using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace VRPlayer
{
    public sealed class EmbyAuthenticationException : InvalidOperationException
    {
        public EmbyAuthenticationException() : base("Emby 登录已失效，请重新登录") { }
    }

    public sealed class EmbyClient
    {
        public const int PageSize = 30;
        private const int AllItemsPageSize = 200;
        private const int JsonByteLimit = 8 * 1024 * 1024;
        private const int SubtitleByteLimit = 4 * 1024 * 1024;
        private const string Fields = "Overview,People,Genres,Tags,Studios,ProductionLocations,MediaSources,ParentId,Path,DateModified,PrimaryImageAspectRatio,SortName";
        private readonly string deviceId;

        [Serializable] private sealed class AuthenticationRequest { public string Username, Pw; }
        [Serializable] private sealed class AuthenticationUser { public string Id, Name; }
        [Serializable] private sealed class AuthenticationResponse { public AuthenticationUser User; public string AccessToken, ServerId; }
        [Serializable] private sealed class PlaybackInfoResponse { public EmbyMediaSource[] MediaSources; public string PlaySessionId, ErrorCode; }
        [Serializable] private sealed class DirectPlayProfile { public string Type = "Video"; }
        [Serializable] private sealed class DeviceProfile
        {
            public string Name = "LsVRPlayer original video";
            public long MaxStreamingBitrate = 1000000000;
            public DirectPlayProfile[] DirectPlayProfiles = { new DirectPlayProfile() };
        }
        [Serializable] private sealed class PlaybackInfoRequest
        {
            public string UserId;
            public long StartTimeTicks, MaxStreamingBitrate = 1000000000;
            public bool EnableDirectPlay = true, EnableDirectStream = true, EnableTranscoding, IsPlayback = true;
            public DeviceProfile DeviceProfile = new DeviceProfile();
        }
        [Serializable] private sealed class PlaybackReport
        {
            public string ItemId, MediaSourceId, PlaySessionId, PlayMethod, EventName;
            public long PositionTicks;
            public bool IsPaused, CanSeek = true;
            public int PlaylistIndex, PlaylistLength = 1;
            public double PlaybackRate = 1;
        }

        public EmbyClient(string deviceId = null)
        {
            this.deviceId = SafeHeaderValue(string.IsNullOrEmpty(deviceId) ? SystemInfo.deviceUniqueIdentifier : deviceId);
        }

        public async Task<EmbyAccount> Authenticate(string serverUrl, string username, string password, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string server = NormalizeServerUrl(serverUrl);
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("请输入 Emby 用户名");
            string body = JsonUtility.ToJson(new AuthenticationRequest { Username = username.Trim(), Pw = password ?? "" });
            var response = await Send(server, "/Users/AuthenticateByName", null, "POST", body, token, Parse<AuthenticationResponse>);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(response?.AccessToken) || string.IsNullOrEmpty(response.User?.Id))
                throw new InvalidOperationException("Emby 登录响应无效，请检查服务器后重试");
            return new EmbyAccount
            {
                id = Guid.NewGuid().ToString("N"), serverUrl = server, serverId = response.ServerId ?? "",
                userId = response.User.Id, username = response.User.Name ?? username.Trim(),
                name = response.User.Name ?? username.Trim(), accessToken = response.AccessToken, remember = true
            };
        }

        public async Task<EmbyItemsResult> GetViews(EmbyAccount account, CancellationToken token = default)
            => await SendAccount(account, UserPath(account) + "/Views?Fields=" + Escape(Fields), "GET", null, token, ParseItems);

        public async Task<EmbyItemsResult> GetResume(EmbyAccount account, CancellationToken token = default)
            => await SendAccount(account, UserPath(account) + "/Items/Resume?Limit=" + PageSize +
                "&MediaTypes=Video&IncludeItemTypes=Movie,Episode,Video&Fields=" + Escape(Fields), "GET", null, token, ParseItems);

        public async Task<EmbyItemsResult> GetLatest(EmbyAccount account, string parentId = "", CancellationToken token = default)
        {
            string path = UserPath(account) + "/Items/Latest?Limit=" + PageSize + "&IncludeItemTypes=Movie,Episode,Video&Fields=" + Escape(Fields);
            if (!string.IsNullOrEmpty(parentId)) path += "&ParentId=" + Escape(parentId);
            return await SendAccount(account, path, "GET", null, token, ParseItems);
        }

        public async Task<EmbyItemsResult> GetItems(EmbyAccount account, string parentId, int startIndex = 0, string search = "", CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return await SendAccount(account, ItemsPath(account, parentId, startIndex, search, false), "GET", null, token, ParseItems);
        }

        public async Task<EmbyItemsResult> GetItems(EmbyAccount account, EmbyItem parent, int startIndex = 0, string search = "", CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return await SendAccount(account, ItemsPath(account, parent?.Id, startIndex, search, parent?.IsMovieLibrary == true), "GET", null, token, ParseItems);
        }

        private static string ItemsPath(EmbyAccount account, string parentId, int startIndex, string search, bool movieLibrary)
            => PagedItemsPath(account, parentId, startIndex, search, movieLibrary, PageSize);

        private static string PagedItemsPath(EmbyAccount account, string parentId, int startIndex, string search, bool movieLibrary, int pageSize)
        {
            string path = UserPath(account) + "/Items?Limit=" + pageSize + "&StartIndex=" + Math.Max(0, startIndex) +
                "&EnableTotalRecordCount=true&SortBy=SortName&SortOrder=Ascending&Fields=" + Escape(Fields);
            if (!string.IsNullOrEmpty(parentId)) path += "&ParentId=" + Escape(parentId);
            path += "&Recursive=" + (movieLibrary || !string.IsNullOrWhiteSpace(search) ? "true" : "false");
            if (movieLibrary) path += "&IncludeItemTypes=Movie";
            if (!string.IsNullOrWhiteSpace(search)) path += "&SearchTerm=" + Escape(search.Trim());
            return path;
        }

        public Task<EmbyItemsResult> GetAllItems(EmbyAccount account, EmbyItem parent, string search = "", CancellationToken token = default)
            => CollectItems(start => SendAccount(account,
                PagedItemsPath(account, parent?.Id, start, search, parent?.IsMovieLibrary == true, AllItemsPageSize), "GET", null, token, ParseItems), token);

        public async Task<EmbyItem> GetPerson(EmbyAccount account, string personId, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var person = await GetItem(account, personId, token);
            token.ThrowIfCancellationRequested();
            if (person.Type != "Person") throw new InvalidOperationException("Emby 演员资料暂不可用");
            return person;
        }

        public Task<EmbyItemsResult> GetPersonMovies(EmbyAccount account, string personId, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); RequireId(personId);
            return CollectItems(start => SendAccount(account,
                MovieFilterPath(account, start, "", "PersonIds", personId), "GET", null, token, ParseItems), token);
        }

        public Task<EmbyItemsResult> GetFilteredMovies(EmbyAccount account, string kind, string value, string id = "", string search = "", CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string firstPath = FilteredMoviesPath(account, kind, value, id, search, 0);
            return CollectItems(start => SendAccount(account,
                start == 0 ? firstPath : FilteredMoviesPath(account, kind, value, id, search, start), "GET", null, token, ParseItems), token);
        }

        private static string FilteredMoviesPath(EmbyAccount account, string kind, string value, string id, string search, int startIndex)
        {
            string parameter;
            if (kind == "Genre") parameter = "Genres";
            else if (kind == "Tag") parameter = "Tags";
            else if (kind == "Studio") parameter = string.IsNullOrWhiteSpace(id) || id == "0" ? "Studios" : "StudioIds";
            else throw new ArgumentException("Emby 分类标识无效");
            string filter = parameter == "StudioIds" ? id : value;
            if (string.IsNullOrWhiteSpace(filter)) throw new ArgumentException("Emby 分类标识无效");
            return MovieFilterPath(account, startIndex, search, parameter, filter);
        }

        private static string MovieFilterPath(EmbyAccount account, int startIndex, string search, string parameter, string value)
            => PagedItemsPath(account, null, startIndex, search, true, AllItemsPageSize) + "&" + parameter + "=" + Escape(value);

        private static async Task<EmbyItemsResult> CollectItems(Func<int, Task<EmbyItemsResult>> fetchPage, CancellationToken token)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var items = new List<EmbyItem>();
            int start = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var page = await fetchPage(start);
                token.ThrowIfCancellationRequested();
                // Keep request creation on Unity's context, but all library-sized work off it.
                bool complete = await Task.Run(() =>
                {
                    var current = page?.Items ?? Array.Empty<EmbyItem>();
                    int added = 0;
                    foreach (var item in current)
                    {
                        token.ThrowIfCancellationRequested();
                        if (item != null && !string.IsNullOrEmpty(item.Id) && seen.Add(item.Id)) { items.Add(item); added++; }
                    }
                    int total = Math.Max(0, page?.TotalRecordCount ?? 0);
                    if (current.Length == 0)
                    {
                        if (start < total) throw new InvalidOperationException("Emby 媒体列表未能完整加载，请重试");
                        return true;
                    }
                    if (added == 0) throw new InvalidOperationException("Emby 媒体列表未能完整加载，请重试");
                    start = checked(start + current.Length);
                    return start >= total && (total > 0 || current.Length < AllItemsPageSize);
                }, token);
                if (complete) break;
            }
            token.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                // Preserve a deterministic wall when multiple pages contain equal sort names.
                var sorted = items.OrderBy(item => string.IsNullOrEmpty(item.SortName) ? item.Name ?? "" : item.SortName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                token.ThrowIfCancellationRequested();
                return new EmbyItemsResult { Items = sorted, TotalRecordCount = sorted.Length };
            }, token);
        }

        public async Task<EmbyItem[]> GetAdditionalParts(EmbyAccount account, string itemId, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            RequireId(itemId); RequireAccount(account);
            string path = "/Videos/" + Escape(itemId) + "/AdditionalParts?UserId=" + Escape(account.userId) + "&Fields=" + Escape(Fields);
            var parts = (await SendAccount(account, path, "GET", null, token, ParseItems)).Items;
            token.ThrowIfCancellationRequested();
            // File stacking order belongs to the server; filenames and parent folders do not identify a part's position.
            return RelatedItems(itemId, parts);
        }

        public async Task<EmbyItem[]> GetExtras(EmbyAccount account, string itemId, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            RequireId(itemId);
            string path = UserPath(account) + "/Items/" + Escape(itemId);
            var features = SendAccount(account, path + "/SpecialFeatures?Fields=" + Escape(Fields), "GET", null, token, ParseItems);
            var trailers = SendAccount(account, path + "/LocalTrailers?Fields=" + Escape(Fields), "GET", null, token, ParseItems);
            var responses = await Task.WhenAll(features, trailers);
            token.ThrowIfCancellationRequested();
            // Both endpoints are authoritative even when ExtraType and the optional count fields are absent.
            return RelatedItems(itemId, responses[0].Items, responses[1].Items);
        }

        private static EmbyItem[] RelatedItems(string mainId, params EmbyItem[][] groups)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { mainId };
            var items = new List<EmbyItem>();
            foreach (var group in groups)
                foreach (var item in group ?? Array.Empty<EmbyItem>())
                    if (item != null && !string.IsNullOrEmpty(item.Id) && seen.Add(item.Id)) items.Add(item);
            return items.ToArray();
        }

        public async Task<EmbyItem> GetItem(EmbyAccount account, string itemId, CancellationToken token = default)
        {
            RequireId(itemId);
            return await SendAccount(account, UserPath(account) + "/Items/" + Escape(itemId) + "?Fields=" + Escape(Fields), "GET", null, token, json =>
            {
                var item = Parse<EmbyItem>(json);
                if (string.IsNullOrEmpty(item?.Id)) throw new InvalidOperationException("Emby 影片资料暂不可用");
                NormalizeItem(item);
                return item;
            });
        }

        public async Task<EmbyItem[]> GetPlayedStateItems(EmbyAccount account, string itemId, CancellationToken token = default)
        {
            var item = await GetItem(account, itemId, token);
            if (!item.IsPlayable || item.IsBrowsable) throw new InvalidOperationException("请选择影片或单集后标记播放状态");
            var main = item;
            if (item.IsAdditionalPart)
            {
                if (string.IsNullOrEmpty(item.ParentId)) throw new InvalidOperationException("无法确认此 CD 所属影片，请从影片详情重试");
                main = await GetItem(account, item.ParentId, token);
            }
            // Only a movie's official AdditionalParts form a film-wide watched action. Never expand a series or extras.
            if (main.Type != "Movie" || main.IsExtra || main.IsAdditionalPart)
            {
                if (item.IsAdditionalPart) throw new InvalidOperationException("无法确认此 CD 所属电影，请从影片详情重试");
                return new[] { item };
            }
            var additional = await GetAdditionalParts(account, main.Id, token);
            var parts = EmbyMovieParts.Create(account.id, main, additional.Where(part => part.Type == "Movie" || part.Type == "Video"));
            if (item.IsAdditionalPart && (parts == null || parts.IndexOf(item.Id) < 0))
                throw new InvalidOperationException("此 CD 不在服务器的影片分段列表中，请刷新后重试");
            return parts?.Items.ToArray() ?? new[] { main };
        }

        public async Task<EmbyUserData> SetPlayed(EmbyAccount account, string itemId, bool played, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); RequireId(itemId);
            string path = UserPath(account) + "/PlayedItems/" + Escape(itemId);
            var result = await SendAccount(account, path, played ? "POST" : "DELETE", null, token, Parse<EmbyUserData>);
            token.ThrowIfCancellationRequested();
            if (result == null || result.Played != played || result.PlaybackPositionTicks != 0)
                throw new InvalidOperationException("Emby 未确认播放状态，请刷新后重试");
            return result;
        }

        public async Task<EmbyPlayback> GetPlayback(EmbyAccount account, string itemId, long startMs, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var item = await GetItem(account, itemId, token);
            if (item.IsBrowsable) throw new InvalidOperationException("请选择一部影片或剧集后播放");
            var request = new PlaybackInfoRequest { UserId = account.userId, StartTimeTicks = MillisecondsToTicks(startMs) };
            var info = await SendAccount(account, "/Items/" + Escape(itemId) + "/PlaybackInfo", "POST", JsonUtility.ToJson(request), token, Parse<PlaybackInfoResponse>);
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(info?.ErrorCode)) throw new InvalidOperationException("Emby 无法提供原始视频，请检查账户播放权限");
            var source = info?.MediaSources?.FirstOrDefault(value => value != null && !string.IsNullOrEmpty(value.Id) && !value.IsInfiniteStream && !value.RequiresOpening);
            if (source == null) throw new InvalidOperationException("Emby 未提供可直接播放的原始文件");
            source.MediaStreams ??= Array.Empty<EmbyMediaStream>();
            source.Path = string.IsNullOrEmpty(source.Path) ? item.Path : source.Path;
            source.DateModified = string.IsNullOrEmpty(source.DateModified) ? item.DateModified : source.DateModified;
            if (source.Size <= 0) source.Size = item.Size;
            string playSessionId = string.IsNullOrEmpty(info.PlaySessionId) ? Guid.NewGuid().ToString("N") : info.PlaySessionId;
            return new EmbyPlayback
            {
                item = item, source = source, token = account.accessToken,
                playSessionId = playSessionId,
                startPositionMs = Math.Max(0, startMs), playMethod = "DirectStream",
                url = BuildDirectStreamUrl(account, itemId, source.Id, playSessionId)
            };
        }

        public Task ReportPlayback(EmbyAccount account, EmbyPlayback playback, string eventName, long positionMs, bool paused, CancellationToken token = default)
        {
            if (playback?.item == null || playback.source == null) throw new ArgumentException("Emby 播放会话无效");
            string path = "/Sessions/Playing/Progress";
            if (string.Equals(eventName, "start", StringComparison.OrdinalIgnoreCase)) path = "/Sessions/Playing";
            else if (string.Equals(eventName, "stop", StringComparison.OrdinalIgnoreCase)) path = "/Sessions/Playing/Stopped";
            var body = new PlaybackReport
            {
                ItemId = playback.item.Id, MediaSourceId = playback.source.Id, PlaySessionId = playback.playSessionId,
                PlayMethod = playback.playMethod, PositionTicks = MillisecondsToTicks(positionMs), IsPaused = paused,
                EventName = string.Equals(eventName, "progress", StringComparison.OrdinalIgnoreCase) ? "TimeUpdate" : eventName
            };
            return SendAccount(account, path, "POST", JsonUtility.ToJson(body), token);
        }

        public Task Logout(EmbyAccount account, CancellationToken token = default)
            => SendAccount(account, "/Sessions/Logout", "POST", "{}", token);

        public Task<string> GetSubtitle(EmbyAccount account, string itemId, string sourceId, int streamIndex, CancellationToken token = default)
        {
            RequireId(itemId); RequireId(sourceId);
            if (streamIndex < 0) throw new ArgumentException("Emby 字幕标识无效");
            // Construct the server-owned route; never attach credentials to a returned DeliveryUrl.
            string path = "/Videos/" + Escape(itemId) + "/" + Escape(sourceId) + "/Subtitles/" + streamIndex + "/Stream.srt";
            RequireAccount(account);
            return Send(account.serverUrl, path, account, "GET", null, token, SubtitleByteLimit);
        }

        public string ImageUrl(EmbyAccount account, string itemId, string type = "Primary", int width = 300)
        {
            RequireId(itemId);
            if (account == null) throw new ArgumentException("Emby 账户无效");
            if (type != "Primary" && type != "Backdrop" && type != "Thumb" && type != "Banner" && type != "Disc" && type != "Logo") type = "Primary";
            return Endpoint(account.serverUrl, "/Items/" + Escape(itemId) + "/Images/" + type + (type == "Backdrop" ? "/0" : "") +
                "?maxWidth=" + Math.Clamp(width, 32, 1024) + "&quality=85&format=" + (type == "Disc" || type == "Logo" ? "png" : "jpg"));
        }

        public string ImageUrl(EmbyAccount account, EmbyItem item, string type = "Primary", int width = 300)
        {
            if (item == null) throw new ArgumentException("Emby 图片标识无效");
            if (type != "Primary" && type != "Backdrop" && type != "Thumb" && type != "Banner" && type != "Disc" && type != "Logo") type = "Primary";
            string url = ImageUrl(account, item.Id, type, width);
            string tag = EmbyLibraryViews.ImageTag(item,type);
            return string.IsNullOrEmpty(tag) ? url : url + "&tag=" + Escape(tag);
        }

        public static string BuildDirectStreamUrl(EmbyAccount account, string itemId, string sourceId, string playSessionId)
        {
            if (account == null) throw new ArgumentException("Emby 账户无效");
            RequireId(itemId); RequireId(sourceId);
            string path = "/Videos/" + Escape(itemId) + "/stream?Static=true&MediaSourceId=" + Escape(sourceId);
            if (!string.IsNullOrEmpty(playSessionId)) path += "&PlaySessionId=" + Escape(playSessionId);
            return Endpoint(account.serverUrl, path);
        }

        public static string NormalizeServerUrl(string serverUrl)
        {
            if (string.IsNullOrWhiteSpace(serverUrl)) throw new ArgumentException("请输入 Emby 服务器地址");
            string value = serverUrl.Trim();
            if (!value.Contains("://")) value = "http://" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("请输入不含账户信息和查询参数的 HTTP 或 HTTPS 服务器地址");
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        public static EmbyItemsResult ParseItems(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new EmbyItemsResult();
            bool array = json.TrimStart().StartsWith("[", StringComparison.Ordinal);
            var result = Parse<EmbyItemsResult>(array ? "{\"Items\":" + json + "}" : json) ?? new EmbyItemsResult();
            result.Items = (result.Items ?? Array.Empty<EmbyItem>()).Where(item => item != null && !string.IsNullOrEmpty(item.Id)).ToArray();
            foreach (var item in result.Items) NormalizeItem(item);
            if (array) result.TotalRecordCount = result.Items.Length;
            return result;
        }

        public static long MillisecondsToTicks(long value) => Math.Min(Math.Max(0, value), long.MaxValue / 10000) * 10000;
        public static string SafeError(Exception exception)
        {
            if (exception is OperationCanceledException) return "Emby 操作已取消";
            if (exception is EmbyAuthenticationException) return "Emby 登录已失效，请重新登录";
            string message = exception?.Message ?? "";
            string[] safeMessages =
            {
                "请输入 Emby 用户名", "请输入 Emby 服务器地址", "Emby 账户无效", "Emby 媒体标识无效", "Emby 字幕标识无效",
                "Emby 登录响应无效，请检查服务器后重试", "Emby 影片资料暂不可用", "请选择一部影片或剧集后播放",
                "Emby 演员资料暂不可用", "Emby 分类标识无效", "Emby 媒体列表未能完整加载，请重试",
                "Emby 无法提供原始视频，请检查账户播放权限", "Emby 未提供可直接播放的原始文件", "Emby 播放会话无效",
                "Emby 返回的数据无法识别，请重试", "Emby 返回的内容过大，请缩小媒体库后重试",
                "Emby 账户没有访问或播放权限", "Emby 媒体或服务器接口已不存在", "Emby 服务器暂时不可用，请稍后重试",
                "无法连接 Emby，请检查地址和网络后重试", "Emby 账户保存失败，请检查设备存储后重试",
                "请输入不含账户信息和查询参数的 HTTP 或 HTTPS 服务器地址"
            };
            return safeMessages.Contains(message) ? message : "Emby 操作失败，请检查网络后重试";
        }
        private static string Escape(string value) => Uri.EscapeDataString(value ?? "");
        private static string Endpoint(string server, string path) => NormalizeServerUrl(server) + "/" + path.TrimStart('/');
        private static string UserPath(EmbyAccount account)
        {
            RequireAccount(account);
            return "/Users/" + Escape(account.userId);
        }
        private static void RequireId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Emby 媒体标识无效");
        }
        private static void RequireAccount(EmbyAccount account)
        {
            if (account == null || string.IsNullOrEmpty(account.userId) || string.IsNullOrEmpty(account.accessToken)) throw new EmbyAuthenticationException();
        }
        private static void NormalizeItem(EmbyItem item)
        {
            item.People ??= Array.Empty<EmbyPerson>(); item.Genres ??= Array.Empty<string>();
            item.Tags ??= Array.Empty<string>(); item.ProductionLocations ??= Array.Empty<string>();
            item.Studios ??= Array.Empty<EmbyNamedItem>(); item.GenreItems ??= Array.Empty<EmbyNamedItem>(); item.TagItems ??= Array.Empty<EmbyNamedItem>();
            if (item.Genres.Length == 0) item.Genres = item.GenreItems.Where(value => !string.IsNullOrEmpty(value?.Name)).Select(value => value.Name).ToArray();
            if (item.Tags.Length == 0) item.Tags = item.TagItems.Where(value => !string.IsNullOrEmpty(value?.Name)).Select(value => value.Name).ToArray();
            item.ImageTags ??= new EmbyImageTags(); item.BackdropImageTags ??= Array.Empty<string>();
            item.UserData ??= new EmbyUserData(); item.MediaSources ??= Array.Empty<EmbyMediaSource>();
            foreach (var source in item.MediaSources) if (source != null) source.MediaStreams ??= Array.Empty<EmbyMediaStream>();
        }
        private static T Parse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { throw new InvalidOperationException("Emby 返回的数据无法识别，请重试"); }
        }
        private Task<string> SendAccount(EmbyAccount account, string path, string method, string body, CancellationToken token)
            => SendAccount(account, path, method, body, token, text => text);

        private Task<T> SendAccount<T>(EmbyAccount account, string path, string method, string body, CancellationToken token, Func<string, T> parse)
        {
            token.ThrowIfCancellationRequested();
            RequireAccount(account);
            return Send(account.serverUrl, path, account, method, body, token, parse);
        }

        private Task<string> Send(string server, string path, EmbyAccount account, string method, string body, CancellationToken token, int byteLimit = JsonByteLimit)
            => Send(server, path, account, method, body, token, text => text, byteLimit);

        private async Task<T> Send<T>(string server, string path, EmbyAccount account, string method, string body, CancellationToken token, Func<string, T> parse, int byteLimit = JsonByteLimit)
        {
            token.ThrowIfCancellationRequested();
            using var download = new BoundedDownloadHandler(byteLimit);
            using var request = new UnityWebRequest(Endpoint(server, path), method) { downloadHandler = download, timeout = 30, redirectLimit = 0 };
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", "Emby Client=\"LsVRPlayer\", Device=\"VR Player\", DeviceId=\"" + deviceId + "\", Version=\"1.0.0\"");
            if (account != null) request.SetRequestHeader("X-Emby-Token", account.accessToken);
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            var operation = request.SendWebRequest();
            try
            {
                while (!operation.isDone) await Task.Delay(40, token);
                token.ThrowIfCancellationRequested();
                if (download.Overflowed) throw new InvalidOperationException("Emby 返回的内容过大，请缩小媒体库后重试");
                if (request.responseCode == 401) throw new EmbyAuthenticationException();
                if (request.responseCode == 403) throw new InvalidOperationException("Emby 账户没有访问或播放权限");
                if (request.responseCode == 404) throw new InvalidOperationException("Emby 媒体或服务器接口已不存在");
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException(request.responseCode >= 500 ? "Emby 服务器暂时不可用，请稍后重试" : "无法连接 Emby，请检查地址和网络后重试");
                // ReceiveData has finished. Only managed bytes/DTOs cross to the worker;
                // UnityWebRequest checks, abort and disposal stay on the Unity thread.
                return await DecodeResponse(download.Payload, parse, token);
            }
            finally
            {
                if (!operation.isDone) request.Abort();
            }
        }

        private static Task<T> DecodeResponse<T>(ArraySegment<byte> payload, Func<string, T> parse, CancellationToken token)
            => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                string text = Encoding.UTF8.GetString(payload.Array, payload.Offset, payload.Count).TrimStart('\uFEFF');
                var result = parse(text);
                token.ThrowIfCancellationRequested();
                return result;
            }, token);

        private static string SafeHeaderValue(string value)
        {
            string result = new string((value ?? "").Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_').Take(128).ToArray());
            return string.IsNullOrEmpty(result) ? "lsvrplayer" : result;
        }

        private sealed class BoundedDownloadHandler : DownloadHandlerScript
        {
            private readonly MemoryStream buffer = new MemoryStream();
            private readonly int byteLimit;
            public bool Overflowed { get; private set; }
            public ArraySegment<byte> Payload => new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
            public byte[] Bytes => buffer.ToArray();
            public BoundedDownloadHandler(int byteLimit) : base(new byte[32 * 1024]) { this.byteLimit = byteLimit; }
            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0) return true;
                if (dataLength > byteLimit - buffer.Length) { Overflowed = true; return false; }
                buffer.Write(data, 0, dataLength);
                return true;
            }
            protected override byte[] GetData() => Bytes;
        }
    }
}
