using System;
using System.Collections;
using System.Collections.Concurrent;
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

namespace VRPlayer.Tests
{
    public sealed class EmbyPlayedStateTests
    {
        private GameObject root;
        private PlayerApp app;
        private Fixture server;
        private string folder;
        private EmbyAccount account;
        private static void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
        private static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        [SetUp] public void SetUp()
        {
            server = new Fixture();
            root = new GameObject("Emby watched fixture"); root.SetActive(false); app = root.AddComponent<PlayerApp>();
            folder = Path.Combine(Path.GetTempPath(), "VRPlayer-played-" + Guid.NewGuid().ToString("N"));
            Set(app, "Store", new PlayerStore(Path.Combine(folder, "settings.json")));
            Set(app, "EmbyApi", new EmbyClient("played-fixture"));
            account = new EmbyAccount { id = "account", userId = "viewer", serverUrl = server.Url, accessToken = "fixture-header-token" };
            Set(app, "ActiveEmbyAccount", account); Set(app, "Source", FileSource.Emby); Set(app, "Generation", 7);
            app.EmbyState.page = EmbyPage.Home;
            app.EmbyState.resume = new[] { server.Item("main"), server.Item("cd2"), server.Item("cd3"), server.Item("episode"), server.Item("extra") };
            app.EmbyState.latest = new[] { server.Item("main") };
            foreach (var item in app.EmbyState.resume)
            {
                var record = app.Store.Touch(new FileEntry { name = item.Name, uri = EmbyIdentity.Create(account.id, item.Id) }, "emby://account/main");
                record.position = 30000; record.subtitleSelection = SubtitleSelection.Off; record.view.distance = 2.5f; record.formatLocked = true;
            }
        }

