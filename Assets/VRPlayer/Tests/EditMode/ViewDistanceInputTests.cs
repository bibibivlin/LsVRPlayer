using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VRPlayer.Tests
{
    public sealed class ViewDistanceInputTests
    {
        private GameObject root,head,events;
        private PlayerApp app;private PlayerUI ui;private VrUiInput input;private PlayerStore store;private TestBackend backend;private string path;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private void Set(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
        private void Tick(Vector2 left,float elapsed=.25f,bool valid=true,bool typing=false)
        {
            var axes=(Vector2[])typeof(VrUiInput).GetField("stickAxes",Private).GetValue(input);axes[0]=left;
            var available=(bool[])typeof(VrUiInput).GetField("stickValid",Private).GetValue(input);available[0]=valid;
            typeof(VrUiInput).GetMethod("ProcessStickControls",Private).Invoke(input,new object[]{typing,Time.unscaledTime,elapsed});
        }
        private ViewSettings Saved()=>new PlayerStore(path).Find(app.Current.uri).view;
        [SetUp] public void Setup()
        {
            path=Path.Combine(Path.GetTempPath(),"VRPlayer-distance-"+Guid.NewGuid()+".json");store=new PlayerStore(path);store.Settings.formatDetection=FormatDetectionMode.Metadata;
            root=new GameObject("View distance input test");root.SetActive(false);app=root.AddComponent<PlayerApp>();backend=new TestBackend();
            Set("Store",store);Set("Backend",backend);Set("Presenter",root.AddComponent<VideoPresenter>());Set("Generation",1);
            Set("Current",store.Touch(new FileEntry{name="Generated.mp4",uri="file:///generated.mp4"},""));app.Presenter.Settings=app.Current.view;store.Save();
            head=new GameObject("Test head",typeof(Camera));ui=root.AddComponent<PlayerUI>();Set("UI",ui);ui.Initialize(app,head.GetComponent<Camera>());ui.Hide();
            events=new GameObject("Distance pointer test",typeof(EventSystem));input=root.AddComponent<VrUiInput>();typeof(VrUiInput).GetField("app",Private).SetValue(input,app);
            var pointers=(PointerEventData[])typeof(VrUiInput).GetField("pointers",Private).GetValue(input);
            for(int i=0;i<pointers.Length;i++)pointers[i]=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-100-i};
        }
        [TearDown] public void Cleanup()
        {
            var quickCanvas=ui.Quick.Canvas;typeof(QuickControlsUI).GetProperty("Canvas").SetValue(ui.Quick,null);UnityEngine.Object.DestroyImmediate(quickCanvas.gameObject);
            var canvas=ui.Canvas;typeof(PlayerUI).GetProperty("Canvas").SetValue(ui,null);UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            var hud=(RectTransform)typeof(PlayerUI).GetField("transportRoot",Private).GetValue(ui);UnityEngine.Object.DestroyImmediate(hud.gameObject);
            Set("Presenter",null);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head);UnityEngine.Object.DestroyImmediate(events);
            if(File.Exists(path))File.Delete(path);
        }
        [TestCase(0f)][TestCase(.5f)]
        public void HiddenFlatAndCurvedScreensMoveNearerOnStickUp(float curvature)
        {
            app.Presenter.Settings.curvature=curvature;Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
            Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
            Tick(Vector2.up,100);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(1.2f));Tick(Vector2.down,100);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(10));
            Assert.That(app.Current.formatLocked,Is.False);Assert.That(app.Presenter.Settings.curvature,Is.EqualTo(curvature));
        }
        [TestCase(Projection.Equirect180)][TestCase(Projection.Equirect360)][TestCase(Projection.Fisheye)][TestCase(Projection.Cubemap3x2)][TestCase(Projection.Eac3x2)]
        public void HiddenPanoramasEnlargeOnStickUpAndRespectZoomLimits(Projection projection)
        {
            app.Presenter.Settings.projection=projection;Tick(Vector2.up);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1.125f));
            Tick(Vector2.down);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));
            Tick(Vector2.up,100);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(2));Tick(Vector2.down,100);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(.5f));
            Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Assert.That(app.Current.formatLocked,Is.False);
        }
        [TestCase(Projection.Flat)][TestCase(Projection.Equirect360)]
        public void DistanceResetPreservesFormatAndInactiveDistance(Projection projection)
        {
            var view=app.Presenter.Settings;view.projection=projection;view.stereo=StereoLayout.TopBottom;view.curvature=.4f;view.distance=6;view.panoramaZoom=1.6f;view.swapEyes=true;
            app.Current.formatLocked=true;app.Current.formatLockOrigin=FormatLockOrigin.Inherited;app.ResetViewDistance();
            Assert.That(view.distance,Is.EqualTo(projection==Projection.Flat?3.5f:6));Assert.That(view.panoramaZoom,Is.EqualTo(projection==Projection.Flat?1.6f:1));
            Assert.That(view.projection,Is.EqualTo(projection));Assert.That(view.stereo,Is.EqualTo(StereoLayout.TopBottom));Assert.That(view.curvature,Is.EqualTo(.4f));Assert.That(view.swapEyes);
            Assert.That(app.Current.formatLocked);Assert.That(app.Current.formatLockOrigin,Is.EqualTo(FormatLockOrigin.Inherited));
            Assert.That(Saved().distance,Is.EqualTo(view.distance));Assert.That(Saved().panoramaZoom,Is.EqualTo(view.panoramaZoom));
        }
        [Test] public void ReturningStickToNeutralPersistsDistanceWithoutSavingEveryTick()
        {
            Tick(Vector2.up);Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));Assert.That(Saved().distance,Is.EqualTo(3.5f));
            Tick(Vector2.zero);Assert.That(Saved().distance,Is.EqualTo(2.5f));
        }
        [Test] public void VisibleFullAndQuickPanelsKeepVerticalInputForTheirLists()
        {
            ui.Show();Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
            ui.Hide();ui.ShowQuick();Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
        }
        [Test] public void SurfaceChangeRequiresNeutralBeforeHiddenDistanceAdjustmentResumes()
        {
            Tick(Vector2.up);ui.Show();typeof(VrUiInput).GetMethod("CancelAllPointers",Private).Invoke(input,null);Assert.That(Saved().distance,Is.EqualTo(3));
            ui.Hide();Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
            Tick(Vector2.zero);Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));
        }
        [TestCase("OnApplicationFocus",false)][TestCase("OnApplicationPause",true)]
        public void LifecycleInterruptionPersistsAndSuppressesHeldDistanceInput(string message,bool value)
        {
            Tick(Vector2.up);typeof(VrUiInput).GetMethod(message,Private).Invoke(input,new object[]{value});Assert.That(Saved().distance,Is.EqualTo(3));
            Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));Tick(Vector2.zero);Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));
        }
        [Test] public void LostTrackingPersistsAndNeedsNeutralBeforeAdjustingAgain()
        {
            Tick(Vector2.up);Tick(Vector2.up,valid:false);Assert.That(Saved().distance,Is.EqualTo(3));
            Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));Tick(Vector2.zero);Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));
        }
        [Test] public void HeldPointerBlocksDistanceAndRequiresNeutralAfterRelease()
        {
            var held=(bool[])typeof(VrUiInput).GetField("held",Private).GetValue(input);held[0]=true;Tick(Vector2.up);held[0]=false;Tick(Vector2.up);
            Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Tick(Vector2.zero);Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
        }
        [Test] public void TwoVerticalSticksDoNotDoubleDistanceAdjustment()
        {
            input.SetDiagnosticStick(1,Vector2.up);Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
        }
        [Test] public void HorizontalStickStillSeeksWithoutChangingDistance()
        {
            Tick(Vector2.right);Assert.That(backend.seeks,Is.EqualTo(1));Assert.That(backend.Snapshot.position,Is.EqualTo(10000));Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
        }
        [Test] public void NoFilmOrFocusedTextPreventsDistanceAdjustment()
        {
            Tick(Vector2.up,typing:true);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Tick(Vector2.zero);Set("Current",null);Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
        }
        private sealed class TestBackend : IMediaBackend
        {
            public MediaSnapshot Snapshot{get;}=new MediaSnapshot{generation=1,duration=120000};public int seeks;
            public Texture Texture=>null;public bool RequiresGammaConversion=>false;public int RenderedFrames=>0;
            public void Open(string uri,long position,int generation){}public void Tick(){}public void Play(){}public void Pause(){}public void Stop(){}public void Seek(long value){Snapshot.position=value;seeks++;}
            public void SetVolume(float value){}public void SetSpeed(float value){}public void SetRepeat(bool value){}public void SelectTrack(string id,int type){}public void Dispose(){}
        }
    }
}
