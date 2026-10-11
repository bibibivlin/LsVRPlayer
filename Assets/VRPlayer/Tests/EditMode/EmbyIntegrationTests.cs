using System;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

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
    }
}
