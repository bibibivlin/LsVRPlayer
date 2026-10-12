using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class EmbyApiTests
    {
        private string directory, metadataPath, vault;

        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "VRPlayerEmbyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            metadataPath = Path.Combine(directory, "accounts.json");
            vault = "";
        }

        [TearDown] public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private EmbyAccountStore Store() => new EmbyAccountStore(metadataPath, () => vault, value => { vault = value; return true; });
        private static EmbyAccount Account(string serverId = "server-a", string userId = "user-a", string secret = "private-access-token")
            => new EmbyAccount { id = Guid.NewGuid().ToString("N"), name = "测试服务器", serverUrl = "http://example.test:8096", serverId = serverId, userId = userId, username = "viewer", accessToken = secret, remember = true };

        [TestCase("http://example.test:8096/", "http://example.test:8096")]
        [TestCase("https://example.test/reverse/emby/", "https://example.test/reverse/emby")]
        [TestCase("example.test:8096", "http://example.test:8096")]
        public void ServerNormalizationPreservesReverseProxyBase(string input, string expected)
            => Assert.That(EmbyClient.NormalizeServerUrl(input), Is.EqualTo(expected));

        [TestCase("ftp://example.test")]
        [TestCase("http://viewer:secret@example.test")]
        [TestCase("http://example.test?api_key=secret")]
        [TestCase("http://example.test/#accessToken=secret")]
        public void SecretBearingOrUnsupportedServerAddressIsRejected(string input)
            => Assert.Throws<ArgumentException>(() => EmbyClient.NormalizeServerUrl(input));

        [Test] public void StableIdentityContainsAccountAndItemWithoutServerOrToken()
        {
            string uri = EmbyIdentity.Create("Account_A", "391");
            Assert.That(uri, Is.EqualTo("emby://Account_A/391"));
            Assert.That(EmbyIdentity.TryParse(uri, out string account, out string item), Is.True);
            Assert.That(account, Is.EqualTo("Account_A")); Assert.That(item, Is.EqualTo("391"));
        }

        [TestCase("emby://account/item/extra")]
        [TestCase("emby://account/item?api_key=private")]
        [TestCase("emby://account/%2Fprivate")]
        [TestCase("emby://account/..")]
        [TestCase("http://account/item")]
        public void AmbiguousOrSecretBearingIdentitiesAreRejected(string uri)
            => Assert.That(EmbyIdentity.TryParse(uri, out _, out _), Is.False);

        [Test] public void DirectStreamUsesOriginalStaticFileWithoutCredentialQuery()
        {
            var account = Account();
            account.serverUrl = "https://example.test/proxy/emby/";
            string url = EmbyClient.BuildDirectStreamUrl(account, "42", "source a", "session a");
            Assert.That(url, Is.EqualTo("https://example.test/proxy/emby/Videos/42/stream?Static=true&MediaSourceId=source%20a&PlaySessionId=session%20a"));
            Assert.That(url, Does.Not.Contain(account.accessToken));
            Assert.That(url, Does.Not.Contain("api_key"));
        }

        [Test] public void ImageUrlIsBoundedJpegWithoutCredentialQuery()
        {
            var account = Account();
            string url = new EmbyClient("test-device").ImageUrl(account, "42", "Primary", 5000);
            Assert.That(url, Does.Contain("maxWidth=1024")); Assert.That(url, Does.Contain("format=jpg"));
            Assert.That(url, Does.Not.Contain(account.accessToken));
        }

        [TestCase("Primary", "primary")]
        [TestCase("Thumb", "thumb")]
        [TestCase("Banner", "banner")]
        [TestCase("Backdrop", "backdrop")]
        [TestCase("Disc", "disc")]
        [TestCase("Logo", "logo")]
        [TestCase("Unsupported", "primary")]
        public void ImageTagMatchesRequestedImageType(string type, string expectedTag)
        {
            var item = new EmbyItem { Id = "1", ImageTags = new EmbyImageTags { Primary = "primary", Thumb = "thumb", Banner = "banner", Disc = "disc", Logo = "logo" }, BackdropImageTags = new[] { "backdrop" } };
            string url = new EmbyClient("test-device").ImageUrl(Account(), item, type);
            Assert.That(url, Does.EndWith("&tag=" + expectedTag));
        }

        [Test] public void MissingThumbTagDoesNotReusePrimaryImageTag()
        {
            var item = new EmbyItem { Id = "1", ImageTags = new EmbyImageTags { Primary = "primary" } };
            Assert.That(new EmbyClient("test-device").ImageUrl(Account(), item, "Thumb"), Does.Not.Contain("&tag="));
        }

        [Test] public void LatestArrayAndPagedListKeepExpectedCounts()
        {
            var latest = EmbyClient.ParseItems("[{\"Id\":\"1\",\"Name\":\"电影\",\"Type\":\"Movie\"}]");
            Assert.That(latest.Items.Length, Is.EqualTo(1)); Assert.That(latest.TotalRecordCount, Is.EqualTo(1));
            Assert.That(latest.Items[0].UserData, Is.Not.Null); Assert.That(latest.Items[0].People, Is.Empty);
            var page = EmbyClient.ParseItems("{\"Items\":[{\"Id\":\"2\",\"Type\":\"Series\"}],\"TotalRecordCount\":83,\"StartIndex\":30}");
            Assert.That(page.TotalRecordCount, Is.EqualTo(83)); Assert.That(page.StartIndex, Is.EqualTo(30));
            Assert.That(page.Items[0].IsBrowsable, Is.True);
        }

        [Test] public void MetadataParsingKeepsResumeActorsAndOriginalFileDetails()
        {
            var item = EmbyClient.ParseItems("{\"Items\":[{\"Id\":\"1\",\"Name\":\"电影\",\"Overview\":\"简介\",\"People\":[{\"Name\":\"演员\",\"Role\":\"主角\",\"Type\":\"Actor\"}],\"UserData\":{\"PlaybackPositionTicks\":12340000,\"PlayedPercentage\":12.5},\"MediaSources\":[{\"Id\":\"s1\",\"Path\":\"film.180.SBS.mkv\",\"Size\":100000000,\"MediaStreams\":[{\"Index\":2,\"Type\":\"Subtitle\",\"Codec\":\"srt\",\"IsExternal\":true}]}]}]}").Items[0];
            Assert.That(item.People[0].Role, Is.EqualTo("主角"));
            Assert.That(item.UserData.PlaybackPositionTicks, Is.EqualTo(12340000));
            Assert.That(item.UserData.PlayedPercentage, Is.EqualTo(12.5));
            Assert.That(item.MediaSources[0].Size, Is.EqualTo(100000000));
            Assert.That(item.MediaSources[0].MediaStreams[0].IsExternal, Is.True);
        }

        [Test] public void MovieMetadataDistinguishesPartsAndExtrasWithoutFilenameInference()
        {
            var item = EmbyClient.ParseItems("[{\"Id\":\"main\",\"Type\":\"Movie\",\"Name\":\"PV CD2\",\"PartCount\":3,\"SpecialFeatureCount\":12,\"LocalTrailerCount\":2,\"ParentId\":\"folder\",\"SortName\":\"sort\"}]").Items[0];
            Assert.That(item.PartCount, Is.EqualTo(3)); Assert.That(item.SpecialFeatureCount, Is.EqualTo(12));
            Assert.That(item.LocalTrailerCount, Is.EqualTo(2)); Assert.That(item.ParentId, Is.EqualTo("folder"));
            Assert.That(item.SortName, Is.EqualTo("sort")); Assert.That(item.IsExtra, Is.False);
            var extra = EmbyClient.ParseItems("[{\"Id\":\"extra\",\"Type\":\"Video\",\"ExtraType\":\"Clip\"}]").Items[0];
            Assert.That(extra.IsExtra, Is.True); Assert.That(extra.IsPlayable, Is.True);
            Assert.That(new EmbyItem { Type = "Trailer" }.IsExtra, Is.True);
        }

        [Test] public void OfficialAdditionalPartRemainsAPlayableMainMovieSegment()
        {
            var result = EmbyClient.ParseItems("{\"Items\":[{\"Id\":\"cd2\",\"Type\":\"Video\",\"ExtraType\":\"AdditionalPart\",\"IsFolder\":false,\"ParentId\":\"main\",\"UserData\":{\"PlaybackPositionTicks\":12340000}}],\"TotalRecordCount\":1}");
            var part = result.Items[0];
            Assert.That(part.IsAdditionalPart, Is.True); Assert.That(part.IsExtra, Is.False);
            Assert.That(part.IsPlayable, Is.True); Assert.That(part.IsBrowsable, Is.False);
            Assert.That(part.ParentId, Is.EqualTo("main")); Assert.That(part.UserData.PlaybackPositionTicks, Is.EqualTo(12340000));
            Assert.That(EmbyMovieParts.Create("account", new EmbyItem { Id = "main", Type = "Movie", PartCount = 2 }, result.Items)?.Items.Select(item => item.Id),
                Is.EqualTo(new[] { "main", "cd2" }));
        }

        [Test] public void PersonAndNumericCategoryMetadataKeepsOfficialFields()
        {
            var person = EmbyClient.ParseItems("[{\"Id\":\"actor\",\"Type\":\"Person\",\"Overview\":\"演员介绍\",\"PremiereDate\":\"1980-01-02T00:00:00Z\",\"EndDate\":\"2020-01-02T00:00:00Z\",\"ProductionLocations\":[\"出生地\"]}]").Items[0];
            Assert.That(person.Overview, Is.EqualTo("演员介绍")); Assert.That(person.PremiereDate, Does.StartWith("1980-01-02"));
            Assert.That(person.EndDate, Does.StartWith("2020-01-02")); Assert.That(person.ProductionLocations, Is.EqualTo(new[] { "出生地" }));
            var movie = EmbyClient.ParseItems("[{\"Id\":\"movie\",\"Studios\":[{\"Id\":123456789012,\"Name\":\"制片公司\"}],\"GenreItems\":[{\"Id\":21,\"Name\":\"剧情\"}],\"TagItems\":[{\"Id\":22,\"Name\":\"标签\"}]}]").Items[0];
            Assert.That(movie.Studios[0].Id, Is.EqualTo(123456789012L)); Assert.That(movie.Studios[0].Name, Is.EqualTo("制片公司"));
            Assert.That(movie.Genres, Is.EqualTo(new[] { "剧情" })); Assert.That(movie.Tags, Is.EqualTo(new[] { "标签" }));
        }

        [Test] public void ExplicitGenreAndTagArraysTakePriorityOverNamedMetadataFallback()
        {
            var movie = EmbyClient.ParseItems("[{\"Id\":\"movie\",\"Genres\":[\"剧情\"],\"Tags\":[\"标签\"],\"GenreItems\":[{\"Id\":1,\"Name\":\"其他\"}],\"TagItems\":[null,{\"Id\":2,\"Name\":\"其他标签\"}]}]").Items[0];
            Assert.That(movie.Genres, Is.EqualTo(new[] { "剧情" })); Assert.That(movie.Tags, Is.EqualTo(new[] { "标签" }));
        }

        [TestCase("Genre", "剧情 & 家庭", "51", "Genres=%E5%89%A7%E6%83%85%20%26%20%E5%AE%B6%E5%BA%AD")]
        [TestCase("Tag", "tag & clip", "52", "Tags=tag%20%26%20clip")]
        [TestCase("Studio", "studio", "53", "StudioIds=53")]
        [TestCase("Studio", "studio & name", "", "Studios=studio%20%26%20name")]
        [TestCase("Studio", "studio & name", "0", "Studios=studio%20%26%20name")]
        public void CategoryQueriesSearchEveryVisibleMovieWithEscapedFilter(string kind, string value, string id, string expected)
        {
            string path = (string)typeof(EmbyClient).GetMethod("FilteredMoviesPath", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Account(), kind, value, id, " title & name ", 200 });
            Assert.That(path, Does.Contain("Limit=200&StartIndex=200")); Assert.That(path, Does.Contain("Recursive=true&IncludeItemTypes=Movie"));
            Assert.That(path, Does.Contain("SearchTerm=title%20%26%20name")); Assert.That(path, Does.EndWith("&" + expected));
            Assert.That(path, Does.Not.Contain("ParentId=")); Assert.That(path, Does.Not.Contain("private-access-token"));
        }

        [Test] public void ActorMovieQueryUsesIdAcrossAllLibraries()
        {
            string path = (string)typeof(EmbyClient).GetMethod("MovieFilterPath", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Account(), 200, "", "PersonIds", "actor & id" });
            Assert.That(path, Does.EndWith("&PersonIds=actor%20%26%20id")); Assert.That(path, Does.Contain("Recursive=true&IncludeItemTypes=Movie"));
            Assert.That(path, Does.Not.Contain("ParentId="));
        }

        [Test] public void FullLibraryReadsPastBothFirstThirtyAndTwoHundredItems()
        {
            var source = Enumerable.Range(1, 205).Select(index => new EmbyItem { Id = index.ToString(), SortName = index.ToString("D3") }).ToArray();
            var starts = new System.Collections.Generic.List<int>();
            var result = CollectItems(start =>
            {
                starts.Add(start);
                return Task.FromResult(new EmbyItemsResult { Items = source.Skip(start).Take(200).ToArray(), TotalRecordCount = 205 });
            }).GetAwaiter().GetResult();
            Assert.That(starts, Is.EqualTo(new[] { 0, 200 })); Assert.That(result.Items.Length, Is.EqualTo(205));
            Assert.That(result.TotalRecordCount, Is.EqualTo(205)); Assert.That(result.Items.Last().Id, Is.EqualTo("205"));
        }

        [Test] public void FullLibraryContinuesClampedPagesAndDeduplicatesWithStableSorting()
        {
            var starts = new System.Collections.Generic.List<int>();
            var result = CollectItems(start =>
            {
                starts.Add(start);
                var items = start == 0 ? new[] { new EmbyItem { Id = "b", SortName = "same" }, new EmbyItem { Id = "a", SortName = "Same" } } :
                    new[] { new EmbyItem { Id = "b", SortName = "same" }, new EmbyItem { Id = "c", SortName = "last" } };
                return Task.FromResult(new EmbyItemsResult { Items = items, TotalRecordCount = 4 });
            }).GetAwaiter().GetResult();
            Assert.That(starts, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(result.Items.Select(item => item.Id), Is.EqualTo(new[] { "c", "a", "b" })); Assert.That(result.TotalRecordCount, Is.EqualTo(3));
        }

        [Test] public void FullLibraryRejectsRepeatedOrTruncatedPagesInsteadOfReturningPartialWall()
        {
            var page = new EmbyItemsResult { Items = new[] { new EmbyItem { Id = "1" } }, TotalRecordCount = 4 };
            Assert.Throws<InvalidOperationException>(() => CollectItems(start => Task.FromResult(page)).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => CollectItems(start => Task.FromResult(start == 0 ? page : new EmbyItemsResult { TotalRecordCount = 4 })).GetAwaiter().GetResult());
        }

        [Test] public void CancellingFullLibraryAfterPageResponsePreventsNextRequestAndPartialResult()
        {
            using var cancellation = new CancellationTokenSource(); int requests = 0;
            Assert.Throws<OperationCanceledException>(() => CollectItems(start =>
            {
                requests++; cancellation.Cancel();
                return Task.FromResult(new EmbyItemsResult { Items = new[] { new EmbyItem { Id = "1" } }, TotalRecordCount = 205 });
            }, cancellation.Token).GetAwaiter().GetResult());
            Assert.That(requests, Is.EqualTo(1));
        }

        private static Task<EmbyItemsResult> CollectItems(Func<int, Task<EmbyItemsResult>> fetchPage, CancellationToken token = default)
            => (Task<EmbyItemsResult>)typeof(EmbyClient).GetMethod("CollectItems", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { fetchPage, token });

        [TestCase("Movie")][TestCase("Video")][TestCase("Episode")][TestCase("Trailer")][TestCase("MusicVideo")]
        public void PlayableVideoTypesTakePriorityOverFolderFlag(string type)
        {
            var item = new EmbyItem { Type = type, IsFolder = true };
            Assert.That(item.IsPlayable, Is.True); Assert.That(item.IsBrowsable, Is.False);
        }

        [Test] public void MovieLibraryUsesMetadataQueryWithPagingAndEscapedSearch()
        {
            var library = EmbyClient.ParseItems("[{\"Id\":\"library\",\"Type\":\"CollectionFolder\",\"CollectionType\":\"movies\"}]").Items[0];
            Assert.That(library.IsMovieLibrary, Is.True);
            string path = ItemsPath(library.Id, 30, " title & clip ", library.IsMovieLibrary);
            Assert.That(path, Does.Contain("&Recursive=true")); Assert.That(path, Does.Contain("&IncludeItemTypes=Movie"));
            Assert.That(path, Does.Contain("Limit=30&StartIndex=30&EnableTotalRecordCount=true"));
            Assert.That(path, Does.Contain("&ParentId=library")); Assert.That(path, Does.EndWith("&SearchTerm=title%20%26%20clip"));
            Assert.That(path, Does.Not.Contain("private-access-token"));
            Assert.That(ItemsPath(library.Id, 0, "", library.IsMovieLibrary), Does.Contain("&Recursive=true&IncludeItemTypes=Movie"));
        }

        [TestCase("tvshows")][TestCase("mixed")][TestCase("homevideos")][TestCase(null)]
        public void OtherLibrariesKeepTheirHierarchyAndRecursiveSearch(string collectionType)
        {
            var library = new EmbyItem { Id = "library", Type = "CollectionFolder", CollectionType = collectionType };
            Assert.That(library.IsMovieLibrary, Is.False);
            string path = ItemsPath(library.Id, -1, "", library.IsMovieLibrary);
            Assert.That(path, Does.Contain("&Recursive=false")); Assert.That(path, Does.Not.Contain("IncludeItemTypes="));
            Assert.That(path, Does.Contain("&StartIndex=0"));
            string search = ItemsPath(library.Id, 60, "episode", library.IsMovieLibrary);
            Assert.That(search, Does.Contain("&Recursive=true")); Assert.That(search, Does.Not.Contain("IncludeItemTypes="));
        }

        private static string ItemsPath(string parentId, int startIndex, string search, bool movieLibrary)
            => (string)typeof(EmbyClient).GetMethod("ItemsPath", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Account(), parentId, startIndex, search, movieLibrary });

        [Test] public void AdditionalPartsKeepServerOrderAndExcludeMainAndDuplicateItems()
        {
            var parts = EmbyClient.ParseItems("{\"Items\":[{\"Id\":\"main\",\"Type\":\"Movie\"},{\"Id\":\"part-b\",\"Type\":\"Video\",\"Name\":\"CD2\"},{\"Id\":\"part-a\",\"Type\":\"Video\",\"Name\":\"CD1\"},{\"Id\":\"part-b\",\"Type\":\"Video\"}],\"TotalRecordCount\":4}").Items;
            var result = RelatedItems("main", parts);
            Assert.That(result.Select(item => item.Id), Is.EqualTo(new[] { "part-b", "part-a" }));
        }

        [Test] public void ExtrasMergeFeaturesAndTrailersWithoutReclassifyingUntypedFeature()
        {
            var features = EmbyClient.ParseItems("[{\"Id\":\"feature-b\",\"Type\":\"Video\",\"ExtraType\":\"Clip\"},{\"Id\":\"shared\",\"Type\":\"Video\"}]").Items;
            var trailers = EmbyClient.ParseItems("[{\"Id\":\"shared\",\"Type\":\"Trailer\"},{\"Id\":\"trailer\",\"Type\":\"Trailer\",\"ExtraType\":\"Trailer\"},{\"Id\":\"main\",\"Type\":\"Movie\"}]").Items;
            var result = RelatedItems("main", features, trailers);
            Assert.That(result.Select(item => item.Id), Is.EqualTo(new[] { "feature-b", "shared", "trailer" }));
            Assert.That(result[1], Is.SameAs(features[1]));
            Assert.That(result[1].ExtraType, Is.Null.Or.Empty);
        }

        private static EmbyItem[] RelatedItems(string mainId, params EmbyItem[][] groups)
            => (EmbyItem[])typeof(EmbyClient).GetMethod("RelatedItems", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { mainId, groups });

        [TestCase(@"Z:\Films\movie.180.SBS.mkv", "movie.180.SBS.mkv")]
        [TestCase("/media/movie.360.TB.mp4", "movie.360.TB.mp4")]
        [TestCase("https://example.test/movie%20SBS.mkv?token=secret", "movie SBS.mkv")]
        public void OriginalFilenameRetainsVrTagsAcrossServerOperatingSystems(string path, string expected)
        {
            var playback = new EmbyPlayback { source = new EmbyMediaSource { Path = path }, item = new EmbyItem { Name = "展示标题" } };
            Assert.That(playback.SourceFileName, Is.EqualTo(expected));
        }

        [Test] public void RememberedCredentialIsExcludedFromPublicMetadataAndRestoredFromVault()
        {
            var store = Store(); var account = Account(); store.Upsert(account);
            Assert.That(File.ReadAllText(metadataPath), Does.Not.Contain(account.accessToken));
            Assert.That(File.ReadAllText(metadataPath), Does.Not.Contain("accessToken"));
            Assert.That(vault, Does.Contain(account.accessToken));
            Assert.That(Store().Accounts[0].accessToken, Is.EqualTo(account.accessToken));
            Assert.That(JsonUtility.ToJson(account), Does.Not.Contain(account.accessToken));
        }

        [Test] public void SessionOnlyLoginNeverRemembersItsCredential()
        {
            var store = Store(); var account = Account(); account.remember = false; store.Upsert(account);
            Assert.That(account.accessToken, Is.Not.Empty);
            Assert.That(vault, Does.Not.Contain(account.accessToken));
            Assert.That(Store().Accounts[0].accessToken, Is.Empty);
        }

        [Test] public void ReloginPreservesStableAccountIdAndMultipleAccountsRemainSeparate()
        {
            var store = Store(); var first = Account(); store.Upsert(first);
            var replacement = Account(secret: "replacement-secret"); store.Upsert(replacement);
            Assert.That(replacement.id, Is.EqualTo(first.id)); Assert.That(store.Accounts.Count, Is.EqualTo(1));
            store.Upsert(Account(userId: "user-b")); store.Upsert(Account(serverId: "server-b"));
            Assert.That(store.Accounts.Count, Is.EqualTo(3));
            Assert.That(Store().Accounts.Count, Is.EqualTo(3));
        }

        [Test] public void ChangingStoredServerAddressCannotReuseProtectedCredential()
        {
            var store = Store(); store.Upsert(Account());
            string metadata = File.ReadAllText(metadataPath).Replace("example.test", "different.test");
            File.WriteAllText(metadataPath, metadata);
            Assert.That(Store().Accounts[0].accessToken, Is.Empty);
        }

        [Test] public void RemovingAccountDeletesRememberedCredentialAndUpdatesSelection()
        {
            var store = Store(); var first = Account(); var second = Account(userId: "user-b", secret: "second-secret");
            store.Upsert(first); store.Upsert(second); store.Remove(second.id);
            Assert.That(vault, Does.Not.Contain(second.accessToken));
            Assert.That(store.SelectedId, Is.EqualTo(first.id));
            Assert.That(Store().Accounts.Count, Is.EqualTo(1));
        }

        [Test] public void FailedCredentialProtectionDoesNotWritePlaintextFallback()
        {
            var store = new EmbyAccountStore(metadataPath, () => "", value => false);
            var account = Account();
            Assert.Throws<InvalidOperationException>(() => store.Upsert(account));
            Assert.That(File.Exists(metadataPath), Is.False);
            Assert.That(File.Exists(metadataPath + ".tmp"), Is.False);
            Assert.That(store.Accounts, Is.Empty); Assert.That(store.SelectedId, Is.Empty);
            Assert.That(store.LastError, Does.Not.Contain("private-access-token"));
        }

        [Test] public void FailedReloginPreservesOriginalAccountIdentityTokenAndSelection()
        {
            bool rejectWrites = false;
            var store = new EmbyAccountStore(metadataPath, () => vault, value =>
            {
                if (rejectWrites) return false;
                vault = value; return true;
            });
            var account = Account(); store.Upsert(account);
            string originalMetadata = File.ReadAllText(metadataPath), originalVault = vault;
            var replacement = Account(secret: "replacement-secret"); string replacementId = replacement.id;
            rejectWrites = true;
            Assert.Throws<InvalidOperationException>(() => store.Upsert(replacement));
            Assert.That(store.Accounts.Count, Is.EqualTo(1)); Assert.That(store.Accounts[0], Is.SameAs(account));
            Assert.That(store.Accounts[0].accessToken, Is.EqualTo(account.accessToken));
            Assert.That(store.SelectedId, Is.EqualTo(account.id)); Assert.That(replacement.id, Is.EqualTo(replacementId));
            Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(originalMetadata)); Assert.That(vault, Is.EqualTo(originalVault));
            Assert.That(Store().Accounts[0].accessToken, Is.EqualTo(account.accessToken));
        }

        [Test] public void FailedRemovalKeepsOriginalAccountOrderSelectionAndRememberedTokens()
        {
            bool rejectWrites = false;
            var store = new EmbyAccountStore(metadataPath, () => vault, value =>
            {
                if (rejectWrites) return false;
                vault = value; return true;
            });
            var first = Account(); var second = Account(userId: "user-b", secret: "second-secret");
            store.Upsert(first); store.Upsert(second);
            string originalMetadata = File.ReadAllText(metadataPath), originalVault = vault;
            rejectWrites = true;
            Assert.Throws<InvalidOperationException>(() => store.Remove(second.id));
            Assert.That(store.Accounts, Is.EqualTo(new[] { first, second })); Assert.That(store.SelectedId, Is.EqualTo(second.id));
            Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(originalMetadata)); Assert.That(vault, Is.EqualTo(originalVault));
            Assert.That(Store().Accounts[1].accessToken, Is.EqualTo(second.accessToken));
        }

        [Test] public void FailedSelectionSaveRestoresLastCommittedSelection()
        {
            bool rejectWrites = false;
            var store = new EmbyAccountStore(metadataPath, () => vault, value =>
            {
                if (rejectWrites) return false;
                vault = value; return true;
            });
            var first = Account(); var second = Account(userId: "user-b"); store.Upsert(first); store.Upsert(second);
            string originalMetadata = File.ReadAllText(metadataPath);
            store.SelectedId = first.id; rejectWrites = true;
            Assert.Throws<InvalidOperationException>(() => store.Save());
            Assert.That(store.SelectedId, Is.EqualTo(second.id));
            Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(originalMetadata)); Assert.That(Store().SelectedId, Is.EqualTo(second.id));
        }

        [Test] public void FailedMetadataStagingCannotChangeCredentialVault()
        {
            var store = Store(); var account = Account(); store.Upsert(account);
            string originalMetadata = File.ReadAllText(metadataPath), originalVault = vault;
            Directory.CreateDirectory(metadataPath + ".tmp");
            Assert.Throws<InvalidOperationException>(() => store.Upsert(Account(userId: "user-b", secret: "second-secret")));
            Assert.That(store.Accounts.Count, Is.EqualTo(1)); Assert.That(store.Accounts[0], Is.SameAs(account));
            Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(originalMetadata)); Assert.That(vault, Is.EqualTo(originalVault));
        }

        [Test] public void FailedMetadataCommitRestoresVaultAfterSuccessfulCredentialWrite()
        {
            var store = Store();
            Directory.CreateDirectory(metadataPath);
            Assert.Throws<InvalidOperationException>(() => store.Upsert(Account()));
            Assert.That(store.Accounts, Is.Empty); Assert.That(store.SelectedId, Is.Empty);
            Assert.That(File.Exists(metadataPath + ".tmp"), Is.False);
            Assert.That(vault, Is.EqualTo("{\"tokens\":[]}"));
        }

        [Test] public void PartiallyFailedCredentialWriteRestoresPreviousVault()
        {
            bool failNextWrite = false;
            var store = new EmbyAccountStore(metadataPath, () => vault, value =>
            {
                vault = value;
                if (failNextWrite) { failNextWrite = false; return false; }
                return true;
            });
            var account = Account(); store.Upsert(account);
            string originalMetadata = File.ReadAllText(metadataPath), originalVault = vault;
            failNextWrite = true;
            Assert.Throws<InvalidOperationException>(() => store.Remove(account.id));
            Assert.That(store.Accounts.Count, Is.EqualTo(1)); Assert.That(store.Accounts[0], Is.SameAs(account));
            Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(originalMetadata)); Assert.That(vault, Is.EqualTo(originalVault));
        }

