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
    public sealed class EmbyLibrarySortingTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject root;
        private PlayerApp app;
        private EmbyBrowserUI browser;
        private SortFixture server;
        private string settingsPath;
        private AppLanguage previousLanguage;

        [SetUp] public void SetUp()
        {
            previousLanguage=Localization.Preference;Localization.SetLanguage(AppLanguage.Chinese);
            server=new SortFixture();root=new GameObject("Emby sort regression");root.SetActive(false);
            app=root.AddComponent<PlayerApp>();browser=root.AddComponent<EmbyBrowserUI>();browser.Initialize(app,_=>{},()=>{},()=>{});
            settingsPath=Path.Combine(Path.GetTempPath(),"VRPlayer-emby-sort-"+Guid.NewGuid().ToString("N")+".json");
            Set("Store",new PlayerStore(settingsPath));Set("EmbyApi",new EmbyClient("sort-regression"));
            Set("ActiveEmbyAccount",server.Account);Set("Source",FileSource.Emby);
            app.EmbyState.page=EmbyPage.Home;
        }

        [TearDown] public void TearDown()
        {
            app.CancelEmbyBrowsing();server.Dispose();UnityEngine.Object.DestroyImmediate(root);
            if(File.Exists(settingsPath))File.Delete(settingsPath);
            Localization.SetLanguage(previousLanguage);
        }

        [UnityTest] public IEnumerator EverySortKeepsTheServerOrderAcrossAllMovieAndSeriesPages()
        {
            foreach(string library in new[]{"movies","shows"})
            foreach(EmbyLibrarySort sort in Enum.GetValues(typeof(EmbyLibrarySort)))
            foreach(bool descending in new[]{false,true}){
                int first=server.Targets.Count;
                var loading=app.EmbyApi.GetAllItemsSorted(server.Account,Library(library),"title & clip",sort,descending);
                yield return Wait(loading);
                Assert.That(loading.Result.Items.Select(item=>item.Id),Is.EqualTo(SortFixture.Expected(sort,descending)));
                Assert.That(loading.Result.TotalRecordCount,Is.EqualTo(205));
                var targets=server.Targets.Skip(first).ToArray();Assert.That(targets.Length,Is.EqualTo(2));
                foreach(string target in targets){
                    Assert.That(target,Does.Contain("SortBy="+EmbyLibraryViews.SortField(sort)).And.Contain("SortOrder="+(descending?"Descending":"Ascending")));
                    Assert.That(target,Does.Contain("ParentId="+library).And.Contain("SearchTerm=title%20%26%20clip"));
                }
                Assert.That(targets[1],Does.Contain("StartIndex=200"));
                Assert.That(targets[0].Contains("IncludeItemTypes=Movie"),Is.EqualTo(library=="movies"));
            }
            Assert.That(server.HeaderAuthenticationOnly,Is.True);
        }

        [UnityTest] public IEnumerator RootMenuChangesSortPreservesSearchAndLeavesNestedPagesAlone()
        {
            app.OpenEmbyLibrary(Library("movies"));yield return WaitUntilLoaded();
            var panel=Draw();var selector=Find(panel,"Emby sort selector");
            Assert.That(selector.GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text,Does.Contain("标题").And.Contain("↑"));
            selector.onClick.Invoke();Assert.That(browser.BlocksInput,Is.True);
            var menu=Field<RectTransform>(browser,"viewMenu");
            Assert.That(menu.GetComponentsInChildren<Button>(true).Count(button=>button.name=="Emby sort Title"||button.name=="Emby sort DateAdded"||button.name=="Emby sort ReleaseDate"),Is.EqualTo(3));
            Find(menu,"Emby sort ReleaseDate").onClick.Invoke();yield return WaitUntilLoaded();
            Assert.That(app.EmbyState.librarySort,Is.EqualTo(EmbyLibrarySort.ReleaseDate));Assert.That(app.EmbyState.librarySortDescending,Is.False);
            browser.Render(true);Find(panel,"Emby sort selector").onClick.Invoke();Find(Field<RectTransform>(browser,"viewMenu"),"Emby sort descending").onClick.Invoke();
            yield return WaitUntilLoaded();Assert.That(app.EmbyState.librarySortDescending,Is.True);
            Assert.That(app.EmbyState.items.Select(item=>item.Id),Is.EqualTo(SortFixture.Expected(EmbyLibrarySort.ReleaseDate,true)));
            app.SearchEmbyLibrary("title & clip");yield return WaitUntilLoaded();
            Assert.That(app.EmbyState.search,Is.EqualTo("title & clip"));
            Assert.That(server.Targets.Last(),Does.Contain("SortBy=PremiereDate").And.Contain("SortOrder=Descending").And.Contain("SearchTerm=title%20%26%20clip"));
            var rootState=app.EmbyState;
            app.OpenEmbyLibrary(new EmbyItem{Id="folder",Name="Folder",Type="Folder"});yield return WaitUntilLoaded();
            Assert.That(app.EmbyState.IsLibraryRoot,Is.False);
            Assert.That(server.Targets.Last(),Does.Contain("SortBy=SortName").And.Contain("SortOrder=Ascending"));
            browser.Render(true);Assert.That(panel.GetComponentsInChildren<Button>(true).Any(button=>button.name=="Emby sort selector"),Is.False);
            app.EmbyBack();Assert.That(app.EmbyState,Is.SameAs(rootState));browser.Render(true);
            Assert.That(Find(panel,"Emby sort selector").GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text,Does.Contain("发行日期").And.Contain("↓"));
            app.OpenEmbyCategory("Genre","Fixture category");yield return WaitUntilLoaded();browser.Render(true);
            Assert.That(app.EmbyState.IsLibraryRoot,Is.False);
            Assert.That(panel.GetComponentsInChildren<Button>(true).Any(button=>button.name=="Emby sort selector"),Is.False);
            Assert.That(server.Targets.Last(),Does.Contain("SortBy=SortName").And.Contain("SortOrder=Ascending"));
            app.EmbyBack();Assert.That(app.EmbyState,Is.SameAs(rootState));
            Assert.That(new PlayerStore(settingsPath).GetEmbyLibrarySort("account","movies"),Is.EqualTo(EmbyLibrarySort.ReleaseDate));
            Assert.That(new PlayerStore(settingsPath).GetEmbyLibrarySortDescending("account","movies"),Is.True);
        }

        [UnityTest] public IEnumerator ReturningToAnOlderRootReloadsItsSearchWithTheCurrentLibrarySort()
        {
            app.OpenEmbyLibrary(Library("movies"));yield return WaitUntilLoaded();
            app.SearchEmbyLibrary("remembered search");yield return WaitUntilLoaded();var oldRoot=app.EmbyState;
            ((Stack<EmbyBrowserState>)Field<object>(app,"embyPages")).Push(oldRoot);
            Set("EmbyState",new EmbyBrowserState{page=EmbyPage.Details});
            app.Store.SetEmbyLibrarySort("account","movies",EmbyLibrarySort.DateAdded,true);
            app.EmbyBack();yield return WaitUntilLoaded();
            Assert.That(app.EmbyState,Is.Not.SameAs(oldRoot));Assert.That(app.EmbyState.search,Is.EqualTo("remembered search"));
            Assert.That(app.EmbyState.librarySort,Is.EqualTo(EmbyLibrarySort.DateAdded));Assert.That(app.EmbyState.librarySortDescending,Is.True);
            Assert.That(app.EmbyState.items.Select(item=>item.Id),Is.EqualTo(SortFixture.Expected(EmbyLibrarySort.DateAdded,true)));
            Assert.That(server.Targets.Last(),Does.Contain("SearchTerm=remembered%20search").And.Contain("SortBy=DateCreated"));
        }

        [UnityTest] public IEnumerator SortingDuringSearchCancelsTheObsoleteResponse()
        {
            app.OpenEmbyLibrary(Library("shows"));yield return WaitUntilLoaded();
            server.HoldNext=true;app.SetEmbyLibrarySort(EmbyLibrarySort.DateAdded,true);
            yield return Wait(server.Held.Task);
            app.SearchEmbyLibrary("new search");yield return WaitUntilLoaded();
            var current=app.EmbyState;Assert.That(current.search,Is.EqualTo("new search"));
            Assert.That(current.items.Select(item=>item.Id),Is.EqualTo(SortFixture.Expected(EmbyLibrarySort.DateAdded,true)));
            server.Release.TrySetResult(true);for(int i=0;i<4;i++)yield return null;
            Assert.That(app.EmbyState,Is.SameAs(current));Assert.That(app.EmbyState.search,Is.EqualTo("new search"));
        }

        [UnityTest] public IEnumerator SortMenuConsumesBackAndRejectsStaleAccountOrPageChoices()
        {
            app.OpenEmbyLibrary(Library("movies"));yield return WaitUntilLoaded();var panel=Draw();
            Find(panel,"Emby sort selector").onClick.Invoke();var menu=Field<RectTransform>(browser,"viewMenu");
            var stale=Find(menu,"Emby sort DateAdded");Assert.That(browser.TryBack(),Is.True);Assert.That(browser.BlocksInput,Is.False);
            Find(panel,"Emby sort selector").onClick.Invoke();stale.onClick.Invoke();
            Assert.That(app.Store.GetEmbyLibrarySort("account","movies"),Is.EqualTo(EmbyLibrarySort.Title));
            menu=Field<RectTransform>(browser,"viewMenu");stale=Find(menu,"Emby sort ReleaseDate");
            Set("ActiveEmbyAccount",new EmbyAccount{id="other"});stale.onClick.Invoke();
            Assert.That(app.Store.GetEmbyLibrarySort("account","movies"),Is.EqualTo(EmbyLibrarySort.Title));
            Assert.That(app.Store.GetEmbyLibrarySort("other","movies"),Is.EqualTo(EmbyLibrarySort.Title));
        }

        private void Set(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private).GetValue(target);
        private static Button Find(Transform parent,string name)=>parent.GetComponentsInChildren<Button>(true).Single(button=>button.name==name);
        private static EmbyItem Library(string id)=>new EmbyItem{Id=id,Name=id,Type="CollectionFolder",CollectionType=id=="movies"?"movies":"tvshows"};
        private RectTransform Draw()
        {
            var panel=new GameObject("Sort panel",typeof(RectTransform)).GetComponent<RectTransform>();panel.SetParent(root.transform,false);panel.sizeDelta=new Vector2(1376,786);
            browser.Bind(panel);return panel;
        }
        private IEnumerator WaitUntilLoaded()
        {
            float deadline=Time.realtimeSinceStartup+15;while(app.EmbyState.loading&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(app.EmbyState.loading,Is.False,"Library request timed out");Assert.That(app.EmbyState.error,Is.Empty);
        }
        private static IEnumerator Wait(Task task)
        {
            float deadline=Time.realtimeSinceStartup+15;while(!task.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(task.IsCompleted,Is.True,"Fixture request timed out");if(task.IsFaulted)throw task.Exception.GetBaseException();Assert.That(task.IsCanceled,Is.False);
        }

        private sealed class SortFixture:IDisposable
        {
            private readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);
            private readonly List<TcpClient> clients=new List<TcpClient>();
            private readonly Dictionary<string,string[]> pages=new Dictionary<string,string[]>();
            public readonly List<string> Targets=new List<string>();
            public readonly TaskCompletionSource<bool> Held=new TaskCompletionSource<bool>(),Release=new TaskCompletionSource<bool>();
            public bool HoldNext,HeaderAuthenticationOnly=true;
            public EmbyAccount Account{get;}
            public SortFixture()
            {
                foreach(EmbyLibrarySort sort in Enum.GetValues(typeof(EmbyLibrarySort)))foreach(bool descending in new[]{false,true}){
                    var items=Expected(sort,descending).Select(id=>new EmbyItem{Id=id,Name="Fixture "+id,SortName=id,Type="Movie"}).ToArray();
                    pages[Key(sort,descending)]=Enumerable.Range(0,2).Select(page=>JsonUtility.ToJson(new EmbyItemsResult{Items=items.Skip(page*200).Take(200).ToArray(),TotalRecordCount=205})).ToArray();
                }
                listener.Start();Account=new EmbyAccount{id="account",userId="viewer",accessToken="fixture-token",serverUrl="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port};
                _ = Accept();
            }
            public static string[] Expected(EmbyLibrarySort sort,bool descending)
            {
                var values=Enumerable.Range(0,205).OrderBy(index=>sort==EmbyLibrarySort.DateAdded?index*29%205:sort==EmbyLibrarySort.ReleaseDate?index*37%205:index);
                var ordered=values.Select(index=>index.ToString("D3"));return (descending?ordered.Reverse():ordered).ToArray();
            }
            private static string Key(EmbyLibrarySort sort,bool descending)=>sort+":"+descending;
            private async Task Accept()
            {
                try{while(true){var client=await listener.AcceptTcpClientAsync();clients.Add(client);_ = Handle(client);}}
                catch(ObjectDisposedException){}catch(SocketException){}
            }
            private async Task Handle(TcpClient client)
            {
                try{
                    using(client)using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
                        string first=await reader.ReadLineAsync();if(first==null)return;bool authenticated=false;
                        for(string line=await reader.ReadLineAsync();!string.IsNullOrEmpty(line);line=await reader.ReadLineAsync())
                            if(line.StartsWith("X-Emby-Token:",StringComparison.OrdinalIgnoreCase))authenticated=line.Substring(13).Trim()==Account.accessToken;
                        string target=first.Split(' ')[1];Targets.Add(target);HeaderAuthenticationOnly&=authenticated&&!target.Contains(Account.accessToken)&&!target.Contains("api_key");
                        if(HoldNext){HoldNext=false;Held.TrySetResult(true);await Release.Task;}
                        var sort=target.Contains("SortBy=DateCreated")?EmbyLibrarySort.DateAdded:target.Contains("SortBy=PremiereDate")?EmbyLibrarySort.ReleaseDate:EmbyLibrarySort.Title;
                        bool descending=target.Contains("SortOrder=Descending");int page=target.Contains("StartIndex=200")?1:0;
                        byte[] body=Encoding.UTF8.GetBytes(pages[Key(sort,descending)][page]);
                        byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: "+body.Length+"\r\n\r\n");
                        await stream.WriteAsync(header,0,header.Length);await stream.WriteAsync(body,0,body.Length);
                    }
                }catch(IOException){}catch(ObjectDisposedException){}catch(SocketException){}
            }
            public void Dispose(){Release.TrySetResult(true);listener.Stop();foreach(var client in clients)client.Dispose();}
        }
    }
}
