using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class EmbyMultipartResumeTests
    {
        private EmbyItem main, second, third;
        private EmbyMovieParts parts;
        private Dictionary<string, PlaybackRecord> records;

        [SetUp] public void SetUp()
        {
            main = Part("main", 40000, "2026-10-12T10:00:00Z");
            second = Part("second", 20000, "2026-10-12T11:00:00Z");
            third = Part("third", 0, null);
            parts = EmbyMovieParts.Create("account", main, new[] { second, third });
            records = new Dictionary<string, PlaybackRecord>();
        }

        private static EmbyItem Part(string id, long milliseconds, string date)
            => new EmbyItem { Id = id, Type = "Video", RunTimeTicks = 60000L * 10000,
                UserData = new EmbyUserData { PlaybackPositionTicks = milliseconds * 10000, LastPlayedDate = date } };
        private PlaybackRecord Find(string uri) => records.TryGetValue(uri, out var value) ? value : null;
        private void Local(string id, long position, string date)
        {
            string uri = EmbyIdentity.Create("account", id);
            records[uri] = new PlaybackRecord { uri = uri, position = position, lastPlayed = date };
        }

        [Test] public void LatestPlayedPartWinsRatherThanMainOrLargestPosition()
        {
            Assert.That(parts.LastPlayed(Find).Item, Is.SameAs(second));
            Assert.That(parts.LastPlayed(Find).PositionTicks, Is.EqualTo(20000L * 10000));
        }

        [Test] public void ReturningToCd1MakesItTheResumeTargetAgain()
        {
            main.UserData.LastPlayedDate = "2026-10-12T12:00:00Z";
            Assert.That(parts.LastPlayed().Item, Is.SameAs(main));
        }

        [Test] public void NewerLocalHistorySuppliesUnsynchronizedPartAndPosition()
        {
            Local("third", 15000, "2026-10-12T12:00:00Z");
            var target = parts.LastPlayed(Find);
            Assert.That(target.Item, Is.SameAs(third));
            Assert.That(target.PositionTicks, Is.EqualTo(15000L * 10000));
        }

        [Test] public void NewerServerProgressOverridesStaleLocalResume()
        {
            Local("second", 55000, "2026-10-12T10:30:00Z");
            Assert.That(parts.LastPlayed(Find).PositionTicks, Is.EqualTo(20000L * 10000));
        }

        [Test] public void CompletedLatestPartDoesNotResurrectOldLocalPosition()
        {
            second.UserData.PlaybackPositionTicks = 0; second.UserData.Played = true;
            Local("second", 55000, "2026-10-12T10:30:00Z");
            var target = parts.LastPlayed(Find);
            Assert.That(target.Item, Is.SameAs(second));
            Assert.That(target.PositionTicks, Is.Zero);
        }

        [Test] public void DatesCompareByUtcRatherThanTextOrLocalTimezone()
        {
            main.UserData.LastPlayedDate = "2026-10-12T20:00:00+08:00";
            second.UserData.LastPlayedDate = "2026-10-12T11:30:00Z";
            Assert.That(parts.LastPlayed().Item, Is.SameAs(main));
        }

        [Test] public void MissingDatesWithOneSavedPartCanResumeWithoutClaimingLastPlayed()
        {
            main.UserData.LastPlayedDate = null; main.UserData.PlaybackPositionTicks = 0;
            second.UserData.LastPlayedDate = "invalid";
            var target = parts.LastPlayed();
            Assert.That(target.Item, Is.SameAs(second));
            Assert.That(target.HasLastPlayed, Is.False);
        }

        [Test] public void MissingDatesWithMultiplePositionsDoNotGuessLatestCd()
        {
            main.UserData.LastPlayedDate = null; second.UserData.LastPlayedDate = "invalid";
            Assert.That(parts.LastPlayed(), Is.Null);
        }

        [Test] public void ForeignAccountHistoryAndExtrasCannotChooseMovieResume()
        {
            records[EmbyIdentity.Create("other", "third")] = new PlaybackRecord { uri = "emby://other/third", position = 50000, lastPlayed = "2026-10-12T13:00:00Z" };
            var extra = Part("pv", 45000, "2026-10-12T14:00:00Z"); extra.ExtraType = "Clip";
            parts = EmbyMovieParts.Create("account", main, new[] { second, third, extra });
            Assert.That(parts.LastPlayed(Find).Item, Is.SameAs(second));
            Assert.That(parts.ProgressFor(extra, Find), Is.Null);
        }

        [Test] public void RecordLookupMustReturnTheRequestedStableIdentity()
        {
            var wrong = new PlaybackRecord { uri = "emby://other/third", position = 45000, lastPlayed = "2026-10-12T13:00:00Z" };
            Assert.That(parts.LastPlayed(_ => wrong).Item, Is.SameAs(second));
        }

        [Test] public void NegativeAndOversizedProgressRemainWithinPartDuration()
        {
            Local("third", long.MaxValue, "2026-10-12T13:00:00Z");
            Assert.That(parts.LastPlayed(Find).PositionTicks, Is.EqualTo(third.RunTimeTicks));
            Local("third", -100, "2026-10-12T13:00:00Z");
            Assert.That(parts.LastPlayed(Find).PositionTicks, Is.Zero);
        }

        [Test] public void OfficialLastPlayedDateSurvivesJsonParsing()
        {
            var item = JsonUtility.FromJson<EmbyItem>("{\"Id\":\"second\",\"Type\":\"Video\",\"UserData\":{\"LastPlayedDate\":\"2026-10-12T11:00:00Z\",\"PlaybackPositionTicks\":200000000}}");
            Assert.That(item.UserData.LastPlayedDate, Is.EqualTo("2026-10-12T11:00:00Z"));
            Assert.That(EmbyMovieParts.Create("account", main, new[] { item }).LastPlayed().Item.Id, Is.EqualTo("second"));
        }

        [TestCase(3, 0, "account", "third", 37000)]
        [TestCase(2, 0, "account", "second", 20000)]
        [TestCase(3, 1, "account", "second", 20000)]
        [TestCase(3, 0, "other", "second", 20000)]
        public void CurrentPreparedPartOverridesDatesOnlyForMatchingAccountAndGeneration(int state, int generation, string accountId, string expectedId, long expectedPosition)
        {
            var root = new GameObject("Multipart resume state regression"); root.SetActive(false);
            var app = root.AddComponent<PlayerApp>();
            try
            {
                typeof(PlayerApp).GetProperty("ActiveEmbyAccount").SetValue(app, new EmbyAccount { id = "account" });
                var backend = new AndroidMediaBackend();
                backend.Snapshot.state = state; backend.Snapshot.generation = generation; backend.Snapshot.position = 37000;
                typeof(PlayerApp).GetProperty("Backend").SetValue(app, backend);
                typeof(PlayerApp).GetProperty("Current").SetValue(app, new PlaybackRecord { uri = EmbyIdentity.Create(accountId, "third") });
                app.EmbyState.page = EmbyPage.Details; app.EmbyState.selected = main; app.EmbyState.parts = parts.Items.ToArray();
                var target = app.EmbyMovieResume;
                Assert.That(target.Item.Id, Is.EqualTo(expectedId));
                Assert.That(target.PositionTicks / 10000, Is.EqualTo(expectedPosition));
            }
            finally
            {
                typeof(PlayerApp).GetProperty("Current").SetValue(app, null);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(EmbyPage.Person, false)]
        [TestCase(EmbyPage.Details, true)]
        public void OnlyLoadedMovieDetailsOfferGroupResume(EmbyPage page, bool loading)
        {
            var root = new GameObject("Multipart resume page boundary"); root.SetActive(false);
            var app = root.AddComponent<PlayerApp>();
            try
            {
                typeof(PlayerApp).GetProperty("ActiveEmbyAccount").SetValue(app, new EmbyAccount { id = "account" });
                app.EmbyState.page = page; app.EmbyState.loading = loading; app.EmbyState.selected = main; app.EmbyState.parts = parts.Items.ToArray();
                Assert.That(app.EmbyMovieResume, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
