using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRPlayer.Tests
{
    public sealed class EmbyIntegrationTests
    {
        [Test] public void EmbyTitlesWithoutExtensionsRemainVisibleInRecentVideos()
        {
            Assert.That(new FileEntry{name="影片名称",uri="emby://account/movie"}.IsVideo,Is.True);
            Assert.That(new FileEntry{name="媒体库",uri="emby://account/library",directory=true}.IsVideo,Is.False);
        }

        [Test] public void SourceEnumPreservesExistingValues()
        {
            Assert.That((int)FileSource.Directory,Is.EqualTo(4));Assert.That((int)FileSource.Emby,Is.EqualTo(5));
        }

        [Test] public void HistoryRetainsSourceVrTagsAlongsideServerTitle()
        {
            string folder=Path.Combine(Path.GetTempPath(),"vrplayer-emby-history-"+Guid.NewGuid().ToString("N"));
            try{
                var store=new PlayerStore(Path.Combine(folder,"settings.json"));
                store.Touch(new FileEntry{name="影片名称",formatName="Movie.180.SBS.mp4",uri="emby://account/movie"},"emby://account/library");store.Save();
                var record=new PlayerStore(Path.Combine(folder,"settings.json")).Find("emby://account/movie");
                Assert.That(record.name,Is.EqualTo("影片名称"));Assert.That(record.formatName,Is.EqualTo("Movie.180.SBS.mp4"));
                var view=ViewSettings.Detect(record.formatName);Assert.That(view.projection,Is.EqualTo(Projection.Equirect180));Assert.That(view.stereo,Is.EqualTo(StereoLayout.SideBySide));
                Assert.That(File.ReadAllText(Path.Combine(folder,"settings.json")),Does.Not.Contain("api_key"));
            }finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        }

        [Test] public void CapturedPlaybackAccountRetainsTokenWithoutSerializingIt()
        {
            var account=new EmbyAccount{id="one",accessToken="test-secret",serverUrl="http://server:8096",userId="user"};
            var copy=(EmbyAccount)typeof(PlayerApp).GetMethod("CopyEmbyAccount",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{account});
            account.accessToken="replacement";
            Assert.That(copy.accessToken,Is.EqualTo("test-secret"));Assert.That(JsonUtility.ToJson(copy),Does.Not.Contain("test-secret"));
        }

        [Test] public void LeavingEmbyCancelsBrowseAndPendingPlaybackIndependently()
        {
            var root=new GameObject("Emby cancellation regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();
            var browse=new CancellationTokenSource();var open=new CancellationTokenSource();var browseToken=browse.Token;var openToken=open.Token;
            try{
                typeof(PlayerApp).GetField("embyBrowsing",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,browse);
                typeof(PlayerApp).GetField("embyOpening",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,open);
                app.EmbyState.loading=true;app.CancelListing();
                Assert.That(browseToken.IsCancellationRequested);Assert.That(openToken.IsCancellationRequested);Assert.That(app.EmbyState.loading,Is.False);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [Test] public void SubtitleIdentityContainsOnlyStableAccountItemSourceAndIndex()
        {
            string value=EmbySubtitleIdentity.Create("account","movie","source",3);
            Assert.That(EmbySubtitleIdentity.TryParse(value,out string account,out string item,out string source,out int index));
            Assert.That(account,Is.EqualTo("account"));Assert.That(item,Is.EqualTo("movie"));Assert.That(source,Is.EqualTo("source"));Assert.That(index,Is.EqualTo(3));
            Assert.That(EmbySubtitleIdentity.TryParse(value+"?api_key=secret",out _,out _,out _,out _),Is.False);
        }

        [Test] public void FailedLoginForNewAccountDoesNotInvalidateExistingAccount()
        {
            var root=new GameObject("Emby login isolation regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();
            var active=new EmbyAccount{id="existing",accessToken="existing-session"};
            try{
                typeof(PlayerApp).GetProperty("ActiveEmbyAccount").SetValue(app,active);
                typeof(PlayerApp).GetMethod("FailEmby",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,new object[]{CancellationToken.None,new EmbyAuthenticationException(),null});
                Assert.That(active.accessToken,Is.EqualTo("existing-session"));Assert.That(app.EmbyState.page,Is.EqualTo(EmbyPage.Accounts));Assert.That(app.EmbyState.error,Is.Not.Empty);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [TestCase(false)] [TestCase(true)] public void FailedAccountSaveKeepsActiveAccountAndShowsRecoverableError(bool remove)
        {
            string folder=Path.Combine(Path.GetTempPath(),"vrplayer-emby-save-"+Guid.NewGuid().ToString("N"));
            var root=new GameObject("Emby save failure regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();
            bool writable=true;var store=new EmbyAccountStore(Path.Combine(folder,"accounts.json"),()=>"",_=>writable);
            var active=new EmbyAccount{id="active",serverUrl="http://server:8096",userId="user-a",accessToken="session-a"};
            var other=new EmbyAccount{id="other",serverUrl="http://server:8096",userId="user-b",accessToken="session-b"};
            try{
                store.Upsert(other);store.Upsert(active);writable=false;
                typeof(PlayerApp).GetField("embyAccounts",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,store);
                typeof(PlayerApp).GetProperty("ActiveEmbyAccount").SetValue(app,active);
                Assert.DoesNotThrow(()=>{if(remove)app.RemoveEmbyAccount(active.id);else app.SelectEmbyAccount(other.id);});
                Assert.That(app.ActiveEmbyAccount,Is.SameAs(active));Assert.That(store.SelectedId,Is.EqualTo(active.id));
                Assert.That(store.Accounts.Count,Is.EqualTo(2));Assert.That(app.EmbyState.error,Does.Contain("保存失败"));
                Assert.That(app.EmbyPendingReports,Is.Zero);
            }finally{UnityEngine.Object.DestroyImmediate(root);if(Directory.Exists(folder))Directory.Delete(folder,true);}
        }

        [TestCase(false)] [TestCase(true)] public void BackFromLoginClearsPasswordAndPreservesPendingPlayback(bool busy)
        {
            var root=new GameObject("Emby login back regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();var browser=root.AddComponent<EmbyBrowserUI>();
            using var authentication=new CancellationTokenSource();using var playback=new CancellationTokenSource();var authenticationToken=authentication.Token;var playbackToken=playback.Token;
            int keyboardCloses=0;browser.Initialize(app,_=>{},()=>keyboardCloses++,()=>{});
            var fieldRoot=new GameObject("Login password",typeof(RectTransform));fieldRoot.transform.SetParent(root.transform,false);var passwordField=fieldRoot.AddComponent<TMP_InputField>();passwordField.SetTextWithoutNotify("fixture password");
            var parts=EmbyMovieParts.Create("account",new EmbyItem{Id="main",Type="Movie"},new[]{new EmbyItem{Id="part",Type="Movie"}});
            try{
                SetPrivate(app,"embyBrowsing",authentication);SetPrivate(app,"embyOpening",playback);SetPrivate(app,"embyMovieParts",parts);app.EmbyState.loading=true;
                SetPrivate(browser,"login",true);SetPrivate(browser,"busy",busy);SetPrivate(browser,"password","fixture password");SetPrivate(browser,"passwordField",passwordField);SetPrivate(browser,"loginError","previous error");
                Assert.That(browser.TryBack());
                Assert.That(authenticationToken.IsCancellationRequested);Assert.That(playbackToken.IsCancellationRequested,Is.False);Assert.That(GetPrivate(app,"embyMovieParts"),Is.SameAs(parts));
                Assert.That(passwordField.text,Is.Empty);Assert.That(GetPrivate(browser,"password"),Is.Empty);Assert.That(GetPrivate(browser,"loginError"),Is.Empty);
                Assert.That(GetPrivate(browser,"login"),Is.False);Assert.That(GetPrivate(browser,"busy"),Is.False);Assert.That(keyboardCloses,Is.EqualTo(1));Assert.That(app.EmbyState.page,Is.EqualTo(EmbyPage.Accounts));
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [Test] public void BackCancelsAccountRemovalConfirmationBeforeLeavingAccounts()
        {
            var root=new GameObject("Emby removal back regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();var browser=root.AddComponent<EmbyBrowserUI>();browser.Initialize(app,_=>{},()=>{},()=>{});
            try{
                SetPrivate(browser,"removing","account");Assert.That(browser.TryBack());Assert.That(GetPrivate(browser,"removing"),Is.Empty);Assert.That(app.EmbyState.page,Is.EqualTo(EmbyPage.Accounts));
                Assert.That(browser.TryBack(),Is.False);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [TestCase(EmbyPage.Home)] [TestCase(EmbyPage.Library)] [TestCase(EmbyPage.Details)] [TestCase(EmbyPage.Person)]
        public void BackFromEmbyPagesRestoresSavedPageAndKeepsPendingCdContinuation(EmbyPage page)
        {
            var root=new GameObject("Emby page back regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();var browser=root.AddComponent<EmbyBrowserUI>();browser.Initialize(app,_=>{},()=>{},()=>{});
            using var browsing=new CancellationTokenSource();using var playback=new CancellationTokenSource();var browsingToken=browsing.Token;var playbackToken=playback.Token;
            var previous=new EmbyBrowserState{page=EmbyPage.Library,title="Previous wall"};
            try{
                ((Stack<EmbyBrowserState>)GetPrivate(app,"embyPages")).Push(previous);app.EmbyState.page=page;app.EmbyState.loading=true;
                SetPrivate(app,"embyBrowsing",browsing);SetPrivate(app,"embyOpening",playback);
                Assert.That(browser.TryBack());Assert.That(app.EmbyState,Is.SameAs(previous));Assert.That(browsingToken.IsCancellationRequested);Assert.That(playbackToken.IsCancellationRequested,Is.False);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [Test] public void BackAtAccountsRootDelegatesClosingPanelWithoutChangingPlayback()
        {
            var root=new GameObject("Emby accounts back regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();var browser=root.AddComponent<EmbyBrowserUI>();browser.Initialize(app,_=>{},()=>{},()=>{});
            using var playback=new CancellationTokenSource();var playbackToken=playback.Token;
            try{
                SetPrivate(app,"embyOpening",playback);var state=app.EmbyState;
                Assert.That(browser.TryBack(),Is.False);Assert.That(app.EmbyState,Is.SameAs(state));Assert.That(playbackToken.IsCancellationRequested,Is.False);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [UnityTest] public IEnumerator CancelledLoginCompletionCannotOverwriteNewLoginForm()
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            var root=new GameObject("Emby stale login back regression");root.SetActive(false);var app=root.AddComponent<PlayerApp>();var browser=root.AddComponent<EmbyBrowserUI>();browser.Initialize(app,_=>{},()=>{},()=>{});
            try{
                typeof(PlayerApp).GetProperty("EmbyApi").SetValue(app,new EmbyClient("back-regression"));
                SetPrivate(browser,"login",true);SetPrivate(browser,"serverAddress","127.0.0.1");SetPrivate(browser,"serverPort",((IPEndPoint)listener.LocalEndpoint).Port.ToString());SetPrivate(browser,"username","fixture");
                var request=(Task)typeof(EmbyBrowserUI).GetMethod("Login",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(browser,null);
                Assert.That(request.IsCompleted,Is.False);Assert.That(GetPrivate(browser,"busy"),Is.True);
                Assert.That(browser.TryBack());
                // Start a new form before the cancelled request's continuation runs.
                SetPrivate(browser,"login",true);SetPrivate(browser,"busy",true);SetPrivate(browser,"loginError","new form state");
                float deadline=Time.realtimeSinceStartup+5;
                while(!request.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(request.IsCompleted);Assert.That(request.IsFaulted,Is.False);
                Assert.That(GetPrivate(browser,"login"),Is.True);Assert.That(GetPrivate(browser,"busy"),Is.True);Assert.That(GetPrivate(browser,"loginError"),Is.EqualTo("new form state"));
                Assert.That(app.EmbyState.page,Is.EqualTo(EmbyPage.Accounts));
            }finally{app.CancelEmbyBrowsing(false);listener.Stop();UnityEngine.Object.DestroyImmediate(root);}
        }

        private static void SetPrivate(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static object GetPrivate(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    }
}
