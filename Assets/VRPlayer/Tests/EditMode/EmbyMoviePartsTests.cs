using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class EmbyMoviePartsTests
    {
        private static EmbyItem Movie(string id) => new EmbyItem { Id = id, Type = "Movie", Name = "Film" };

        [Test] public void MainAndServerOrderedPartsFormOneSequenceWithoutDuplicateExtrasOrFolders()
        {
            var main = Movie("main"); var second = Movie("part-b"); var third = Movie("part-a");
            var sequence = EmbyMovieParts.Create("account", main, new[] { main, second, second, null,
                new EmbyItem { Id = "folder", Type = "Folder" }, new EmbyItem { Id = "pv", Type = "Video", ExtraType = "Clip" }, third });
            Assert.That(sequence.Items.Select(x => x.Id), Is.EqualTo(new[] { "main", "part-b", "part-a" }));
            Assert.That(sequence.MainId, Is.EqualTo("main"));
        }

        [Test] public void SingleMovieHasNoMultipartSequence()
            => Assert.That(EmbyMovieParts.Create("account", Movie("main"), Array.Empty<EmbyItem>()), Is.Null);

        [Test] public void OfficialAdditionalPartTypeIsKeptInTheMainMovieSequence()
        {
            var second = new EmbyItem { Id = "cd2", Type = "Video", ExtraType = "AdditionalPart" };
            var sequence = EmbyMovieParts.Create("account", Movie("main"), new[] { second });
            Assert.That(sequence.Items.Select(x => x.Id), Is.EqualTo(new[] { "main", "cd2" }));
            Assert.That(second.IsExtra, Is.False);
        }

        [TestCase("Folder", "")][TestCase("Video", "Clip")][TestCase("Trailer", "")]
        public void FolderOrExtraCannotBecomeTheMainMultipartMovie(string type, string extra)
            => Assert.That(EmbyMovieParts.Create("account", new EmbyItem { Id = "main", Type = type, ExtraType = extra }, new[] { Movie("second") }), Is.Null);

        [TestCase("emby://account/main", 0)][TestCase("emby://account/second", 1)]
        [TestCase("emby://other/second", -1)][TestCase("emby://account/foreign", -1)][TestCase("file:///second.mp4", -1)]
        public void PartLookupRequiresCapturedAccountAndItem(string uri, int expected)
            => Assert.That(EmbyMovieParts.Create("account", Movie("main"), new[] { Movie("second") }).IndexOfUri(uri), Is.EqualTo(expected));

        [Test] public void DetailsMustBeLoadedBeforeTheirPartsCanBeUsed()
        {
            var state = new EmbyBrowserState { page = EmbyPage.Details, selected = Movie("main"), parts = new[] { Movie("main"), Movie("second") }, loading = true };
            Assert.That(EmbyMovieParts.FromDetails("account", state), Is.Null);
            state.loading = false; Assert.That(EmbyMovieParts.FromDetails("account", state).Items.Count, Is.EqualTo(2));
            state.page = EmbyPage.Library; Assert.That(EmbyMovieParts.FromDetails("account", state), Is.Null);
        }

        [Test] public void SameMovieRequiresBothAccountAndMainIdentity()
        {
            var first = EmbyMovieParts.Create("account", Movie("main"), new[] { Movie("second") });
            Assert.That(first.SameMovie(EmbyMovieParts.Create("other", Movie("main"), new[] { Movie("second") })), Is.False);
            Assert.That(first.SameMovie(EmbyMovieParts.Create("account", Movie("other"), new[] { Movie("second") })), Is.False);
            Assert.That(first.SameMovie(EmbyMovieParts.Create("account", Movie("main"), new[] { Movie("second") })), Is.True);
        }
    }

    public sealed class EmbyPartPlaylistTests
    {
        private GameObject root;
        private PlayerApp app;
        private EmbyMovieParts parts;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static EmbyItem Movie(string id) => new EmbyItem { Id = id, Type = "Movie" };
        private static FileEntry Entry(string id) => new FileEntry { uri = EmbyIdentity.Create("account", id), name = id };

        [SetUp] public void SetUp()
        {
            root = new GameObject("Emby multipart regression"); root.SetActive(false); app = root.AddComponent<PlayerApp>();
            parts = EmbyMovieParts.Create("account", Movie("main"), new[] { Movie("second"), Movie("third") });
            typeof(PlayerApp).GetProperty("Current").SetValue(app, new PlaybackRecord { uri = Entry("main").uri });
            typeof(PlayerApp).GetField("embyMovieParts", Private).SetValue(app, parts);
            typeof(PlayerApp).GetField("playlist", Private).SetValue(app, parts.Items.Select(x => Entry(x.Id)).ToList());
        }

        [TearDown] public void TearDown()
        {
            typeof(PlayerApp).GetProperty("Current").SetValue(app, null); UnityEngine.Object.DestroyImmediate(root);
        }

        [Test] public void BrowsingAndCancellingAnotherPageKeepsTheCommittedMovieQueue()
        {
            app.EmbyState.page = EmbyPage.Library; app.EmbyState.parts = Array.Empty<EmbyItem>(); app.CancelEmbyBrowsing();
            Assert.That(app.EmbyPlaybackPartIds, Is.EqualTo(new[] { "main", "second", "third" }));
            Assert.That(app.CanNavigateAdjacent(1)); Assert.That(app.CanNavigateAdjacent(-1), Is.False);
        }

        [Test] public void StartingAnotherEmbyPageCancelsItsOldBrowseButKeepsPendingPlayback()
        {
            var browse = new CancellationTokenSource(); var opening = new CancellationTokenSource();
            var browseToken = browse.Token; var openingToken = opening.Token;
            typeof(PlayerApp).GetField("embyBrowsing", Private).SetValue(app, browse);
            typeof(PlayerApp).GetField("embyOpening", Private).SetValue(app, opening);
            var nextBrowse = (CancellationToken)typeof(PlayerApp).GetMethod("BeginEmbyRequest", Private)
                .Invoke(app, new object[] { new EmbyBrowserState { page = EmbyPage.Home, loading = true } });
            Assert.That(browseToken.IsCancellationRequested); Assert.That(openingToken.IsCancellationRequested, Is.False);
            app.CancelListing();
            Assert.That(nextBrowse.IsCancellationRequested); Assert.That(openingToken.IsCancellationRequested);
        }

        [TestCase(FileSource.Emby, false)]
        [TestCase(FileSource.Recent, true)]
        [TestCase(FileSource.Directory, true)]
        public void EmbySourceButtonPreservesPendingOpenOnlyWhenAlreadyBrowsingEmby(FileSource source, bool openingCancelled)
        {
            var browse = new CancellationTokenSource(); var opening = new CancellationTokenSource(); var listing = new CancellationTokenSource();
            var browseToken = browse.Token; var openingToken = opening.Token; var listingToken = listing.Token;
            typeof(PlayerApp).GetField("embyBrowsing", Private).SetValue(app, browse);
            typeof(PlayerApp).GetField("embyOpening", Private).SetValue(app, opening);
            typeof(PlayerApp).GetField("listing", Private).SetValue(app, listing);
            typeof(PlayerApp).GetProperty("Source").SetValue(app, source);
            app.EmbyState.loading = true;
            app.ShowEmby();
            Assert.That(browseToken.IsCancellationRequested, Is.True); Assert.That(listingToken.IsCancellationRequested, Is.True);
            Assert.That(openingToken.IsCancellationRequested, Is.EqualTo(openingCancelled));
            Assert.That(app.Source, Is.EqualTo(FileSource.Emby)); Assert.That(app.EmbyState.loading, Is.False);
            Assert.That(app.EmbyPlaybackPartIds, Is.EqualTo(new[] { "main", "second", "third" }));
        }

        [TestCase("second", "main")]
        [TestCase("third", "main")]
        [TestCase("ordinary", "ordinary")]
        public void RecentContainingNavigationUsesOnlyOfficialPartMembership(string itemId, string expected)
            => Assert.That(ContainingMain("account", itemId, parts, null), Is.EqualTo(expected));

        [Test] public void RecentContainingNavigationUsesOfficialExtraMembershipAndRejectsAnotherAccount()
        {
            var extras = EmbyMovieExtras.Create("account", "main", new[] { new EmbyItem { Id = "pv", Type = "Video", ExtraType = "Clip" } });
            Assert.That(ContainingMain("account", "pv", null, extras), Is.EqualTo("main"));
            Assert.That(ContainingMain("account", "ordinary", null, extras), Is.EqualTo("ordinary"));
            Assert.That(ContainingMain("other", "pv", null, extras), Is.EqualTo("pv"));
            Assert.That(ContainingMain("other", "second", parts, null), Is.EqualTo("second"));
        }

        private static string ContainingMain(string accountId, string itemId, EmbyMovieParts parts, EmbyMovieExtras extras)
            => (string)typeof(PlayerApp).GetMethod("EmbyContainingMainId", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { accountId, itemId, parts, extras });

        [Test] public void EnteringMultipartMovieFromAnOrdinaryPlaylistReplacesTheQueueWithItsParts()
        {
            typeof(PlayerApp).GetField("embyMovieParts", Private).SetValue(app, null);
            typeof(PlayerApp).GetField("playlist", Private).SetValue(app, new List<FileEntry> { Entry("before"), Entry("main"), Entry("after") });
            Assert.That(Complete(parts, parts.Items.Select(x => Entry(x.Id)).ToList(), true, Entry("main").uri, 0));
            var queue = (List<FileEntry>)typeof(PlayerApp).GetField("playlist", Private).GetValue(app);
            Assert.That(queue.Select(x => x.uri), Is.EqualTo(parts.Items.Select(x => Entry(x.Id).uri)));
            Assert.That(app.CanNavigateAdjacent(-1), Is.False); Assert.That(app.CanNavigateAdjacent(1));
        }

        [TestCase("emby://account/second", true)][TestCase("emby://account/third", true)]
        [TestCase("emby://other/second", false)][TestCase("emby://account/extra", false)]
        public void AutomaticAdvanceInheritsFormatOnlyWithinTheCommittedMovie(string target, bool expected)
            => Assert.That(typeof(PlayerApp).GetMethod("InheritEmbyPartFormat", Private).Invoke(app, new object[] { target }), Is.EqualTo(expected));

        [TestCase(1, "main")][TestCase(0, "other")]
        public void StaleCompletionCannotReplaceTheCommittedQueue(int generation, string itemId)
        {
            bool changed = Complete(null, new List<FileEntry> { Entry("other") }, false, Entry(itemId).uri, generation);
            Assert.That(changed, Is.False); Assert.That(app.EmbyPlaybackPartIds, Is.EqualTo(new[] { "main", "second", "third" }));
        }

        [Test] public void PlayingAnExtraReplacesPartsWithOneStandaloneVideo()
        {
            typeof(PlayerApp).GetProperty("Current").SetValue(app, new PlaybackRecord { uri = Entry("extra").uri });
            Assert.That(Complete(null, new List<FileEntry> { Entry("extra") }, false, Entry("extra").uri, 0));
            Assert.That(app.EmbyPlaybackPartIds, Is.Empty); Assert.That(app.EmbyPlaybackPartIndex, Is.EqualTo(-1));
            Assert.That(app.CanNavigateAdjacent(1), Is.False); Assert.That(app.CanNavigateAdjacent(-1), Is.False);
        }

        [Test] public void AnOrdinaryEmbyMovieCannotNavigateTheLibraryPlaylist()
        {
            typeof(PlayerApp).GetField("embyMovieParts", Private).SetValue(app, null);
            typeof(PlayerApp).GetField("playlist", Private).SetValue(app, new List<FileEntry> { Entry("before"), Entry("main"), Entry("after") });
            Assert.That(app.CanNavigateAdjacent(-1), Is.False); Assert.That(app.CanNavigateAdjacent(1), Is.False);
            Assert.That(Allow("after", false), Is.False); Assert.That(Allow("after", true), Is.False);
        }

        [Test] public void OrdinaryCompletionAlwaysClearsTheFormerMultipartAndLibraryQueue()
        {
            Assert.That(Complete(null, new List<FileEntry> { Entry("before"), Entry("main"), Entry("after") }, true, Entry("main").uri, 0));
            Assert.That(app.EmbyPlaybackQueueKind, Is.EqualTo("None"));
            Assert.That(app.EmbyPlaybackQueueIds, Is.Empty);
            Assert.That(((List<FileEntry>)typeof(PlayerApp).GetField("playlist", Private).GetValue(app)).Select(x => x.uri), Is.EqualTo(new[] { Entry("main").uri }));
        }

        [Test] public void ExtrasAllowExplicitNavigationWithinTheGroupButNeverAutomaticAdvance()
        {
            var extras = EmbyMovieExtras.Create("account", "main", new[] {
                new EmbyItem { Id = "pv", Type = "Video", ExtraType = "Clip" },
                new EmbyItem { Id = "cm", Type = "Video", ExtraType = "Clip" } });
            typeof(PlayerApp).GetProperty("Current").SetValue(app, new PlaybackRecord { uri = Entry("pv").uri });
            Assert.That(Complete(null, new List<FileEntry> { Entry("main"), Entry("pv"), Entry("cm"), Entry("third"), Entry("other") }, false, Entry("pv").uri, 0, extras));
            Assert.That(app.EmbyPlaybackQueueKind, Is.EqualTo("Extras"));
            Assert.That(app.EmbyPlaybackQueueIds, Is.EqualTo(new[] { "pv", "cm" }));
            Assert.That(app.EmbyPlaybackQueueIndex, Is.Zero);
            Assert.That(app.EmbyPlaybackMainId, Is.EqualTo("main"));
            Assert.That(app.CanNavigateAdjacent(1)); Assert.That(app.CanNavigateAdjacent(-1), Is.False);
            Assert.That(Allow("cm", false)); Assert.That(Allow("cm", true), Is.False);
            Assert.That(Allow("main", false), Is.False); Assert.That(Allow("third", false), Is.False);
            Assert.That(Allow("other", false), Is.False);
        }

        [Test] public void LocalVideosRetainTheirOwnDirectoryNavigation()
        {
            typeof(PlayerApp).GetProperty("Current").SetValue(app, new PlaybackRecord { uri = "file:///video/first.mp4" });
            typeof(PlayerApp).GetField("playlist", Private).SetValue(app, new List<FileEntry> {
                new FileEntry { uri = "file:///video/first.mp4" }, new FileEntry { uri = "file:///video/second.mp4" } });
            Assert.That(app.CanNavigateAdjacent(1));
        }

        private bool Allow(string itemId, bool automatic)
            => (bool)typeof(PlayerApp).GetMethod("AllowEmbyAdjacent", Private).Invoke(app, new object[] { Entry(itemId).uri, automatic });

        private bool Complete(EmbyMovieParts value, List<FileEntry> siblings, bool keep, string uri, int generation, EmbyMovieExtras extras = null)
            => (bool)typeof(PlayerApp).GetMethod("CompleteEmbyOpen", Private).Invoke(app, new object[] { value, siblings, keep, uri, generation, extras });
    }

    public sealed class EmbyMovieExtrasTests
    {
        [Test] public void ExtrasKeepOfficialOrderAndExcludeMainPartsDuplicatesAndFolders()
        {
            var clip = new EmbyItem { Id = "pv", Type = "Video" };
            var extras = EmbyMovieExtras.Create("account", "main", new[] { clip, clip, null,
                new EmbyItem { Id = "main", Type = "Movie" },
                new EmbyItem { Id = "cd2", Type = "Video", ExtraType = "AdditionalPart" },
                new EmbyItem { Id = "folder", Type = "Folder" },
                new EmbyItem { Id = "trailer", Type = "Trailer" } });
            Assert.That(extras.Items.Select(x => x.Id), Is.EqualTo(new[] { "pv", "trailer" }));
            Assert.That(extras.IndexOfUri("emby://account/trailer"), Is.EqualTo(1));
            Assert.That(extras.IndexOfUri("emby://other/trailer"), Is.EqualTo(-1));
            Assert.That(extras.IndexOfUri("emby://account/main"), Is.EqualTo(-1));
        }

        [Test] public void EmptyExtrasDoNotCreateAnAdjacentGroup()
            => Assert.That(EmbyMovieExtras.Create("account", "main", Array.Empty<EmbyItem>()), Is.Null);

        [Test] public void ALoadingDetailCannotSupplyAnExtrasGroup()
        {
            var state = new EmbyBrowserState { page = EmbyPage.Details, loading = true,
                selected = new EmbyItem { Id = "main" }, extras = new[] { new EmbyItem { Id = "pv", Type = "Video" } } };
            Assert.That(EmbyMovieExtras.FromDetails("account", state), Is.Null);
            state.loading = false;
            Assert.That(EmbyMovieExtras.FromDetails("account", state).Items.Count, Is.EqualTo(1));
        }
    }
}
