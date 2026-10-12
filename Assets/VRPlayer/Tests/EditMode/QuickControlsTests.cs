using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class QuickControlsTests
    {
        private GameObject root,head,events;
        private PlayerApp app;private PlayerUI ui;private PlayerStore store;private TestBackend backend;private string path;
        private void Set(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
        private VirtualFileList FullFileList=>(VirtualFileList)typeof(PlayerUI).GetField("fileList",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
        private void FileButton(string name)
        {
            var content=(RectTransform)typeof(PlayerUI).GetField("content",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            var button=content.GetComponentsInChildren<Button>().First(candidate=>candidate.name==name);button.onClick.Invoke();
        }
        private async Task WaitForDirectory()
        {
            for(int i=0;i<200&&app.Loading;i++)await Task.Delay(5);
            Assert.That(app.Loading,Is.False,"Directory listing did not complete");
        }
        private string FileFilter=>((TMPro.TMP_InputField)typeof(PlayerUI).GetField("filter",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui)).text;
        private void ApplyFileFilter(string value)
        {
            var filter=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("filter",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            filter.text=value;FileButton("搜索");
        }
        private static void WriteMovieRows(string folder,int count=40)
        {
            Directory.CreateDirectory(folder);
            for(int i=0;i<count;i++)File.WriteAllText(Path.Combine(folder,"MovieMatch"+i.ToString("D2")+".mp4"),"");
        }
        private void DirectorySnapshot(string uri,int count,bool loading=false,string locate="")
        {
            Set("Source",FileSource.Directory);Set("DirectoryUri",uri);Set("Loading",loading);Set("LocateFileUri",locate);
            Set("Files",Enumerable.Range(0,count).Select(index=>new FileEntry{name="Movie"+index.ToString("D3")+".mp4",uri=uri+"/movie"+index}).ToList());
            typeof(PlayerUI).GetMethod("OnAppChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
        }
        [SetUp] public void Setup()
        {
            path=Path.Combine(Path.GetTempPath(),"VRPlayer-quick-"+Guid.NewGuid()+".json");store=new PlayerStore(path);store.Settings.formatDetection=FormatDetectionMode.Metadata;
            root=new GameObject("Quick controls test");root.SetActive(false);app=root.AddComponent<PlayerApp>();backend=new TestBackend();
            Set("Store",store);Set("Backend",backend);Set("Presenter",root.AddComponent<VideoPresenter>());
            Set("Generation",1);backend.Snapshot.generation=1;backend.Snapshot.duration=120000;
            Set("Current",store.Touch(new FileEntry{name="Generated.mp4",uri="file:///generated.mp4"},""));
            app.Presenter.Settings=app.Current.view;
            head=new GameObject("Test head",typeof(Camera));ui=root.AddComponent<PlayerUI>();Set("UI",ui);ui.Initialize(app,head.GetComponent<Camera>());
            events=new GameObject("Pointer test",typeof(EventSystem));
        }
        [TearDown] public void Cleanup()
        {
            // These canvases are external to the owner, matching the runtime hierarchy.
            var quickCanvas=ui.Quick.Canvas;typeof(QuickControlsUI).GetProperty("Canvas").SetValue(ui.Quick,null);UnityEngine.Object.DestroyImmediate(quickCanvas.gameObject);
            var canvas=ui.Canvas;typeof(PlayerUI).GetProperty("Canvas").SetValue(ui,null);UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            var hud=(RectTransform)typeof(PlayerUI).GetField("transportRoot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);UnityEngine.Object.DestroyImmediate(hud.gameObject);
            Set("Presenter",null);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head);UnityEngine.Object.DestroyImmediate(events);
            if(File.Exists(path))File.Delete(path);
        }
        [Test] public void FullAndQuickPanelsAreMutuallyExclusive()
        {
            ui.ShowQuick();Assert.That(ui.Quick.Visible,Is.False);ui.Hide();ui.ShowQuick();Assert.That(ui.Visible,Is.False);Assert.That(ui.AnyVisible);Assert.That(ui.ActiveCanvas,Is.SameAs(ui.Quick.Canvas));
            ui.Show();Assert.That(ui.Visible);Assert.That(ui.Quick.Visible,Is.False);Assert.That(ui.ActiveCanvas,Is.SameAs(ui.Canvas));
        }
        [TestCase("")][TestCase("subtitles")][TestCase("mode")][TestCase("advanced")]
        public void FloatingCloseStopsPlaybackAndReturnsToFilePage(string popup)
        {
            DirectorySnapshot("smb://generated/Video/",40);FullFileList.SetOffset(640);
            ui.SelectTab("设置");ui.Hide();ui.ShowQuick();if(popup!="")ui.Quick.TogglePopup(popup);
            var surface=ui.Quick.Canvas.GetComponentInChildren<QuickSeekSurface>(true);
            var pointer=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-100};surface.OnPointerDown(pointer);
            int generation=app.Generation,revision=ui.SurfaceRevision;backend.Snapshot.position=42000;
            var record=app.Current;
            var close=ui.Quick.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Stop and open files");
            close.onClick.Invoke();surface.OnPointerUp(pointer);
            Assert.That(backend.stops,Is.EqualTo(1));Assert.That(backend.seeks,Is.Zero);Assert.That(app.Current,Is.Null);
            Assert.That(app.Generation,Is.GreaterThan(generation));Assert.That(ui.SurfaceRevision,Is.GreaterThan(revision));
            Assert.That(ui.Visible);Assert.That(ui.Quick.Visible,Is.False);Assert.That(ui.Quick.Popup,Is.Empty);
            Assert.That(ui.ActiveCanvas,Is.SameAs(ui.Canvas));Assert.That(ui.SelectedTab,Is.EqualTo("文件"));
            Assert.That(app.DirectoryUri,Is.EqualTo("smb://generated/Video/"));Assert.That(FullFileList.Offset,Is.EqualTo(640).Within(.1f));
            Assert.That(new PlayerStore(path).Find(record.uri).position,Is.EqualTo(42000));
        }
        [Test] public void FloatingCloseCancelsSubtitleBrowsingAndPendingStorageRetry()
        {
            ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("files");
            var browseToken=((System.Threading.CancellationTokenSource)typeof(QuickControlsUI).GetField("browse",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui.Quick)).Token;
            var pending=new System.Threading.CancellationTokenSource();var storageToken=pending.Token;
            typeof(PlayerApp).GetField("storageAccess",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,pending);
            ui.Quick.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Stop and open files").onClick.Invoke();
            Assert.That(browseToken.IsCancellationRequested);Assert.That(storageToken.IsCancellationRequested);
            Assert.That(ui.Quick.BrowserLoading,Is.False);Assert.That(ui.Quick.BrowserUri,Is.Empty);Assert.That(ui.Visible);
        }
        [TestCase("smb://config/Video/%E4%B8%AD%E6%96%87%20%E6%97%A5%E6%9C%AC%E8%AA%9E/","smb://config/Video/中文 日本語/")]
        [TestCase("smb://config/Video/100%25/%252F/%2F/%23/%3F/%2B/","smb://config/Video/100%25/%252F/%2F/%23/%3F/%2B/")]
        [TestCase("file:///C:/%E8%A7%86%E9%A2%91/%ED%95%9C%EA%B8%80/","file:///C:/视频/한글/")]
        [TestCase("content://documents/tree/primary%3A%E8%A7%86%E9%A2%91","content://documents/tree/primary%3A%E8%A7%86%E9%A2%91")]
        public void ReadableAddressPreservesOriginalIdentityAndReservedEscapes(string original,string displayed)
        {
            Assert.That(FileLibrary.DisplayAddress(original),Is.EqualTo(displayed));
            Assert.That(FileLibrary.ParseAddress(displayed,original),Is.EqualTo(original));
        }
        [Test] public void EditedUnicodeAddressEscapesNewCharactersOnce()
        {
            string original="smb://config/Video/%E4%B8%AD%E6%96%87/100%25/%252F/";
            string display=FileLibrary.DisplayAddress(original);
            Assert.That(FileLibrary.ParseAddress(display+"日本語/",original),Is.EqualTo(original+"%E6%97%A5%E6%9C%AC%E8%AA%9E/"));
        }
        [Test] public async Task DisplayingAndOpeningChineseDirectoryDoesNotChangeHistoryIdentity()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-中文 日本語 100%25 #+"+Guid.NewGuid());Directory.CreateDirectory(folder);
            try{
                string original=new Uri(folder).AbsoluteUri;await app.Browse(original);
                var address=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("address",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
                Assert.That(address.text,Does.Contain("中文 日本語"));Assert.That(app.DirectoryUri,Is.EqualTo(original));
                long history=app.BrowserHistoryPosition;FileButton("打开");await WaitForDirectory();
                Assert.That(app.DirectoryUri,Is.EqualTo(original));Assert.That(app.BrowserHistoryPosition,Is.EqualTo(history));
            }finally{Directory.Delete(folder,true);}
        }
        [TestCase("file:///storage/emulated/0/Movies/",true)][TestCase("/sdcard/Movies/",true)]
        [TestCase("smb://config/Movies/",false)][TestCase("content://documents/tree/primary%3AMovies",false)]
        [TestCase("file:///data/user/0/player/files/",false)][TestCase("/sdcard-other/Movies/",false)]
        public void StoragePermissionRequestsOnlyApplyToSharedLocalStorage(string uri,bool shared)
        {
            Assert.That(FileLibrary.IsSharedStorage(uri),Is.EqualTo(shared));
        }
        [Test] public void CancelledStorageWaitCannotRequestOrRetryAccess()
        {
            using var cancelled=new System.Threading.CancellationTokenSource();cancelled.Cancel();
            Assert.That(async()=>await FileLibrary.TryRequestReadPermission("file:///storage/emulated/0/Movies/",cancelled.Token),Throws.InstanceOf<OperationCanceledException>());
        }
        [Test] public void OpeningAnotherFilmPreservesHiddenOrQuickSurface()
        {
            ui.Hide();app.Open(new FileEntry{name="Other.mp4",uri="file:///other.mp4"},"");Assert.That(ui.AnyVisible,Is.False);
            ui.ShowQuick();app.Open(new FileEntry{name="Third.mp4",uri="file:///third.mp4"},"");Assert.That(ui.Quick.Visible);Assert.That(ui.Visible,Is.False);
        }
        [TestCase("mode")][TestCase("advanced")][TestCase("subtitles")]
        public void BackClosesQuickPopupBeforeClosingPanel(string popup)
        {
            ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup(popup);int revision=ui.SurfaceRevision;
            Assert.That(ui.Back());Assert.That(ui.Quick.Popup,Is.Empty);Assert.That(ui.Quick.Visible);Assert.That(ui.SurfaceRevision,Is.GreaterThan(revision));
            Assert.That(ui.Back());Assert.That(ui.AnyVisible,Is.False);Assert.That(ui.ActiveCanvas,Is.Null);
        }
        [Test] public async Task BackInQuickSubtitleBrowserUsesParentsBeforeReturningToSubtitles()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-back-"+Guid.NewGuid()),child=Path.Combine(folder,"Child");Directory.CreateDirectory(child);
            try{
                app.Current.directory="";ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("subtitles");ui.Quick.TogglePopup("files");
                await ui.Quick.Browse(new Uri(folder).AbsoluteUri);await ui.Quick.Browse(new Uri(child).AbsoluteUri);
                FileSource source=app.Source;string mainDirectory=app.DirectoryUri;string film=app.Current.uri;
                ui.Back();Assert.That(ui.Quick.BrowserUri,Is.EqualTo(new Uri(folder).AbsoluteUri));Assert.That(ui.Quick.Popup,Is.EqualTo("files"));
                ui.Back();Assert.That(ui.Quick.BrowserUri,Is.Empty);Assert.That(ui.Quick.Popup,Is.EqualTo("files"));
                ui.Back();Assert.That(ui.Quick.Popup,Is.EqualTo("subtitles"));Assert.That(ui.Quick.Visible);
                Assert.That(app.Source,Is.EqualTo(source));Assert.That(app.DirectoryUri,Is.EqualTo(mainDirectory));Assert.That(app.Current.uri,Is.EqualTo(film));
                ui.Back();Assert.That(ui.Quick.Popup,Is.Empty);ui.Back();Assert.That(ui.AnyVisible,Is.False);
            }finally{ui.Quick.ClosePopup();Directory.Delete(folder,true);}
        }
        [Test] public async Task BackFromQuickSmbRootReturnsToSubtitleSources()
        {
            app.Current.directory="";ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("subtitles");ui.Quick.TogglePopup("files");await ui.Quick.Browse("smb://test-config/");
            ui.Back();Assert.That(ui.Quick.Popup,Is.EqualTo("files"));Assert.That(ui.Quick.BrowserUri,Is.Empty);Assert.That(ui.Quick.Visible);
            ui.Back();Assert.That(ui.Quick.Popup,Is.EqualTo("subtitles"));
        }
        [Test] public void BackClosesKeyboardBeforeSmbFormAndFullPanel()
        {
            // This inactive UI rig checks navigation, not SMB discovery coroutines.
            LogAssert.Expect(LogType.Error,"Coroutine couldn't be started because the the game object 'Quick controls test' is inactive!");
            Set("Source",FileSource.Smb);ui.SelectTab("文件");
            var content=(RectTransform)typeof(PlayerUI).GetField("content",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            foreach(var button in content.GetComponentsInChildren<Button>())if(button.name=="手动添加"){button.onClick.Invoke();break;}
            var modal=typeof(PlayerUI).GetField("modal",BindingFlags.Instance|BindingFlags.NonPublic);Assert.That(modal.GetValue(ui),Is.EqualTo("server"));
            var field=content.GetComponentInChildren<TMPro.TMP_InputField>();
            typeof(PlayerUI).GetMethod("ShowKeyboard",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{field});
            var keyboard=(RectTransform)typeof(PlayerUI).GetField("keyboard",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            ui.Back();Assert.That(keyboard.gameObject.activeSelf,Is.False);Assert.That(modal.GetValue(ui),Is.EqualTo("server"));Assert.That(ui.Visible);
            LogAssert.Expect(LogType.Error,"Coroutine couldn't be started because the the game object 'Quick controls test' is inactive!");
            ui.Back();Assert.That(modal.GetValue(ui),Is.Empty);Assert.That(ui.Visible);ui.Back();Assert.That(ui.AnyVisible,Is.False);
        }
        [Test] public void BackCancelsSmbRequestWithoutLeavingFilesPage()
        {
            var token=new System.Threading.CancellationTokenSource();
            typeof(PlayerUI).GetField("connection",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ui,token);
            typeof(PlayerUI).GetField("modal",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ui,"connecting");
            ui.Back();Assert.That(token.IsCancellationRequested);Assert.That(ui.Visible);Assert.That(ui.SelectedTab,Is.EqualTo("文件"));
            Assert.That(typeof(PlayerUI).GetField("connection",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui),Is.Null);
        }
        [Test] public void BackFromSearchKeyboardAppliesFilterWithoutClosingFullPanel()
        {
            Set("Files",new System.Collections.Generic.List<FileEntry>{new FileEntry{name="Wanted.mp4",uri="file:///wanted.mp4"},new FileEntry{name="Other.mp4",uri="file:///other.mp4"}});
            ui.SelectTab("文件");var privateFields=BindingFlags.Instance|BindingFlags.NonPublic;
            var list=(VirtualFileList)typeof(PlayerUI).GetField("fileList",privateFields).GetValue(ui);Assert.That(list.Count,Is.EqualTo(2));
            var filter=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("filter",privateFields).GetValue(ui);
            typeof(PlayerUI).GetMethod("ShowKeyboard",privateFields).Invoke(ui,new object[]{filter});filter.text="Wanted";
            ui.Back();list=(VirtualFileList)typeof(PlayerUI).GetField("fileList",privateFields).GetValue(ui);
            Assert.That(list.Count,Is.EqualTo(1));Assert.That(ui.Visible);Assert.That(ui.SelectedTab,Is.EqualTo("文件"));
            Assert.That(ui.BlocksScrolling,Is.False);
        }
        [Test] public async Task BackFromDirectoryUsesNavigationHistoryBeforeClosingPanel()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-main-back-"+Guid.NewGuid()),child=Path.Combine(folder,"Child");Directory.CreateDirectory(child);
            try{
                await app.Browse(new Uri(folder).AbsoluteUri);await app.Browse(new Uri(child).AbsoluteUri);ui.Back();
                Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(folder).AbsoluteUri));Assert.That(ui.Visible);Assert.That(app.Source,Is.EqualTo(FileSource.Directory));
                app.Home();ui.Back();Assert.That(ui.AnyVisible,Is.False);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task BackFromFavoriteShortcutRestoresFavoriteSearchSortSubtitleViewAndOffset()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-favorite-back-"+Guid.NewGuid());Directory.CreateDirectory(folder);
            try{
                for(int i=0;i<40;i++){
                    string deep=Path.Combine(folder,"Shortcut"+i.ToString("D2"),"Deep");Directory.CreateDirectory(deep);
                    store.Settings.favorites.Add(new Favorite{name="Shortcut "+i.ToString("D2"),uri=new Uri(deep).AbsoluteUri});
                }
                app.ShowFavorites();ApplyFileFilter("Shortcut");FileButton("名称排序");FileButton("视频文件");FullFileList.SetOffset(941);
                var row=FullFileList.GetComponentsInChildren<RowBinding>().First(binding=>binding.Item?.open!=null);
                string selected=row.Item.key;row.Press();row.Open();await WaitForDirectory();
                Assert.That(app.DirectoryUri,Is.EqualTo(selected));Assert.That(app.Source,Is.EqualTo(FileSource.Directory));
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Favorites));Assert.That(app.DirectoryUri,Is.Empty);
                Assert.That(FileFilter,Is.EqualTo("Shortcut"));Assert.That(FullFileList.Count,Is.EqualTo(40));Assert.That(FullFileList.Offset,Is.EqualTo(941));
                FileButton("字幕文件");FileButton("日期排序");Assert.That(ui.Visible);
                ui.Back();Assert.That(ui.AnyVisible,Is.False);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task BackFromHomeShortcutReturnsToHomeWithoutVisitingPhysicalParents()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-home-back-"+Guid.NewGuid()),deep=Path.Combine(folder,"Parent","Deep");Directory.CreateDirectory(deep);
            try{
                app.Home();var roots=app.Files.Select(item=>item.uri).ToArray();await app.Browse(new Uri(deep).AbsoluteUri);
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Home));Assert.That(app.DirectoryUri,Is.Empty);Assert.That(app.Files.Select(item=>item.uri),Is.EqualTo(roots));Assert.That(ui.Visible);
                ui.Back();Assert.That(ui.AnyVisible,Is.False);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task BackFromRecentContainingDirectoryRestoresRecentQueryAndViewport()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-recent-back-"+Guid.NewGuid()),deep=Path.Combine(folder,"Parent","Deep");Directory.CreateDirectory(deep);
            string mediaUri=new Uri(Path.Combine(deep,"Track25.mp4")).AbsoluteUri;
            try{
                for(int i=0;i<40;i++){
                    string name="Track"+i.ToString("D2")+".mp4",media=Path.Combine(deep,name);File.WriteAllText(media,"");
                    store.Touch(new FileEntry{name=name,uri=new Uri(media).AbsoluteUri},new Uri(deep).AbsoluteUri);
                }
                app.ShowRecent();ApplyFileFilter("Track");Assert.That(FullFileList.Count,Is.EqualTo(40));FullFileList.SetOffset(941);
                app.OpenContainingDirectory(mediaUri);await WaitForDirectory();
                Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(deep).AbsoluteUri));Assert.That(app.LocateFileUri,Is.EqualTo(mediaUri));Assert.That(FullFileList.Offset,Is.EqualTo(25*VirtualFileList.RowPitch));
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Recent));Assert.That(app.DirectoryUri,Is.Empty);Assert.That(app.LocateFileUri,Is.Empty);
                Assert.That(FileFilter,Is.EqualTo("Track"));Assert.That(FullFileList.Count,Is.EqualTo(40));Assert.That(FullFileList.Offset,Is.EqualTo(941));Assert.That(ui.Visible);
                ui.Back();Assert.That(ui.AnyVisible,Is.False);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task UpVisitsPhysicalParentAndBackRestoresDirectoryJustLeft()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-up-back-"+Guid.NewGuid()),deep=Path.Combine(folder,"Parent","Deep");WriteMovieRows(deep);
            try{
                app.Home();await app.Browse(new Uri(deep).AbsoluteUri);ApplyFileFilter("Match");FullFileList.SetOffset(739);
                FileButton("上级");await WaitForDirectory();Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(Path.GetDirectoryName(deep)).AbsoluteUri));
                ui.Back();await WaitForDirectory();Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(deep).AbsoluteUri));Assert.That(FileFilter,Is.EqualTo("Match"));Assert.That(FullFileList.Offset,Is.EqualTo(739));
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Home));Assert.That(ui.Visible);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task BackFromUnrelatedAddressRestoresPreviousDirectoryAcrossTabs()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-address-back-"+Guid.NewGuid()),first=Path.Combine(folder,"First","Deep"),other=Path.Combine(folder,"Other","Nested");WriteMovieRows(first);Directory.CreateDirectory(other);
            try{
                app.Home();await app.Browse(new Uri(first).AbsoluteUri);ApplyFileFilter("Match");FullFileList.SetOffset(813);
                var address=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("address",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);address.text=new Uri(other).AbsoluteUri;FileButton("打开");await WaitForDirectory();
                Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(other).AbsoluteUri));ui.SelectTab("投影");ui.SelectTab("文件");
                ui.Back();await WaitForDirectory();Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(first).AbsoluteUri));Assert.That(FileFilter,Is.EqualTo("Match"));Assert.That(FullFileList.Offset,Is.EqualTo(813));Assert.That(ui.Visible);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task RepeatedDirectoryVisitsRestoreEachHistoricalSearchAndOffset()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-repeat-back-"+Guid.NewGuid()),first=Path.Combine(folder,"First"),second=Path.Combine(folder,"Second"),third=Path.Combine(folder,"Third");WriteMovieRows(first);WriteMovieRows(second);Directory.CreateDirectory(third);
            try{
                app.Home();await app.Browse(new Uri(first).AbsoluteUri);ApplyFileFilter("Match");FullFileList.SetOffset(241);
                await app.Browse(new Uri(second).AbsoluteUri);ApplyFileFilter("Movie");FullFileList.SetOffset(431);
                await app.Browse(new Uri(first).AbsoluteUri);ApplyFileFilter("MovieMatch");FullFileList.SetOffset(941);
                await app.Browse(new Uri(third).AbsoluteUri);
                ui.Back();await WaitForDirectory();Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(first).AbsoluteUri));Assert.That(FileFilter,Is.EqualTo("MovieMatch"));Assert.That(FullFileList.Offset,Is.EqualTo(941));
                ui.Back();await WaitForDirectory();Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(second).AbsoluteUri));Assert.That(FileFilter,Is.EqualTo("Movie"));Assert.That(FullFileList.Offset,Is.EqualTo(431));
                ui.Back();await WaitForDirectory();Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(first).AbsoluteUri));Assert.That(FileFilter,Is.EqualTo("Match"));Assert.That(FullFileList.Offset,Is.EqualTo(241));
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Home));
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task RefreshKeepsDirectoryViewWithoutAddingAnotherBackStep()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-refresh-back-"+Guid.NewGuid());WriteMovieRows(folder);
            try{
                app.Home();await app.Browse(new Uri(folder).AbsoluteUri);ApplyFileFilter("Match");FullFileList.SetOffset(621);
                app.Refresh();await WaitForDirectory();app.Refresh();await WaitForDirectory();
                Assert.That(FileFilter,Is.EqualTo("Match"));Assert.That(FullFileList.Offset,Is.EqualTo(621));ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Home));Assert.That(ui.Visible);
                ui.Back();Assert.That(ui.AnyVisible,Is.False);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [TestCase(FileSource.Home)][TestCase(FileSource.Favorites)][TestCase(FileSource.Recent)][TestCase(FileSource.Smb)]
        public async Task ExplicitSourceChangeDiscardsDirectoryBackHistory(FileSource source)
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-source-back-"+Guid.NewGuid()),child=Path.Combine(folder,"Child");Directory.CreateDirectory(child);
            try{
                app.Home();await app.Browse(new Uri(folder).AbsoluteUri);await app.Browse(new Uri(child).AbsoluteUri);
                switch(source){
                    case FileSource.Home:app.Home();break;
                    case FileSource.Favorites:app.ShowFavorites();break;
                    case FileSource.Recent:app.ShowRecent();break;
                    case FileSource.Smb:LogAssert.Expect(LogType.Error,new Regex("Coroutine couldn't be started.*inactive"));app.ShowServers();break;
                }
                Assert.That(app.Source,Is.EqualTo(source));ui.Back();Assert.That(ui.AnyVisible,Is.False);Assert.That(app.Source,Is.EqualTo(source));Assert.That(app.DirectoryUri,Is.Empty);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [TestCase(false)][TestCase(true)] public async Task BackFromFailedOrRetiredListingCancelsRequestAndKeepsRestoredRoots(bool failed)
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-listing-back-"+Guid.NewGuid());WriteMovieRows(folder,200);
            try{
                app.Home();var roots=app.Files.Select(item=>item.uri).ToArray();var listing=app.Browse(new Uri(failed?Path.Combine(folder,"Missing"):folder).AbsoluteUri);
                var request=(System.Threading.CancellationTokenSource)typeof(PlayerApp).GetField("listing",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);var token=request.Token;
                if(failed){await listing;Assert.That(app.Files,Is.Empty);}
                ui.Back();await listing;
                Assert.That(token.IsCancellationRequested);Assert.That(app.Source,Is.EqualTo(FileSource.Home));Assert.That(app.DirectoryUri,Is.Empty);Assert.That(app.Loading,Is.False);
                Assert.That(app.Files.Select(item=>item.uri),Is.EqualTo(roots));Assert.That(ui.Visible);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task BackWithoutHistoryClosesPanelInsteadOfGuessingPhysicalParent()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-no-history-back-"+Guid.NewGuid()),deep=Path.Combine(folder,"Parent","Deep");Directory.CreateDirectory(deep);
            try{
                app.Home();await app.Browse(new Uri(deep).AbsoluteUri,false);ui.Back();
                Assert.That(ui.AnyVisible,Is.False);Assert.That(app.Source,Is.EqualTo(FileSource.Directory));Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(deep).AbsoluteUri));
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public async Task SafParentMappingSupportsUpWhileBackRestoresTheChild()
        {
            // This simulates SAF identities in EditMode; it does not validate Android directory access.
            const string child="content://fixture/tree/child",parent="content://fixture/tree/root";
            try{
                app.Home();await app.Browse(child,physicalParent:parent);app.Up();Assert.That(app.DirectoryUri,Is.EqualTo(parent));
                app.CancelListing();ui.Back();Assert.That(app.DirectoryUri,Is.EqualTo(child));Assert.That(app.Source,Is.EqualTo(FileSource.Directory));Assert.That(ui.Visible);
                app.CancelListing();await Task.Yield();Assert.That(app.DirectoryUri,Is.EqualTo(child));
            }finally{app.CancelListing();}
        }
        [TestCase(false)][TestCase(true)]
        public async Task ReturningToParentRestoresItsScrollPositionAfterRealListing(bool controllerBack)
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-scroll-"+Guid.NewGuid()),child=Path.Combine(folder,"Child");Directory.CreateDirectory(child);
            try{
                for(int i=0;i<45;i++)File.WriteAllText(Path.Combine(folder,"Movie"+i.ToString("D3")+".mp4"),"");
                await app.Browse(new Uri(folder).AbsoluteUri);FullFileList.SetOffset(941);
                await app.Browse(new Uri(child).AbsoluteUri);Assert.That(FullFileList.Offset,Is.Zero);
                if(controllerBack)ui.Back();else FileButton("上级");await WaitForDirectory();
                Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(folder).AbsoluteUri));Assert.That(FullFileList.Offset,Is.EqualTo(941));
                float viewportHeight=FullFileList.GetComponent<ListScrollRect>().viewport.rect.height;
                Assert.That(FullFileList.PoolCount,Is.LessThanOrEqualTo(Mathf.CeilToInt(viewportHeight/VirtualFileList.RowPitch)+2));
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [TestCase(false)][TestCase(true)] public async Task ReturningToFilteredParentRestoresSearchAndPosition(bool keyboardOpen)
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-filter-scroll-"+Guid.NewGuid());Directory.CreateDirectory(folder);
            try{
                for(int i=0;i<35;i++)Directory.CreateDirectory(Path.Combine(folder,"Match"+i.ToString("D2")));
                for(int i=0;i<15;i++)Directory.CreateDirectory(Path.Combine(folder,"Other"+i.ToString("D2")));
                await app.Browse(new Uri(folder).AbsoluteUri);
                var filter=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("filter",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);filter.text="Match";FileButton("搜索");FullFileList.SetOffset(861);
                var row=FullFileList.GetComponentsInChildren<RowBinding>().First(binding=>binding.Item?.open!=null);row.Press();row.Open();await WaitForDirectory();
                Assert.That(app.DirectoryUri,Is.Not.EqualTo(new Uri(folder).AbsoluteUri));
                if(keyboardOpen){
                    var address=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("address",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
                    typeof(PlayerUI).GetMethod("ShowKeyboard",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{address});FileButton("上级");
                }else ui.Back();
                await WaitForDirectory();
                Assert.That(FullFileList.Count,Is.EqualTo(35));Assert.That(FullFileList.Offset,Is.EqualTo(861));
                filter=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("filter",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);Assert.That(filter.text,Is.EqualTo("Match"));
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public void LoadingPlaceholderAndTabChangesCannotOverwriteRememberedScroll()
        {
            DirectorySnapshot("smb://fixture/parent",50);FullFileList.SetOffset(1103);
            DirectorySnapshot("smb://fixture/child",20);FullFileList.SetOffset(239);
            DirectorySnapshot("smb://fixture/parent",0,true);Assert.That(FullFileList.Offset,Is.Zero);
            ui.SelectTab("投影");ui.SelectTab("文件");DirectorySnapshot("smb://fixture/parent",0,true);
            DirectorySnapshot("smb://fixture/parent",50);Assert.That(FullFileList.Offset,Is.EqualTo(1103));
            DirectorySnapshot("smb://fixture/child",20);Assert.That(FullFileList.Offset,Is.EqualTo(239));
        }
        [Test] public void RestoringAnEarlierVisitAcrossLoadingTabChangesKeepsItsOwnOffset()
        {
            // Simulate listing callbacks so this regression does not depend on worker completion timing.
            const string first="file:///fixture/repeated",other="file:///fixture/other",third="file:///fixture/third";
            DirectorySnapshot(first,40);ApplyFileFilter("Movie");FullFileList.SetOffset(241);var earlier=ui.CaptureFileView();
            DirectorySnapshot(other,40);DirectorySnapshot(first,40);FullFileList.SetOffset(941);DirectorySnapshot(third,40);
            ui.RestoreFileView(earlier,FileSource.Directory,first);DirectorySnapshot(first,0,true);
            Assert.That(FullFileList.Offset,Is.Zero);ui.SelectTab("投影");ui.SelectTab("文件");
            DirectorySnapshot(first,40);Assert.That(FileFilter,Is.EqualTo("Movie"));Assert.That(FullFileList.Offset,Is.EqualTo(241));
        }
        [Test] public async Task BackRestoresACompletedSearchWithNoMatches()
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-empty-search-back-"+Guid.NewGuid()),first=Path.Combine(folder,"First"),other=Path.Combine(folder,"Other");WriteMovieRows(first);Directory.CreateDirectory(other);
            try{
                app.Home();await app.Browse(new Uri(first).AbsoluteUri);ApplyFileFilter("Match");FullFileList.SetOffset(941);
                ApplyFileFilter("DoesNotExist");Assert.That(FullFileList.Count,Is.EqualTo(1));Assert.That(FullFileList.Offset,Is.Zero);
                var address=(TMPro.TMP_InputField)typeof(PlayerUI).GetField("address",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);address.text=new Uri(other).AbsoluteUri;FileButton("打开");await WaitForDirectory();
                ui.Back();await WaitForDirectory();Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(first).AbsoluteUri));Assert.That(app.Files.Count,Is.EqualTo(40));
                Assert.That(FileFilter,Is.EqualTo("DoesNotExist"));Assert.That(FullFileList.Count,Is.EqualTo(1));Assert.That(FullFileList.Offset,Is.Zero);Assert.That(ui.Visible);
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Home));
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [Test] public void RefreshAndTemporaryReadFailureKeepTheDirectoryPosition()
        {
            DirectorySnapshot("content://fixture/tree/parent",40);FullFileList.SetOffset(1029);
            DirectorySnapshot("content://fixture/tree/parent",0,true);DirectorySnapshot("content://fixture/tree/parent",0);
            DirectorySnapshot("content://fixture/tree/parent",0,true);DirectorySnapshot("content://fixture/tree/parent",40);
            Assert.That(FullFileList.Offset,Is.EqualTo(1029));
        }
        [Test] public void RestoredPositionClampsWhenDirectoryHasFewerFiles()
        {
            DirectorySnapshot("file:///fixture/parent",40);FullFileList.SetOffset(1700);DirectorySnapshot("file:///fixture/child",10);
            DirectorySnapshot("file:///fixture/parent",8);
            float viewportHeight=FullFileList.GetComponent<ListScrollRect>().viewport.rect.height;
            Assert.That(FullFileList.Offset,Is.EqualTo(Mathf.Max(0,8*VirtualFileList.RowPitch-viewportHeight)));
        }
        [Test] public void FileLocationOverridesCachedPositionAndLaterUpdatesPreserveUserScroll()
        {
            const string uri="file:///fixture/parent";DirectorySnapshot(uri,40);FullFileList.SetOffset(900);DirectorySnapshot("file:///fixture/other",10);
            ui.PrepareDirectoryNavigation();DirectorySnapshot(uri,40,locate:uri+"/movie30");
            float viewportHeight=FullFileList.GetComponent<ListScrollRect>().viewport.rect.height;
            Assert.That(FullFileList.Offset,Is.EqualTo(Mathf.Min(30*VirtualFileList.RowPitch,40*VirtualFileList.RowPitch-viewportHeight)));
            FullFileList.SetOffset(1200);DirectorySnapshot(uri,40,locate:uri+"/movie30");Assert.That(FullFileList.Offset,Is.EqualTo(1200));
        }
        [Test] public void ModalListsCannotReplaceDirectoryScrollMemory()
        {
            DirectorySnapshot("smb://fixture/parent",40);FullFileList.SetOffset(1177);
            typeof(PlayerUI).GetMethod("DrawServerForm",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null).Invoke(ui,null);
            ui.Back();Assert.That(FullFileList.Offset,Is.EqualTo(1177));
        }
        [Test] public void SortAndSubtitleViewsKeepIndependentPositions()
        {
            DirectorySnapshot("file:///fixture/parent",40);FullFileList.SetOffset(1221);
            FileButton("名称排序");Assert.That(FullFileList.Offset,Is.Zero);FullFileList.SetOffset(547);
            FileButton("视频文件");Assert.That(FullFileList.Offset,Is.Zero);FileButton("字幕文件");Assert.That(FullFileList.Offset,Is.EqualTo(547));
            FileButton("日期排序");Assert.That(FullFileList.Offset,Is.Zero);FileButton("大小排序");Assert.That(FullFileList.Offset,Is.EqualTo(1221));
        }
        [Test] public void BackFromManualSubtitleSourcesRestoresSubtitlePage()
        {
            app.Home();ui.SelectTab("字幕");var content=(RectTransform)typeof(PlayerUI).GetField("content",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            foreach(var button in content.GetComponentsInChildren<Button>())if(button.name=="手动选择 SRT"){button.onClick.Invoke();break;}
            Assert.That(ui.SelectedTab,Is.EqualTo("文件"));ui.Back();Assert.That(ui.SelectedTab,Is.EqualTo("字幕"));Assert.That(ui.Visible);
            ui.Back();Assert.That(ui.AnyVisible,Is.False);
        }
        [TestCase(false)][TestCase(true)] public async Task ManualSubtitleBackReturnsToEntryWithoutConsumingExistingFileHistory(bool browseInside)
        {
            string folder=Path.Combine(Path.GetTempPath(),"VRPlayer-subtitle-entry-back-"+Guid.NewGuid()),first=Path.Combine(folder,"First"),second=Path.Combine(first,"Second"),third=Path.Combine(second,"Third");WriteMovieRows(first);Directory.CreateDirectory(third);
            try{
                app.Home();await app.Browse(new Uri(first).AbsoluteUri);ApplyFileFilter("Match");FullFileList.SetOffset(739);await app.Browse(new Uri(second).AbsoluteUri);
                ui.SelectTab("字幕");FileButton("手动选择 SRT");Assert.That(ui.SelectedTab,Is.EqualTo("文件"));
                if(browseInside){
                    await app.Browse(new Uri(third).AbsoluteUri);ui.Back();await WaitForDirectory();
                    Assert.That(ui.SelectedTab,Is.EqualTo("文件"));Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(second).AbsoluteUri));Assert.That(ui.Visible);
                }
                ui.Back();Assert.That(ui.SelectedTab,Is.EqualTo("字幕"));Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(second).AbsoluteUri));Assert.That(ui.Visible);
                ui.SelectTab("文件");ui.Back();await WaitForDirectory();
                Assert.That(app.DirectoryUri,Is.EqualTo(new Uri(first).AbsoluteUri));Assert.That(FileFilter,Is.EqualTo("Match"));Assert.That(FullFileList.Offset,Is.EqualTo(739));
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Home));Assert.That(ui.Visible);ui.Back();Assert.That(ui.AnyVisible,Is.False);
            }finally{app.CancelListing();Directory.Delete(folder,true);}
        }
        [TestCase("投影")][TestCase("字幕")][TestCase("设置")][TestCase("信息")]
        public void BackFromTopLevelTabClosesFullPanel(string selected)
        {
            ui.SelectTab(selected);ui.Back();Assert.That(ui.AnyVisible,Is.False);
        }
        [Test] public void BackWhileHiddenPreservesPlaybackAndVisibility()
        {
            ui.Hide();string film=app.Current.uri;Assert.That(ui.Back(),Is.False);Assert.That(ui.AnyVisible,Is.False);Assert.That(app.Current.uri,Is.EqualTo(film));Assert.That(backend.seeks,Is.Zero);
        }
        [Test] public void MutingRemembersVolumeAcrossSettingsReload()
        {
            app.SetVolume(.37f);app.ToggleMute();Assert.That(store.Settings.volume,Is.Zero);Assert.That(backend.volume,Is.Zero);
            Set("Store",new PlayerStore(path));app.ToggleMute();Assert.That(backend.volume,Is.EqualTo(.37f).Within(.00001f));
        }
        [Test] public void TurningOffTwiceDoesNotEraseRememberedManualSubtitle()
        {
            app.EmbeddedSubtitle("generated");app.DisableSubtitles();app.DisableSubtitles();Assert.That(app.Current.subtitleSelection,Is.EqualTo(SubtitleSelection.Off));Assert.That(app.Current.subtitle,Is.Empty);
            var restored=new PlayerStore(path).Find(app.Current.uri);Assert.That(restored.subtitleBeforeOff,Is.EqualTo("track:generated"));
            Set("Current",restored);app.SetSubtitlesEnabled(true);Assert.That(app.Current.subtitleSelection,Is.EqualTo(SubtitleSelection.Manual));Assert.That(backend.track,Is.EqualTo("generated"));
        }
        [Test] public async Task ExplicitSubtitleEnableDoesNotChangeGlobalAutomaticLoading()
        {
            app.DisableSubtitles();app.SetSubtitlesEnabled(true);await Task.Yield();Assert.That(store.Settings.autoLoadSubtitles,Is.False);Assert.That(app.Current.subtitleSelection,Is.EqualTo(SubtitleSelection.Auto));
        }
        [Test] public async Task ReenablingAutomaticSubtitleRestoresPreviousFileWithoutGlobalAutoload()
        {
            string srt=Path.ChangeExtension(path,".srt"),uri=new Uri(srt).AbsoluteUri;
            try{
                File.WriteAllText(srt,"1\n00:00:01,000 --> 00:00:03,000\nRemember this subtitle");
                app.Current.subtitle=uri;app.DisableSubtitles();app.SetSubtitlesEnabled(true);
                for(int i=0;i<200&&app.Current.subtitle!=uri;i++)await Task.Delay(10);
                Assert.That(app.Current.subtitle,Is.EqualTo(uri));Assert.That(app.Current.subtitleSelection,Is.EqualTo(SubtitleSelection.Auto));Assert.That(store.Settings.autoLoadSubtitles,Is.False);
            }finally{if(File.Exists(srt))File.Delete(srt);}
        }
        [Test] public void MetadataChangeClearsSubtitleRestoreInformation()
        {
            var file=new FileEntry{name="Generated.mp4",uri=app.Current.uri,size=100};var record=store.Touch(file,"");record.subtitleBeforeOff="file:///old.srt";record.subtitleSelectionBeforeOff=SubtitleSelection.Manual;
            file.size=200;store.Touch(file,"");Assert.That(record.subtitleBeforeOff,Is.Empty);Assert.That(record.subtitleSelectionBeforeOff,Is.EqualTo(SubtitleSelection.Auto));
        }
        [TestCase(1)][TestCase(2)][TestCase(3)]
        public void OlderSettingsDefaultToOriginalPanoramaAndRememberedVolume(int version)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":"+version+",\"volume\":0,\"history\":[{\"uri\":\"file:///a.mp4\",\"view\":{\"projection\":2}}]}");
            Assert.That(settings.version,Is.EqualTo(3));Assert.That(settings.history[0].view.panoramaZoom,Is.EqualTo(1));Assert.That(settings.lastAudibleVolume,Is.EqualTo(.8f));
        }
        [Test] public void TriggerPressThatOpensPanelCannotClickUntilRelease()
        {
            var gate=new TriggerPressGate();Assert.That(gate.Update(0,true));gate.Suppress();Assert.That(gate.Update(0,true),Is.False);
            Assert.That(gate.Update(1,true),Is.False);Assert.That(gate.Update(0,false),Is.False);Assert.That(gate.Update(0,true));Assert.That(gate.Consumed(0),Is.False);
        }
        [Test] public void VolumeRegionRemainsHoveredUntilBothPointersLeave()
        {
            var region=root.AddComponent<QuickPointerRegion>();var left=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-100};var right=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-101};
            region.OnPointerEnter(left);region.OnPointerEnter(right);region.OnPointerExit(left);Assert.That(region.Hovered);region.OnPointerExit(right);Assert.That(region.Hovered,Is.False);
        }
        [Test] public void CancelledSeekAndSecondControllerReleaseCannotCommit()
        {
            var sliderRoot=new GameObject("Seek test",typeof(RectTransform),typeof(OwnedSlider),typeof(SeekGesture));var gesture=sliderRoot.GetComponent<SeekGesture>();int commits=0;gesture.Commit=_=>commits++;
            var left=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-100};var right=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-101};
            try{gesture.OnPointerDown(left);gesture.OnPointerDown(right);gesture.OnPointerUp(right);gesture.Cancel(right.pointerId);Assert.That(gesture.Dragging);Assert.That(commits,Is.Zero);gesture.Cancel(left.pointerId);gesture.OnPointerUp(left);Assert.That(commits,Is.Zero);
                gesture.OnPointerDown(left);gesture.OnPointerUp(left);Assert.That(commits,Is.EqualTo(1));
            }finally{UnityEngine.Object.DestroyImmediate(sliderRoot);}
        }
        [Test] public void PopupSuppressesHorizontalSeekWhileAllowingVerticalScroll()
        {
            var gesture=new StickTransportGesture();Assert.That(gesture.Update(Vector2.up,true,Vector2.zero,true,false,0,true),Is.Zero);Assert.That(gesture.Scroll,Is.EqualTo(1));
            gesture.Update(Vector2.zero,true,Vector2.zero,true,false,1,true);Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,2,true),Is.Zero);
            Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,3,false),Is.Zero);
        }
        [TestCase(true,true,false,5,true)][TestCase(true,true,true,20,false)][TestCase(true,false,false,20,false)][TestCase(false,true,false,20,false)][TestCase(true,true,false,4.99f,false)]
        public void QuickPanelHidesOnlyAfterFiveIdlePlayingSeconds(bool enabled,bool playing,bool interacting,float now,bool hidden)
        {
            Assert.That(QuickControlsUI.ShouldAutoHide(enabled,playing,interacting,now,0),Is.EqualTo(hidden));
        }
        [Test] public void ZoomDoesNotLockAutomaticFormatDetection()
        {
            app.SetPanoramaZoom(2);Assert.That(app.Current.formatLocked,Is.False);Assert.That(new PlayerStore(path).Find(app.Current.uri).view.panoramaZoom,Is.EqualTo(2));
        }
        [Test] public void NewPlaybackGenerationAndCancellationCannotCommitQuickSeek()
        {
            var surface=ui.Quick.Canvas.GetComponentInChildren<QuickSeekSurface>(true);var pointer=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-100};
            surface.OnPointerDown(pointer);Set("Generation",2);surface.OnPointerUp(pointer);Assert.That(backend.seeks,Is.Zero);
            Set("Generation",1);surface.OnPointerDown(pointer);surface.Cancel();surface.OnPointerUp(pointer);Assert.That(backend.seeks,Is.Zero);
        }
        [Test] public void FullProjectionPageUpdatesDistanceRangeAfterProjectionChange()
        {
            ui.SelectTab("投影");app.ChangeFormat(view=>view.projection=Projection.Equirect360);
            typeof(PlayerUI).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
            var content=(RectTransform)typeof(PlayerUI).GetField("content",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            bool zoom=false;foreach(var slider in content.GetComponentsInChildren<Slider>())if(slider.minValue==.5f&&slider.maxValue==2)zoom=true;
            Assert.That(zoom);
        }
        [Test] public void CancellingOtherControllerCannotEndQuickSeek()
        {
            var surface=ui.Quick.Canvas.GetComponentInChildren<QuickSeekSurface>(true);
            var left=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-100};var right=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-101};
            surface.OnPointerDown(left);surface.OnPointerDown(right);surface.Cancel(right.pointerId);surface.OnPointerUp(right);
            Assert.That(surface.Dragging);Assert.That(backend.seeks,Is.Zero);
            surface.OnPointerUp(left);Assert.That(backend.seeks,Is.EqualTo(1));Assert.That(surface.Dragging,Is.False);
        }
        [Test] public void TabOpensFullMenuWhileQuickPopupIsOpen()
        {
            ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("mode");Assert.That(ui.BlocksTransport);
            var input=root.AddComponent<VrUiInput>();typeof(VrUiInput).GetField("app",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(input,app);
            var shortcut=typeof(VrUiInput).GetMethod("HandleKeyboardShortcut",BindingFlags.Instance|BindingFlags.NonPublic);
            shortcut.Invoke(input,new object[]{Key.RightArrow,false});Assert.That(backend.seeks,Is.Zero);
            shortcut.Invoke(input,new object[]{Key.Tab,true});Assert.That(ui.Quick.Visible);
            shortcut.Invoke(input,new object[]{Key.Tab,false});Assert.That(ui.Visible);Assert.That(ui.Quick.Visible,Is.False);Assert.That(ui.Quick.Popup,Is.Empty);
        }
        [Test] public void ModePopupResetRestoresOnlyCurrentViewDistance()
        {
            app.ChangeFormat(view=>{view.projection=Projection.Equirect360;view.stereo=StereoLayout.SideBySide;view.distance=7;view.panoramaZoom=2;});
            ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("mode");
            var popup=(RectTransform)typeof(QuickControlsUI).GetField("popupRoot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui.Quick);
            Button reset=null;foreach(var button in popup.GetComponentsInChildren<Button>()){
                Assert.That(button.name,Is.Not.EqualTo("重置"));if(button.name=="重置远近")reset=button;
            }
            Assert.That(reset,Is.Not.Null);reset.onClick.Invoke();
            Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));Assert.That(app.Presenter.Settings.distance,Is.EqualTo(7));
            Assert.That(app.Presenter.Settings.projection,Is.EqualTo(Projection.Equirect360));Assert.That(app.Presenter.Settings.stereo,Is.EqualTo(StereoLayout.SideBySide));Assert.That(app.Current.formatLocked);
        }
        private System.Collections.Generic.IReadOnlyList<BrowserRow> RecentRows=>
            (System.Collections.Generic.IReadOnlyList<BrowserRow>)typeof(VirtualFileList).GetField("items",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(FullFileList);

        [TestCase(0)][TestCase(1)][TestCase(2)] public void RecentOrderIgnoresInheritedDirectorySortingAndRefreshesRepeatedOpens(int inheritedSort)
        {
            store.Settings.history.Clear();
            var a=new FileEntry{name="A.mp4",uri="file:///a.mp4",modified=999,size=999};var z=new FileEntry{name="Z.mp4",uri="file:///z.mp4",modified=1,size=1};
            store.Touch(a,"file:///");store.Touch(z,"file:///");
            typeof(PlayerUI).GetField("sort",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ui,inheritedSort);
            app.ShowRecent();Assert.That(RecentRows.Select(row=>row.key),Is.EqualTo(new[]{z.uri,a.uri}));
            var record=store.Find(a.uri);record.lastPlayed="2000-01-01T00:00:00Z";record.position=15000;
            store.Touch(a,"file:///");app.Notify();
            Assert.That(app.Files.Select(file=>file.uri),Is.EqualTo(new[]{a.uri,z.uri}));
            Assert.That(RecentRows.Select(row=>row.key),Is.EqualTo(new[]{a.uri,z.uri}));
            Assert.That(RecentRows[0].subtitle,Does.Contain("本地").And.Contain("00:15"));
            Assert.That(store.Settings.history.Count,Is.EqualTo(2));Assert.That(DateTimeOffset.Parse(record.lastPlayed).Year,Is.GreaterThan(2000));
        }

        [Test] public void RecentAlwaysShowsVideosAndEmbyTitlesWithoutExtensions()
        {
            store.Settings.history.Clear();store.Touch(new FileEntry{name="本地.mp4",uri="file:///movie.mp4"},"file:///");
            store.Touch(new FileEntry{name="远程影片",uri="emby://account/movie"},"emby://account/main");
            typeof(PlayerUI).GetField("subtitleBrowse",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ui,true);
            app.ShowRecent();Assert.That(RecentRows.Count,Is.EqualTo(2));
            Assert.That(RecentRows[0].subtitle,Does.StartWith("Emby · "));Assert.That(RecentRows[0].secondary,Is.EqualTo("影片详情"));
            var content=(RectTransform)typeof(PlayerUI).GetField("content",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            Assert.That(content.GetComponentsInChildren<Button>().First(button=>button.name=="最近播放优先").interactable,Is.False);
        }

        [Test] public void RecentTitleAndProgressRenderTogetherAndRecycledDirectoryRowsKeepOneLine()
        {
            store.Settings.history.Clear();store.Touch(new FileEntry{name="测试影片 CD2",uri="emby://account/cd2"},"emby://account/main").position=11000;
            app.ShowRecent();Canvas.ForceUpdateCanvases();FullFileList.SetOffset(0);
            var row=FullFileList.GetComponentsInChildren<RowBinding>().First(binding=>binding.Item?.key=="emby://account/cd2");
            var title=row.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single(label=>label.text.Contains("测试影片"));
            var progress=row.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single(label=>label.text.Contains("进度"));
            title.ForceMeshUpdate();progress.ForceMeshUpdate();
            Assert.That(title.textInfo.characterInfo.Take(title.textInfo.characterCount).Any(character=>character.isVisible),Is.True);
            Assert.That(progress.textInfo.characterInfo.Take(progress.textInfo.characterCount).Any(character=>character.isVisible),Is.True);
            FullFileList.SetRows(new[]{new BrowserRow{key="directory",label="普通目录",open=()=>{}}});
            Assert.That(row.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(label=>(label.text??"").Contains("进度")),Is.False);
            title.ForceMeshUpdate();Assert.That(title.textInfo.characterInfo.Take(title.textInfo.characterCount).Any(character=>character.isVisible),Is.True);
        }

        [Test] public void SavingProgressUpdatesVisibleRecentRowsAndClearingRemainsEmpty()
        {
            app.ShowRecent();backend.Snapshot.position=27000;app.SaveProgress();
            Assert.That(RecentRows[0].subtitle,Does.Contain("00:27"));
            app.ClearPlaybackHistory();Assert.That(store.Settings.history,Is.Empty);Assert.That(RecentRows[0].key,Is.EqualTo("empty"));
            app.SaveProgress();Assert.That(store.Settings.history,Is.Empty);Assert.That(RecentRows[0].key,Is.EqualTo("empty"));
        }

        [Test] public void OpeningALocalRecentRowMovesItFirstAndKeepsItsStoredParent()
        {
            var file=new FileEntry{name="Z.mp4",uri="file:///z.mp4"};store.Touch(file,"file:///owned-parent");
            store.Touch(new FileEntry{name="A.mp4",uri="file:///a.mp4"},"file:///");app.ShowRecent();
            RecentRows.First(row=>row.key==file.uri).open();
            Assert.That(app.Current.uri,Is.EqualTo(file.uri));Assert.That(app.Current.directory,Is.EqualTo("file:///owned-parent"));
            Assert.That(RecentRows[0].key,Is.EqualTo(file.uri));Assert.That(FullFileList.Offset,Is.Zero);
            Assert.That(new PlayerStore(path).Settings.history[0].uri,Is.EqualTo(file.uri),"A new open must persist without waiting for the periodic progress timer");
        }

        [Test] public void MissingEmbyHistoryAccountOpensLoginAndBackReturnsToRecent()
        {
            var uri="emby://missing/movie";store.Touch(new FileEntry{name="Emby 影片",uri=uri},"emby://missing/main");app.ShowRecent();
            app.OpenRecentOrFile(app.Files.First(file=>file.uri==uri));
            Assert.That(app.Source,Is.EqualTo(FileSource.Emby));Assert.That(app.EmbyState.page,Is.EqualTo(EmbyPage.Accounts));
            Assert.That(app.Current.uri,Is.EqualTo("file:///generated.mp4"));
            ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Recent));Assert.That(RecentRows[0].key,Is.EqualTo(uri));
        }

        [TestCase(false)][TestCase(true)] public async Task EmbyHistoryJumpLoadsTheStoredAccountAndDoesNotPlayAfterReturn(bool detailsOnly)
        {
            string accountsPath=path+".accounts";var gate=new TaskCompletionSource<bool>();
            var accounts=new EmbyAccountStore(accountsPath,()=>"",_=>true);
            var account=new EmbyAccount{id="history",serverUrl="http://example.test:8096",serverId="fixture-server",userId="fixture-user",accessToken="fixture-token",remember=false};
            accounts.Upsert(account);
            typeof(PlayerApp).GetField("embyAccounts",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,accounts);
            typeof(PlayerApp).GetField("embyReports",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,gate.Task);
            string uri=EmbyIdentity.Create(account.id,"cd2");store.Touch(new FileEntry{name="CD2",uri=uri},EmbyIdentity.Create(account.id,"main"));
            try{
                app.ShowRecent();long generation=app.Generation;
                if(detailsOnly)app.OpenContainingDirectory(uri);else app.OpenRecentOrFile(app.Files.First(file=>file.uri==uri));
                Assert.That(app.Source,Is.EqualTo(FileSource.Emby));Assert.That(app.ActiveEmbyAccount,Is.SameAs(account));
                Assert.That(app.EmbyState.page,Is.EqualTo(EmbyPage.Details));Assert.That(app.EmbyState.selected.Id,Is.EqualTo("cd2"));
                Assert.That(app.EmbyState.loading,Is.True);
                var opening=(System.Threading.CancellationTokenSource)typeof(PlayerApp).GetField("embyOpening",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
                Assert.That(opening!=null,Is.EqualTo(!detailsOnly));
                ui.Back();Assert.That(app.Source,Is.EqualTo(FileSource.Recent));gate.SetResult(true);await Task.Yield();
                Assert.That(app.Generation,Is.EqualTo(generation));Assert.That(app.Current.uri,Is.EqualTo("file:///generated.mp4"));
                Assert.That(JsonUtility.ToJson(store.Settings),Does.Not.Contain("fixture-token").And.Not.Contain("example.test"));
            }finally{
                app.CancelListing();gate.TrySetResult(true);await Task.Yield();
                typeof(PlayerApp).GetField("embyReports",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,Task.CompletedTask);
                if(File.Exists(accountsPath))File.Delete(accountsPath);
            }
        }

        private sealed class TestBackend : IMediaBackend
        {
            public MediaSnapshot Snapshot{get;private set;}=new MediaSnapshot{tracks=new[]{new MediaTrack{id="generated",type=3,supported=true,name="Generated subtitle"}}};
            public float volume;public string track;public int seeks,stops;
            public Texture Texture=>null;public bool RequiresGammaConversion=>false;public int RenderedFrames=>0;
            public void Open(string uri,long position,int generation)=>Snapshot=new MediaSnapshot{generation=generation,duration=120000,position=position};
            public void Tick(){}public void Play(){}public void Pause(){}public void Stop(){stops++;Snapshot.playing=false;}public void Seek(long value){Snapshot.position=value;seeks++;}
            public void SetVolume(float value)=>volume=value;public void SetSpeed(float value){}public void SetRepeat(bool value){}public void SelectTrack(string id,int type){if(type==3)track=id;}
            public void Dispose(){}
        }
    }
}