        [TearDown] public void TearDown()
        {
            Field(app, "embyPlayback", null); server.Dispose(); UnityEngine.Object.DestroyImmediate(root);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        private static IEnumerator Wait(Task task)
        {
            float end = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Loopback request timed out");
            if (task.IsFaulted) throw task.Exception;
        }

        [UnityTest] public IEnumerator MarkingAnyMovieCdUpdatesEveryOfficialPartAndDrainsOldProgress()
        {
            var details = new EmbyBrowserState { page = EmbyPage.Details, selected = server.Item("main"), parts = new[] { server.Item("main"), server.Item("cd2"), server.Item("cd3") } };
            Field<Stack<EmbyBrowserState>>(app, "embyPages").Push(details);
            var backend = new AndroidMediaBackend(); Set(app, "Backend", backend);
            backend.Snapshot.generation = 7; backend.Snapshot.state = 3; backend.Snapshot.position = 30000; backend.Snapshot.duration = 60000;
            Set(app, "Current", app.Store.Find("emby://account/cd2"));
            Field(app, "embyPlayback", new EmbyPlayback { item = server.Item("cd2"), source = new EmbyMediaSource { Id = "source" }, playSessionId = "fixture-session" });
            Field(app, "embyPlaybackAccount", account); Field(app, "embyPlaybackGeneration", 7); Field(app, "embyReportedStart", true);
            var oldReport = new TaskCompletionSource<bool>(); Field(app, "embyReports", oldReport.Task);
            var change = app.SetEmbyPlayed(app.EmbyState.resume[1], true);
            yield return null;
            Assert.That(app.EmbyPlayedBusy, Is.True);
            yield return Wait(app.SetEmbyPlayed(app.EmbyState.resume[0], true));
            Assert.That(server.Writes, Is.Empty);
            oldReport.SetResult(true); yield return Wait(change);
            Assert.That(server.Writes, Is.EqualTo(new[] { "POST cd2", "POST cd3", "POST main" }));
            Assert.That(server.Requests.Any(request => request.StartsWith("POST /Sessions/Playing/Stopped")), Is.True);
            Assert.That(server.HeaderAuthenticationOnly, Is.True);
            Assert.That(app.EmbyState.resume.Select(item => item.Id), Is.EqualTo(new[] { "episode", "extra" }));
            Assert.That(details.parts.All(item => item.UserData.Played && item.UserData.PlaybackPositionTicks == 0), Is.True);
            Assert.That(app.EmbyState.latest[0].UserData.Played, Is.True);
            foreach (string id in new[] { "main", "cd2", "cd3" })
            {
                var record = app.Store.Find("emby://account/" + id);
                Assert.That(record.position, Is.Zero); Assert.That(record.subtitleSelection, Is.EqualTo(SubtitleSelection.Off));
                Assert.That(record.view.distance, Is.EqualTo(2.5f)); Assert.That(record.formatLocked, Is.True);
            }
            app.SaveProgress(); Assert.That(app.Current.position, Is.Zero);
            app.EmbyBack(); Assert.That(app.EmbyMovieResume, Is.Null);
            app.Pause(); yield return Wait(Field<Task>(app, "embyReports"));
            Assert.That(server.Requests.Count(request => request.Contains("/Sessions/")), Is.EqualTo(1), "Old playback must not recreate resume progress");
            Set(app, "Generation", 8); backend.Snapshot.generation = 8; app.SaveProgress();
            Assert.That(app.Current.position, Is.EqualTo(30000), "A new playback records progress normally");
        }

        [UnityTest] public IEnumerator UnwatchingMovieClearsAllCdResumePositionsAndRetainsOtherItems()
        {
            foreach (string id in new[] { "main", "cd2", "cd3" }) server.SetInitiallyPlayed(id);
            yield return Wait(app.SetEmbyPlayed(app.EmbyState.resume[0], false));
            Assert.That(server.Writes, Is.EqualTo(new[] { "DELETE cd2", "DELETE cd3", "DELETE main" }));
            Assert.That(app.EmbyState.latest[0].UserData.Played, Is.False);
            Assert.That(app.EmbyState.resume.Select(item => item.Id), Is.EqualTo(new[] { "episode", "extra" }));
            Assert.That(app.Store.Find("emby://account/extra").position, Is.EqualTo(30000));
            var reload = new PlayerStore(Path.Combine(folder, "settings.json"));
            Assert.That(reload.Find("emby://account/cd3").position, Is.Zero);
        }

        [UnityTest] public IEnumerator EpisodeAndExtraMarkOnlyTheSelectedItemWithoutExpandingTheMovieOrSeason()
        {
            yield return Wait(app.SetEmbyPlayed(app.EmbyState.resume.Single(item => item.Id == "episode"), true));
            yield return Wait(app.SetEmbyPlayed(app.EmbyState.resume.Single(item => item.Id == "extra"), true));
            Assert.That(server.Writes, Is.EqualTo(new[] { "POST episode", "POST extra" }));
            Assert.That(server.Requests.Any(request => request.Contains("AdditionalParts")), Is.False);
            Assert.That(app.EmbyState.resume.Select(item => item.Id), Is.EqualTo(new[] { "main", "cd2", "cd3" }));
        }

        [UnityTest] public IEnumerator PartialFailureKeepsMainInResumeAndOffersAnHonestRetry()
        {
            server.FailId = "cd3";
            yield return Wait(app.SetEmbyPlayed(app.EmbyState.resume[0], true));
            Assert.That(app.EmbyState.resume.Any(item => item.Id == "main"), Is.True);
            Assert.That(app.EmbyState.playedError, Does.Contain("部分 CD")); Assert.That(app.EmbyPlayedBusy, Is.False);
            Assert.That(app.EmbyState.latest[0].UserData.Played, Is.False);
            server.FailId = "";
            yield return Wait(app.SetEmbyPlayed(app.EmbyState.resume[0], true));
            Assert.That(app.EmbyState.resume.Any(item => item.Id == "main" || item.IsAdditionalPart), Is.False);
            Assert.That(app.EmbyState.playedError, Is.Empty);
        }

        [UnityTest] public IEnumerator AccountSwitchCancelsRemainingWritesAndCannotOverwriteTheNewPage()
        {
            server.HoldId = "cd2";
            var change = app.SetEmbyPlayed(app.EmbyState.resume[0], true);
            yield return Wait(server.Held.Task);
            Set(app, "ActiveEmbyAccount", new EmbyAccount { id = "other" });
            var next = new EmbyBrowserState { page = EmbyPage.Library, title = "Other account", items = new[] { server.Item("main") } };
            app.CancelEmbyBrowsing(); Set(app, "EmbyState", next); server.Release.TrySetResult(true);
            yield return Wait(change);
            Assert.That(app.EmbyState, Is.SameAs(next)); Assert.That(next.error, Is.Empty); Assert.That(next.playedError, Is.Empty);
            Assert.That(next.items[0].UserData.Played, Is.False); Assert.That(server.Writes, Does.Not.Contain("POST main"));
            Assert.That(app.EmbyPlayedBusy, Is.False); Assert.That(app.Store.Find("emby://account/main").position, Is.EqualTo(30000));
        }

        [UnityTest] public IEnumerator UnverifiedAdditionalPartCannotMarkItsParentOrSibling()
        {
            var orphan = server.Item("orphan"); app.EmbyState.resume = new[] { orphan };
            yield return Wait(app.SetEmbyPlayed(orphan, true));
            Assert.That(server.Writes, Is.Empty); Assert.That(app.EmbyState.playedError, Is.Not.Empty);
        }

        // Loopback HTTP verifies the actual authenticated API methods, response parsing, ordering and cancellation.
        private sealed class Fixture : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly ConcurrentBag<TcpClient> clients = new ConcurrentBag<TcpClient>();
            private readonly Dictionary<string, EmbyItem> items = new Dictionary<string, EmbyItem>();
            public readonly ConcurrentQueue<string> Requests = new ConcurrentQueue<string>(), Writes = new ConcurrentQueue<string>();
            public readonly TaskCompletionSource<bool> Held = new TaskCompletionSource<bool>(), Release = new TaskCompletionSource<bool>();
            public string FailId = "", HoldId = "";
            public bool HeaderAuthenticationOnly = true;
            public string Url { get; }
            public Fixture()
            {
                foreach (string id in new[] { "main", "cd2", "cd3", "extra", "episode", "orphan" })
                    items[id] = new EmbyItem { Id = id, Name = id, Type = id == "main" ? "Movie" : id == "episode" ? "Episode" : "Video",
                        ParentId = "main", ExtraType = id.StartsWith("cd") || id == "orphan" ? "AdditionalPart" : id == "extra" ? "Clip" : "",
                        RunTimeTicks = TimeSpan.FromMinutes(1).Ticks, UserData = new EmbyUserData { PlaybackPositionTicks = 300000000, LastPlayedDate = "2026-10-16T01:00:00Z" } };
                listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port; _ = Accept();
            }
            public EmbyItem Item(string id) => JsonUtility.FromJson<EmbyItem>(JsonUtility.ToJson(items[id]));
            public void SetInitiallyPlayed(string id) => items[id].UserData.Played = true;
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
                        string[] request = first.Split(' '); string method = request[0], target = request[1];
                        int length = 0; bool authenticated = false;
                        for (string line = await reader.ReadLineAsync(); !string.IsNullOrEmpty(line); line = await reader.ReadLineAsync())
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(15).Trim(), out length);
                            if (line.StartsWith("X-Emby-Token:", StringComparison.OrdinalIgnoreCase)) authenticated = line.Substring(13).Trim() == "fixture-header-token";
                        }
                        while (length > 0) { var chars = new char[length]; int read = await reader.ReadAsync(chars, 0, length); if (read <= 0) break; length -= read; }
                        HeaderAuthenticationOnly &= authenticated && !target.Contains("fixture-header-token") && !target.Contains("api_key");
                        Requests.Enqueue(method + " " + target);
                        string path = new Uri(Url + target).AbsolutePath, id = path.Split('/').Last(), json = "{}"; int status = 200;
                        if (path.Contains("/PlayedItems/"))
                        {
                            if (id == HoldId) { Held.TrySetResult(true); await Release.Task; }
                            if (id == FailId) status = 500;
                            else
                            {
                                Writes.Enqueue(method + " " + id);
                                var data = items[id].UserData; data.Played = method == "POST"; data.PlaybackPositionTicks = 0; data.PlayedPercentage = data.Played ? 100 : 0;
                                data.LastPlayedDate = "2026-10-17T01:00:00Z"; json = JsonUtility.ToJson(data);
                            }
                        }
                        else if (id == "AdditionalParts") json = JsonUtility.ToJson(new EmbyItemsResult { Items = new[] { Item("cd2"), Item("cd3"), Item("extra"), Item("episode") }, TotalRecordCount = 4 });
                        else if (items.ContainsKey(id)) json = JsonUtility.ToJson(items[id]);
                        else if (!path.StartsWith("/Sessions/")) status = 404;
                        byte[] body = Encoding.UTF8.GetBytes(json), header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Result\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, 0, header.Length); await stream.WriteAsync(body, 0, body.Length);
                    }
                }
                catch (IOException) { } catch (ObjectDisposedException) { } catch (SocketException) { }
            }
            public void Dispose() { listener.Stop(); Release.TrySetResult(true); foreach (var client in clients) client.Dispose(); }
        }
    }
}