#if UNITY_EDITOR_WIN
        [Test] public void WindowsCredentialVaultEncryptsTokenAndRestoresForCurrentUser()
        {
            var store = new EmbyAccountStore(metadataPath); var account = Account(); store.Upsert(account);
            string encrypted = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(metadataPath + ".credentials"));
            Assert.That(encrypted, Does.Not.Contain(account.accessToken));
            Assert.That(new EmbyAccountStore(metadataPath).Accounts[0].accessToken, Is.EqualTo(account.accessToken));
        }
#endif

        [TestCase("Playlist")][TestCase("Folder")][TestCase("UserView")][TestCase("Series")][TestCase("Season")][TestCase("BoxSet")]
        public void ContainerTypesRemainBrowsableWhenServerOmitsFolderFlag(string type)
            => Assert.That(new EmbyItem { Type = type }.IsBrowsable, Is.True);

        [Test] public void CancelledAuthenticationDoesNotIssueRequest()
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
                await new EmbyClient("test-device").Authenticate("http://example.test", "viewer", "password", cancellation.Token));
        }

        [Test] public void CancelledPlaybackAndSubtitleRequestsDoNotIssueRequests()
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var client = new EmbyClient("test-device"); var account = Account();
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetPlayback(account, "1", 0, cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetSubtitle(account, "1", "source", 2, cancellation.Token));
        }

        [Test] public void CancelledLibraryPartsAndExtrasRequestsDoNotIssueRequests()
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var client = new EmbyClient("test-device");
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetItems(null, "library", token: cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetItems(null, new EmbyItem { Id = "library", CollectionType = "movies" }, token: cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetAdditionalParts(null, "1", cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetExtras(null, "1", cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetAllItems(null, new EmbyItem { Id = "library" }, token: cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetPerson(null, "actor", cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetPersonMovies(null, "actor", cancellation.Token));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await client.GetFilteredMovies(null, "Genre", "category", token: cancellation.Token));
        }

        [Test] public void ErrorPresentationNeverEchoesArbitraryServerOrCredentialMessages()
            => Assert.That(EmbyClient.SafeError(new IOException("http://private.test/?api_key=private-access-token")), Is.EqualTo("Emby 操作失败，请检查网络后重试"));

        [Test] public void ResumeTickConversionDoesNotOverflow()
        {
            Assert.That(EmbyClient.MillisecondsToTicks(-1), Is.Zero);
            Assert.That(EmbyClient.MillisecondsToTicks(1234), Is.EqualTo(12340000));
            Assert.That(EmbyClient.MillisecondsToTicks(long.MaxValue), Is.GreaterThan(0));
        }
    }
}
