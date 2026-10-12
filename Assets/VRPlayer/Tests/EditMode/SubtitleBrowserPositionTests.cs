using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class SubtitleBrowserPositionTests
    {
        private const BindingFlags PrivateFields=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject root,head;
        private PlayerApp app;
        private PlayerUI ui;
        private PlayerStore store;
        private string temporaryRoot;
        private QuickControlsUI Quick=>ui.Quick;
        private VirtualFileList List=>(VirtualFileList)typeof(QuickControlsUI).GetField("list",PrivateFields).GetValue(Quick);
        private CancellationToken BrowseToken=>((CancellationTokenSource)typeof(QuickControlsUI).GetField("browse",PrivateFields).GetValue(Quick)).Token;
        private void Set(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);

        [SetUp] public void Setup()
        {
            temporaryRoot=Path.Combine(Path.GetTempPath(),"VRPlayer-subtitle-position-"+Guid.NewGuid());Directory.CreateDirectory(temporaryRoot);
            store=new PlayerStore(Path.Combine(temporaryRoot,"settings.json"));store.Settings.formatDetection=FormatDetectionMode.Metadata;
            root=new GameObject("Subtitle browser position test");root.SetActive(false);app=root.AddComponent<PlayerApp>();
            Set("Store",store);Set("Backend",new TestBackend());Set("Presenter",root.AddComponent<VideoPresenter>());Set("Generation",1);
            Set("Current",store.Touch(new FileEntry{name="Generated.mp4",uri="file:///generated.mp4"},""));app.Presenter.Settings=app.Current.view;
            head=new GameObject("Subtitle browser test head",typeof(Camera));ui=root.AddComponent<PlayerUI>();Set("UI",ui);ui.Initialize(app,head.GetComponent<Camera>());
            ui.Hide();ui.ShowQuick();Quick.TogglePopup("files");
        }

        [TearDown] public void Cleanup()
        {
            Quick.ClosePopup();
            // UI canvases live outside their inactive owner, as they do at runtime.
            var quickCanvas=Quick.Canvas;typeof(QuickControlsUI).GetProperty("Canvas").SetValue(Quick,null);UnityEngine.Object.DestroyImmediate(quickCanvas.gameObject);
            var canvas=ui.Canvas;typeof(PlayerUI).GetProperty("Canvas").SetValue(ui,null);UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            var hud=(RectTransform)typeof(PlayerUI).GetField("transportRoot",PrivateFields).GetValue(ui);UnityEngine.Object.DestroyImmediate(hud.gameObject);
            Set("Presenter",null);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head);
            // Only this fixture's unique temporary tree is removed.
            if(Directory.Exists(temporaryRoot))Directory.Delete(temporaryRoot,true);
        }

        private string DirectoryWithSubtitles(string name,int count)
        {
            string folder=Path.Combine(temporaryRoot,name);Directory.CreateDirectory(folder);
            for(int i=0;i<count;i++)File.WriteAllText(Path.Combine(folder,$"Subtitle-{i:D3}.srt"),"1\n00:00:01,000 --> 00:00:02,000\nSubtitle");
            return folder;
        }
        private static string UriFor(string folder)=>new Uri(folder).AbsoluteUri;
        private void ClickBrowserButton(string name)
        {
            var popup=(RectTransform)typeof(QuickControlsUI).GetField("popupRoot",PrivateFields).GetValue(Quick);
            foreach(var button in popup.GetComponentsInChildren<Button>())if(button.name==name){button.onClick.Invoke();return;}
            Assert.Fail("Subtitle browser button was not found: "+name);
        }
        private async Task WaitForBrowser(string uri)
        {
            for(int i=0;i<200&&Quick.BrowserLoading;i++)await Task.Delay(10);
            Assert.That(Quick.BrowserLoading,Is.False,"Subtitle directory listing did not finish");
            Assert.That(Quick.BrowserUri,Is.EqualTo(uri));
        }

        [Test] public async Task BackToParentDirectoryRestoresPreviousScrollPosition()
        {
            string folder=DirectoryWithSubtitles("Parent",40),child=Path.Combine(folder,"Child");Directory.CreateDirectory(child);
            await Quick.Browse(UriFor(folder));List.SetOffset(1000);
            await Quick.Browse(UriFor(child));Assert.That(List.Offset,Is.Zero);
            Assert.That(Quick.TryBack());await WaitForBrowser(UriFor(folder));
            Assert.That(List.Count,Is.EqualTo(41));Assert.That(List.Offset,Is.EqualTo(1000));Assert.That(Quick.Popup,Is.EqualTo("files"));
        }

        [Test] public async Task BackFromSourceDirectoryRestoresSourcesScrollPosition()
        {
            string folder=DirectoryWithSubtitles("Favorite source",1);
            for(int i=0;i<30;i++)store.Settings.favorites.Add(new Favorite{name="Source "+i,uri=UriFor(i==20?folder:Path.Combine(temporaryRoot,"Source-"+i))});
            await Quick.Browse("",false);Assert.That(List.Count,Is.GreaterThanOrEqualTo(30));List.SetOffset(900);
            await Quick.Browse(UriFor(folder));Assert.That(Quick.TryBack());await WaitForBrowser("");
            Assert.That(List.Count,Is.GreaterThanOrEqualTo(30));Assert.That(List.Offset,Is.EqualTo(900));Assert.That(Quick.Popup,Is.EqualTo("files"));
        }

        [Test] public async Task BackFromInitialMovieDirectoryReturnsToSubtitlePopupAndItsPosition()
        {
            Quick.ClosePopup();app.Current.directory=UriFor(DirectoryWithSubtitles("Current movie",1));
            for(int i=0;i<30;i++)app.SubtitleCandidates.Add(new FileEntry{name="Subtitle "+i,uri="file:///subtitle-"+i+".srt"});
            Quick.TogglePopup("subtitles");List.SetOffset(700);Quick.TogglePopup("files");await WaitForBrowser(app.Current.directory);
            Assert.That(Quick.TryBack());Assert.That(Quick.Popup,Is.EqualTo("subtitles"));Assert.That(List.Offset,Is.EqualTo(700));
            Assert.That(Quick.TryBack());Assert.That(Quick.Popup,Is.Empty);Assert.That(Quick.Visible);
        }

        [Test] public async Task BackFromBrowserOpenedWithoutPopupReturnsToQuickBar()
        {
            Quick.ClosePopup();app.Current.directory=UriFor(DirectoryWithSubtitles("Direct browser",1));
            Quick.TogglePopup("files");await WaitForBrowser(app.Current.directory);
            Assert.That(Quick.TryBack());Assert.That(Quick.Popup,Is.Empty);Assert.That(Quick.Visible);
        }

        [Test] public async Task ParentButtonUsesPhysicalParentAndBackReturnsToDepartedDirectory()
        {
            string first=UriFor(DirectoryWithSubtitles("Earlier sibling",40)),second=UriFor(DirectoryWithSubtitles("Current sibling",1));
            await Quick.Browse(first);List.SetOffset(1000);await Quick.Browse(second);ClickBrowserButton("上级");await WaitForBrowser(UriFor(temporaryRoot));
            Assert.That(Quick.TryBack());await WaitForBrowser(second);
            Assert.That(Quick.TryBack());await WaitForBrowser(first);Assert.That(List.Offset,Is.EqualTo(1000));
        }

        [Test] public async Task SourcesButtonPreservesPreviousDirectoryInBackHistory()
        {
            string uri=UriFor(DirectoryWithSubtitles("From sources",40));await Quick.Browse(uri);List.SetOffset(1000);
            ClickBrowserButton("来源");await WaitForBrowser("");Assert.That(Quick.TryBack());await WaitForBrowser(uri);
            Assert.That(List.Offset,Is.EqualTo(1000));Assert.That(Quick.TryBack());await WaitForBrowser("");
        }

        [Test] public async Task BackThroughRepeatedDirectoryVisitsRestoresEachVisitPosition()
        {
            string first=UriFor(DirectoryWithSubtitles("Repeated",40)),second=UriFor(DirectoryWithSubtitles("Between visits",40)),last=UriFor(DirectoryWithSubtitles("Last visit",1));
            await Quick.Browse(first);List.SetOffset(1000);await Quick.Browse(second);List.SetOffset(800);
            await Quick.Browse(first);List.SetOffset(1200);await Quick.Browse(last);
            Assert.That(Quick.TryBack());await WaitForBrowser(first);Assert.That(List.Offset,Is.EqualTo(1200));
            Assert.That(Quick.TryBack());await WaitForBrowser(second);Assert.That(List.Offset,Is.EqualTo(800));
            Assert.That(Quick.TryBack());await WaitForBrowser(first);Assert.That(List.Offset,Is.EqualTo(1000));
            Assert.That(Quick.TryBack());await WaitForBrowser("");Assert.That(Quick.Popup,Is.EqualTo("files"));
        }

        [Test] public async Task RefreshKeepsScrollPositionAndClampsWhenDirectoryShrinks()
        {
            string folder=DirectoryWithSubtitles("Refreshing",40),uri=UriFor(folder);
            await Quick.Browse(uri);List.SetOffset(1700);await Quick.Browse(uri,false);
            Assert.That(List.Offset,Is.EqualTo(1700));
            for(int i=10;i<40;i++)File.Delete(Path.Combine(folder,$"Subtitle-{i:D3}.srt"));
            await Quick.Browse(uri,false);
            Assert.That(List.Count,Is.EqualTo(10));Assert.That(List.Offset,Is.EqualTo(10*VirtualFileList.RowPitch-472));
        }

        [Test] public async Task FailedRefreshDoesNotEraseScrollPositionBeforeRetry()
        {
            string folder=DirectoryWithSubtitles("Unavailable",40),uri=UriFor(folder),moved=Path.Combine(temporaryRoot,"Temporarily moved");
            await Quick.Browse(uri);List.SetOffset(1000);Directory.Move(folder,moved);
            await Quick.Browse(uri,false);Assert.That(Quick.BrowserLoading,Is.False);Assert.That(List.Count,Is.EqualTo(1));Assert.That(List.Offset,Is.Zero);
            Directory.Move(moved,folder);await Quick.Browse(uri,false);
            Assert.That(List.Count,Is.EqualTo(40));Assert.That(List.Offset,Is.EqualTo(1000));
        }

        [Test] public async Task ClosingSubtitleBrowserStartsNewSessionAtTop()
        {
            string uri=UriFor(DirectoryWithSubtitles("New session",40));
            await Quick.Browse(uri);List.SetOffset(1200);Quick.ClosePopup();Quick.TogglePopup("files");await Quick.Browse(uri);
            Assert.That(List.Count,Is.EqualTo(40));Assert.That(List.Offset,Is.Zero);
        }

        [Test] public async Task RapidBrowsingKeepsFinalDirectoryAndSavedScrollPosition()
        {
            string first=UriFor(DirectoryWithSubtitles("First",40)),second=UriFor(DirectoryWithSubtitles("Second",60));
            await Quick.Browse(first);List.SetOffset(720);await Quick.Browse(second);List.SetOffset(1280);await Quick.Browse(first);
            Assert.That(List.Offset,Is.EqualTo(720));
            Task older=Quick.Browse(second,false);var olderToken=BrowseToken;
            Task middle=Quick.Browse(first,false);var middleToken=BrowseToken;
            Task newest=Quick.Browse(second,false);
            Assert.That(olderToken.IsCancellationRequested);Assert.That(middleToken.IsCancellationRequested);
            await Task.WhenAll(older,middle,newest);
            Assert.That(Quick.BrowserUri,Is.EqualTo(second));Assert.That(Quick.BrowserLoading,Is.False);
            Assert.That(List.Count,Is.EqualTo(60));Assert.That(List.Offset,Is.EqualTo(1280));
        }

        private sealed class TestBackend : IMediaBackend
        {
            public MediaSnapshot Snapshot{get;private set;}=new MediaSnapshot{generation=1,duration=120000};
            public Texture Texture=>null;public bool RequiresGammaConversion=>false;public int RenderedFrames=>0;
            public void Open(string uri,long position,int generation)=>Snapshot=new MediaSnapshot{generation=generation,duration=120000,position=position};
            public void Tick(){}public void Play(){}public void Pause(){}public void Stop(){}public void Seek(long value){}
            public void SetVolume(float value){}public void SetSpeed(float value){}public void SetRepeat(bool value){}public void SelectTrack(string id,int type){}
            public void Dispose(){}
        }
    }
}
