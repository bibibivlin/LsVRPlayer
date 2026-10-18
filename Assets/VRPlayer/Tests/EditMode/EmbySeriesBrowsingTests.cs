using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class EmbySeriesBrowsingTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private PlayerApp app;
        private EmbyBrowserUI browser;
        private SeriesFixture server;
        private string settingsPath;

        [SetUp] public void SetUp()
        {
            server = new SeriesFixture();
            root = new GameObject("Emby series regression"); root.SetActive(false);
            app = root.AddComponent<PlayerApp>(); browser = root.AddComponent<EmbyBrowserUI>();
            browser.Initialize(app, _ => { }, () => { }, () => { });
            typeof(PlayerApp).GetProperty("ActiveEmbyAccount").SetValue(app, server.Account);
            typeof(PlayerApp).GetProperty("EmbyApi").SetValue(app, new EmbyClient("series-regression"));
            typeof(PlayerApp).GetProperty("Source").SetValue(app, FileSource.Emby);
            settingsPath = Path.Combine(Path.GetTempPath(), "VRPlayer-series-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new PlayerStore(settingsPath); store.SetEmbyLibraryView("account", "shows", EmbyLibraryView.Disc);
            typeof(PlayerApp).GetProperty("Store").SetValue(app, store);
            app.EmbyState.page = EmbyPage.Library; app.EmbyState.viewLibraryId = "shows";
        }

        [TearDown] public void TearDown()
        {
            app.CancelEmbyBrowsing(); server.Dispose(); UnityEngine.Object.DestroyImmediate(root);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }

        [UnityTest] public IEnumerator OneRegularSeasonOpensEpisodesWithoutAnIntermediateSeasonPage()
        {
            server.Seasons = new[] { Season("regular", 1) };
            yield return Wait(Load(Series()));
            Assert.That(app.EmbyState.episodesView, Is.True);
            Assert.That(app.EmbyState.parent.Id, Is.EqualTo("series"));
            Assert.That(app.EmbyState.episodeSeason.Id, Is.EqualTo("regular"));
            Assert.That(app.EmbyState.items.Select(item => item.IndexNumber), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(app.EmbyState.specialEpisodes, Is.Empty);
            Assert.That(server.Targets.Any(target => target.Contains("/Shows/series/Episodes?") && target.Contains("SeasonId=regular")), Is.True);
            Assert.That(server.HeaderAuthenticationOnly, Is.True);
        }

        [UnityTest] public IEnumerator SpecialsDoNotCountAsASecondSeasonAndStayBelowRegularEpisodes()
        {
            server.Seasons = new[] { Season("specials", 0), Season("regular", 1) };
            yield return Wait(Load(Series()));
            var state = app.EmbyState;
            Assert.That(state.episodesView, Is.True); Assert.That(state.episodeSeason.Id, Is.EqualTo("regular"));
            Assert.That(state.items.Select(item => item.Id), Is.EqualTo(new[] { "regular-1", "regular-2", "regular-3" }));
            Assert.That(state.specialEpisodes.Select(item => item.Id), Is.EqualTo(new[] { "specials-1", "specials-2", "specials-3" }));
            Assert.That(state.totalCount, Is.EqualTo(6));
            var panel = Draw();
            Assert.That(panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Any(label => label.text == "特别篇"), Is.True);
            Assert.That(panel.GetComponentsInChildren<Button>(true).Any(button => button.name == "Emby view selector"), Is.False);
            var layout = Field<EmbyWallLayout>(browser, "posterLayout");
            Assert.That(layout.ArtworkWidth / layout.ArtworkHeight, Is.EqualTo(16f / 9).Within(.001));
            var cards = panel.GetComponentsInChildren<RowBinding>(true);
            Assert.That(cards.Count(card => card.Item != null), Is.EqualTo(6));
            var firstSpecial = cards.Single(card => card.Item?.key == "account:specials-1").GetComponent<RectTransform>();
            Assert.That(-firstSpecial.anchoredPosition.y, Is.GreaterThanOrEqualTo(58 + layout.Pitch + 74));
            Assert.That(app.Store.GetEmbyLibraryView("account", "shows"), Is.EqualTo(EmbyLibraryView.Disc));
        }

        [UnityTest] public IEnumerator MultipleSeasonsKeepServerOrderAndEachSeasonOpensOnlyItsEpisodes()
        {
            server.Seasons = new[] { Season("specials", 0), Season("regular", 1), Season("second", 2) };
            yield return Wait(Load(Series()));
            var seasonsPage = app.EmbyState;
            Assert.That(seasonsPage.episodesView, Is.False);
            Assert.That(seasonsPage.items.Select(item => item.Id), Is.EqualTo(new[] { "specials", "regular", "second" }));
            Assert.That(server.Targets.Any(target => target.Contains("/Episodes?")), Is.False);
            var panel = Draw();
            var layout = Field<EmbyWallLayout>(browser, "posterLayout");
            Assert.That(layout.ArtworkWidth / layout.ArtworkHeight, Is.EqualTo(2f / 3).Within(.001));
            Assert.That(panel.GetComponentsInChildren<Button>(true).Any(button => button.name == "Emby view selector"), Is.False);
            foreach (var season in seasonsPage.items)
            {
                var artwork = EmbyLibraryViews.Artwork(season, EmbyLibraryView.Disc);
                Assert.That(artwork.ItemId, Is.EqualTo("series")); Assert.That(artwork.Tag, Is.EqualTo("show-poster"));
            }
            // A TV season's physical parent is not authoritative when SeriesId is present.
            var second = seasonsPage.items[2]; second.ParentId = "physical-folder";
            app.OpenEmbyItem(second);
            yield return WaitUntilLoaded();
            Assert.That(app.EmbyState.episodesView, Is.True);
            Assert.That(app.EmbyState.items.Select(item => item.Id), Is.EqualTo(new[] { "second-1", "second-2", "second-3" }));
            Assert.That(server.Targets.Last(), Does.Contain("/Shows/series/Episodes?").And.Contain("SeasonId=second"));
            app.EmbyBack(); Assert.That(app.EmbyState, Is.SameAs(seasonsPage));
        }

        [UnityTest] public IEnumerator EpisodePaginationKeepsNumericServerOrderInsteadOfAlphabeticTitles()
        {
            server.EpisodeCount = 205;
            var loading = app.EmbyApi.GetEpisodes(server.Account, "series", "regular", "a b");
            yield return Wait(loading);
            Assert.That(loading.Result.TotalRecordCount, Is.EqualTo(205));
            Assert.That(loading.Result.Items.Select(item => item.IndexNumber), Is.EqualTo(Enumerable.Range(1, 205)));
            Assert.That(server.Targets.Count, Is.EqualTo(2));
            Assert.That(server.Targets.Last(), Does.Contain("StartIndex=200").And.Contain("SearchTerm=a%20b"));
            Assert.That(server.Targets.Any(target => target.Contains("SortBy=SortName")), Is.False);
        }

        [UnityTest] public IEnumerator BackDuringSingleSeasonExpansionDiscardsTheRetiredEpisodes()
        {
            server.Seasons = new[] { Season("regular", 1) }; server.HoldEpisodes = true;
            var previous = app.EmbyState;
            ((Stack<EmbyBrowserState>)Field<object>(app, "embyPages")).Push(previous);
            var loading = Load(Series());
            yield return Wait(server.Held.Task);
            app.EmbyBack(); Assert.That(app.EmbyState, Is.SameAs(previous));
            server.Release.TrySetResult(true); yield return Wait(loading);
            Assert.That(app.EmbyState, Is.SameAs(previous)); Assert.That(app.EmbyState.items, Is.Empty);
        }

        [UnityTest] public IEnumerator LongSingleSeasonRecyclesCardsAcrossTheSeparateSpecialsSection()
        {
            server.Seasons = new[] { Season("specials", 0), Season("regular", 1) }; server.EpisodeCount = 205;
            yield return Wait(Load(Series()));
            var panel = Draw(); var layout = Field<EmbyWallLayout>(browser, "posterLayout");
            int pool = panel.GetComponentsInChildren<RowBinding>(true).Length;
            Assert.That(pool, Is.LessThan(40));
            var body = Field<RectTransform>(browser, "body");
            body.anchoredPosition = new Vector2(0, 58 + Mathf.CeilToInt(205f / layout.Columns) * layout.Pitch);
            typeof(EmbyBrowserUI).GetMethod("UpdatePosterWall", Private).Invoke(browser, null);
            var cards = panel.GetComponentsInChildren<RowBinding>(true);
            Assert.That(cards.Length, Is.EqualTo(pool));
            Assert.That(cards.Any(card => card.Item?.key == "account:specials-1"), Is.True);
            Assert.That(cards.Where(card => card.Item != null).Select(card => card.Item.key).Distinct().Count(), Is.EqualTo(cards.Count(card => card.Item != null)));
        }

        [Test] public void ReturningFromSpecialEpisodeDetailsRetainsItsUpdatedPlayedState()
        {
            var episode = new EmbyItem { Id = "special", Type = "Episode" };
            var previous = new EmbyBrowserState { specialEpisodes = new[] { episode } };
            ((Stack<EmbyBrowserState>)Field<object>(app, "embyPages")).Push(previous);
            var data = new EmbyUserData { Played = true };
            typeof(PlayerApp).GetMethod("ApplyEmbyPlayedData", Private).Invoke(app, new object[] { "account", "special", data });
            app.EmbyBack(); Assert.That(app.EmbyState.specialEpisodes[0].UserData, Is.SameAs(data));
        }

        [Test] public void SeasonsPreferTheirOwnPosterAndFallbackUsesTheSeriesImageOwner()
        {
            var item = EmbyClient.ParseItems("[{\"Id\":\"season\",\"Type\":\"Season\",\"SeriesId\":\"series\",\"SeriesPrimaryImageTag\":\"series-tag\",\"ImageTags\":{\"Primary\":\"season-tag\"}}]").Items[0];
            var own = EmbyLibraryViews.Artwork(item, EmbyLibraryView.Banner);
            Assert.That(own.ItemId, Is.EqualTo("season")); Assert.That(own.Type, Is.EqualTo("Primary")); Assert.That(own.Tag, Is.EqualTo("season-tag"));
            item.ImageTags.Primary = null;
            var fallback = EmbyLibraryViews.Artwork(item, EmbyLibraryView.Poster);
            Assert.That(fallback.ItemId, Is.EqualTo("series")); Assert.That(fallback.Tag, Is.EqualTo("series-tag"));
            item.SeriesPrimaryImageTag = null;
            Assert.That(EmbyLibraryViews.Artwork(item, EmbyLibraryView.Poster).Available, Is.False);
        }

        [Test] public void EpisodesUseTheirOwnStillBeforeThumbAndNeverInheritTheSeriesPoster()
        {
            var item = new EmbyItem { Id = "episode", Type = "Episode", SeriesId = "series", SeriesPrimaryImageTag = "show",
                ImageTags = new EmbyImageTags { Primary = "episode-still", Thumb = "thumb" }, BackdropImageTags = new[] { "backdrop" } };
            Assert.That(EmbyLibraryViews.Artwork(item, EmbyLibraryView.Thumb).Tag, Is.EqualTo("episode-still"));
            item.ImageTags.Primary = null;
            Assert.That(EmbyLibraryViews.Artwork(item, EmbyLibraryView.Poster).Type, Is.EqualTo("Thumb"));
            item.ImageTags.Thumb = null;
            Assert.That(EmbyLibraryViews.Artwork(item, EmbyLibraryView.Thumb).Available, Is.False);
        }

        private static EmbyItem Series() => new EmbyItem { Id = "series", Name = "示例剧集", Type = "Series", ImageTags = new EmbyImageTags { Primary = "show-poster" } };
        private static EmbyItem Season(string id, int number) => new EmbyItem { Id = id, Name = number == 0 ? "Specials" : "第 " + number + " 季", Type = "Season", SeriesId = "series", IndexNumber = number };
        private Task Load(EmbyItem parent) => (Task)typeof(PlayerApp).GetMethod("LoadEmbyLibrary", Private).Invoke(app, new object[] { parent, 0, "" });
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private RectTransform Draw()
        {
            var panel = new GameObject("Emby panel", typeof(RectTransform)).GetComponent<RectTransform>(); panel.SetParent(root.transform, false); panel.sizeDelta = new Vector2(1376, 786);
            browser.Bind(panel);
            var build = typeof(EmbyBrowserUI).GetMethod("BuildNextCard", Private);
            while ((bool)build.Invoke(browser, null)) { }
            return panel;
        }
        private IEnumerator WaitUntilLoaded()
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (app.EmbyState.loading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(app.EmbyState.loading, Is.False); Assert.That(app.EmbyState.error, Is.Empty);
        }
        private static IEnumerator Wait(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Series request timed out");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
            Assert.That(task.IsCanceled, Is.False);
        }

        private sealed class SeriesFixture : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly List<TcpClient> clients = new List<TcpClient>();
            public readonly List<string> Targets = new List<string>();
            public readonly TaskCompletionSource<bool> Held = new TaskCompletionSource<bool>(), Release = new TaskCompletionSource<bool>();
            public EmbyItem[] Seasons = Array.Empty<EmbyItem>();
            public int EpisodeCount = 3;
            public bool HoldEpisodes, HeaderAuthenticationOnly = true;
            public EmbyAccount Account { get; }
            public SeriesFixture()
            {
                listener.Start(); Account = new EmbyAccount { id = "account", userId = "viewer", accessToken = "fixture-token", serverUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port };
                _ = Accept();
            }
            private async Task Accept()
            {
                try { while (true) { var client = await listener.AcceptTcpClientAsync(); clients.Add(client); _ = Handle(client); } }
                catch (ObjectDisposedException) { } catch (SocketException) { }
            }
            private async Task Handle(TcpClient client)
            {
                try
                {
                    using (client)
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    {
                        string first = await reader.ReadLineAsync(); if (first == null) return;
                        bool authenticated = false;
                        for (string line = await reader.ReadLineAsync(); !string.IsNullOrEmpty(line); line = await reader.ReadLineAsync())
                            if (line.StartsWith("X-Emby-Token:", StringComparison.OrdinalIgnoreCase)) authenticated = line.Substring(13).Trim() == Account.accessToken;
                        string target = first.Split(' ')[1]; Targets.Add(target);
                        HeaderAuthenticationOnly &= authenticated && !target.Contains(Account.accessToken) && !target.Contains("api_key");
                        EmbyItemsResult result;
                        if (target.Contains("/Seasons?")) result = new EmbyItemsResult { Items = Seasons, TotalRecordCount = Seasons.Length };
                        else
                        {
                            if (HoldEpisodes) { Held.TrySetResult(true); await Release.Task; }
                            string seasonId = target.Contains("SeasonId=specials") ? "specials" : target.Contains("SeasonId=second") ? "second" : "regular";
                            int start = target.Contains("StartIndex=200") ? 200 : 0;
                            result = new EmbyItemsResult { TotalRecordCount = EpisodeCount, Items = Enumerable.Range(1, EpisodeCount).Skip(start).Take(200).Select(index => new EmbyItem
                            {
                                Id = seasonId + "-" + index, Name = (EpisodeCount - index).ToString("D3") + " 示例集", Type = "Episode", IndexNumber = index,
                                ImageTags = new EmbyImageTags { Primary = "still-" + index }, RunTimeTicks = TimeSpan.FromMinutes(24).Ticks
                            }).ToArray() };
                        }
                        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(result));
                        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                        await stream.WriteAsync(header, 0, header.Length); await stream.WriteAsync(body, 0, body.Length);
                    }
                }
                catch (IOException) { } catch (ObjectDisposedException) { } catch (SocketException) { }
            }
            public void Dispose() { Release.TrySetResult(true); listener.Stop(); foreach (var client in clients) client.Dispose(); }
        }
    }
}
