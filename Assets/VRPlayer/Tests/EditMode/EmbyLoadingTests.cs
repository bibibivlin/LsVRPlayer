using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRPlayer.Tests
{
    public sealed class EmbyLoadingTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test] public async Task ResponseParsingLeavesTheMainThreadFreeAndReturnsToItsCaller()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId, parsingThread = mainThread;
            var entered = new TaskCompletionSource<bool>();
            using var release = new ManualResetEventSlim();
            var processing = Decode("\uFEFF[{\"Id\":\"film\",\"Name\":\"电影\",\"GenreItems\":[{\"Name\":\"剧情\"}]}]", json =>
            {
                parsingThread = Thread.CurrentThread.ManagedThreadId;
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Parser was not released by its caller");
                return EmbyClient.ParseItems(json);
            });
            try
            {
                Assert.That(await Task.WhenAny(entered.Task, Task.Delay(5000)), Is.SameAs(entered.Task));
                Assert.That(parsingThread, Is.Not.EqualTo(mainThread));
                Assert.That(processing.IsCompleted, Is.False);
                // These continuations must keep running while parsing is deliberately held.
                for (int i = 0; i < 3; i++) { await Task.Yield(); Assert.That(processing.IsCompleted, Is.False); }
            }
            finally { release.Set(); }
            var result = await processing;
            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
            Assert.That(result.Items[0].Name, Is.EqualTo("电影"));
            Assert.That(result.Items[0].Genres, Is.EqualTo(new[] { "剧情" }));
            Assert.That(result.Items[0].UserData, Is.Not.Null);
        }

        [Test] public async Task CancelledResponseNeverStartsParsing()
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool parsed = false;
            try { await Decode("[]", json => { parsed = true; return EmbyClient.ParseItems(json); }, cancellation.Token); Assert.Fail("Cancelled response must not parse"); }
            catch (OperationCanceledException) { }
            Assert.That(parsed, Is.False);
        }

        [Test] public async Task CancellationDuringParsingDiscardsTheCompletedDto()
        {
            using var cancellation = new CancellationTokenSource();
            try { await Decode("[]", json => { var result = EmbyClient.ParseItems(json); cancellation.Cancel(); return result; }, cancellation.Token); Assert.Fail("Cancelled parsing must not return a DTO"); }
            catch (OperationCanceledException) { }
        }

        [Test] public async Task BackgroundParserKeepsSafeMalformedResponseErrors()
        {
            try { await Decode("invalid JSON", EmbyClient.ParseItems); Assert.Fail("Malformed JSON must fail"); }
            catch (InvalidOperationException error) { Assert.That(EmbyClient.SafeError(error), Is.EqualTo("Emby 返回的数据无法识别，请重试")); }
        }

        [UnityTest] public IEnumerator LibraryPersonAndCategoryLoadEveryPageWithoutBlockingEditorUpdates()
        {
            using var server = new LibraryFixture();
            var api = new EmbyClient("loading-regression"); var account = server.Account;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            var requests = new Func<Task<EmbyItemsResult>>[]
            {
                () => api.GetAllItems(account, new EmbyItem { Id = "library", CollectionType = "movies" }),
                () => api.GetPersonMovies(account, "person"),
                () => api.GetFilteredMovies(account, "Genre", "剧情")
            };
            foreach (var request in requests)
            {
                server.ResetHold();
                var loading = request();
                yield return Wait(server.Held.Task);
                for (int frame = 0; frame < 4; frame++)
                {
                    Assert.That(loading.IsCompleted, Is.False);
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                    yield return null;
                }
                server.Release.TrySetResult(true);
                yield return Wait(loading);
                Assert.That(loading.Result.Items.Length, Is.EqualTo(405));
                Assert.That(loading.Result.TotalRecordCount, Is.EqualTo(405));
                Assert.That(loading.Result.Items.Select(item => item.Id), Is.EqualTo(Enumerable.Range(0, 405).Select(index => index.ToString("D3"))));
                Assert.That(loading.Result.Items[0].Overview.Length, Is.EqualTo(2048));
            }
            Assert.That(server.HeaderAuthenticationOnly, Is.True);
        }

        [UnityTest] public IEnumerator BackWhileLoadingReturnsImmediatelyAndDiscardsTheRetiredLibrary()
        {
            using var server = new LibraryFixture();
            var root = new GameObject("Emby loading cancellation regression"); root.SetActive(false);
            var app = root.AddComponent<PlayerApp>(); var browser = root.AddComponent<EmbyBrowserUI>();
            browser.Initialize(app, _ => { }, () => { }, () => { });
            var previous = new EmbyBrowserState { page = EmbyPage.Home, title = "Previous page" };
            try
            {
                typeof(PlayerApp).GetProperty("ActiveEmbyAccount").SetValue(app, server.Account);
                typeof(PlayerApp).GetProperty("EmbyApi").SetValue(app, new EmbyClient("loading-back-regression"));
                ((Stack<EmbyBrowserState>)typeof(PlayerApp).GetField("embyPages", Private).GetValue(app)).Push(previous);
                var loading = (Task)typeof(PlayerApp).GetMethod("LoadEmbyLibrary", Private).Invoke(app,
                    new object[] { new EmbyItem { Id = "library", Type = "CollectionFolder", CollectionType = "movies" }, 0, "" });
                yield return Wait(server.Held.Task);
                Assert.That(app.EmbyState.loading, Is.True);
                Assert.That(browser.TryBack(), Is.True);
                Assert.That(app.EmbyState, Is.SameAs(previous));
                server.Release.TrySetResult(true);
                yield return Wait(loading);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(app.EmbyState, Is.SameAs(previous));
                Assert.That(app.EmbyState.error, Is.Empty);
                Assert.That(app.EmbyState.items, Is.Empty);
            }
            finally { app.CancelEmbyBrowsing(); UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Task<EmbyItemsResult> Decode(string json, Func<string, EmbyItemsResult> parse, CancellationToken token = default)
        {
            // Use a nonzero offset to ensure decoding honors the completed download's valid range.
            byte[] bytes = Encoding.UTF8.GetBytes("prefix" + json + "suffix");
            var payload = new ArraySegment<byte>(bytes, 6, bytes.Length - 12);
            return (Task<EmbyItemsResult>)typeof(EmbyClient).GetMethod("DecodeResponse", BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(typeof(EmbyItemsResult)).Invoke(null, new object[] { payload, parse, token });
        }

        private static IEnumerator Wait(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Emby loading timed out");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
            Assert.That(task.IsCanceled, Is.False);
        }

        private sealed class LibraryFixture : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly List<TcpClient> clients = new List<TcpClient>();
            private readonly string[] pages;
            public TaskCompletionSource<bool> Held { get; private set; }
            public TaskCompletionSource<bool> Release { get; private set; }
            public EmbyAccount Account { get; }
            public bool HeaderAuthenticationOnly { get; private set; } = true;

            public LibraryFixture()
            {
                var items = Enumerable.Range(0, 405).Select(index => new EmbyItem
                {
                    Id = index.ToString("D3"), Name = "电影 " + index, Type = "Movie", SortName = index.ToString("D3"),
                    Overview = new string('文', 2048)
                }).Reverse().ToArray();
                pages = Enumerable.Range(0, 3).Select(page => JsonUtility.ToJson(new EmbyItemsResult
                    { Items = items.Skip(page * 200).Take(200).ToArray(), TotalRecordCount = 405 })).ToArray();
                listener.Start();
                Account = new EmbyAccount { id = "account", userId = "user", accessToken = "fixture-header-token",
                    serverUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port };
                ResetHold(); _ = Accept();
            }

            public void ResetHold() { Held = new TaskCompletionSource<bool>(); Release = new TaskCompletionSource<bool>(); }
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
                        string target = first.Split(' ')[1];
                        HeaderAuthenticationOnly &= authenticated && !target.Contains(Account.accessToken) && !target.Contains("api_key");
                        int page = target.Contains("StartIndex=400") ? 2 : target.Contains("StartIndex=200") ? 1 : 0;
                        if (page == 1) { Held.TrySetResult(true); await Release.Task; }
                        byte[] body = Encoding.UTF8.GetBytes(pages[page]);
                        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                        await stream.WriteAsync(header, 0, header.Length);
                        await stream.WriteAsync(body, 0, body.Length);
                    }
                }
                catch (IOException) { } catch (ObjectDisposedException) { } catch (SocketException) { }
            }

            public void Dispose() { Release.TrySetResult(true); listener.Stop(); foreach (var client in clients) client.Dispose(); }
        }
    }
}
