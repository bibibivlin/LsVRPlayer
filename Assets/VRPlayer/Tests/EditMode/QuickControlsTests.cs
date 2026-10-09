using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class QuickControlsTests
    {
        private GameObject root,head,events;
        private PlayerApp app;private PlayerUI ui;private PlayerStore store;private TestBackend backend;private string path;
        private void Set(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
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
        [Test] public void OpeningAnotherFilmPreservesHiddenOrQuickSurface()
        {
            ui.Hide();app.Open(new FileEntry{name="Other.mp4",uri="file:///other.mp4"},"");Assert.That(ui.AnyVisible,Is.False);
            ui.ShowQuick();app.Open(new FileEntry{name="Third.mp4",uri="file:///third.mp4"},"");Assert.That(ui.Quick.Visible);Assert.That(ui.Visible,Is.False);
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
        private sealed class TestBackend : IMediaBackend
        {
            public MediaSnapshot Snapshot{get;private set;}=new MediaSnapshot{tracks=new[]{new MediaTrack{id="generated",type=3,supported=true,name="Generated subtitle"}}};
            public float volume;public string track;public int seeks;
            public Texture Texture=>null;public bool RequiresGammaConversion=>false;public int RenderedFrames=>0;
            public void Open(string uri,long position,int generation)=>Snapshot=new MediaSnapshot{generation=generation,duration=120000,position=position};
            public void Tick(){}public void Play(){}public void Pause(){}public void Stop(){}public void Seek(long value){Snapshot.position=value;seeks++;}
            public void SetVolume(float value)=>volume=value;public void SetSpeed(float value){}public void SetRepeat(bool value){}public void SelectTrack(string id,int type){if(type==3)track=id;}
            public void Dispose(){}
        }
    }
}
