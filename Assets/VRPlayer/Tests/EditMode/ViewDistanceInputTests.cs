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
        private bool ControllerBack(bool leftPressed,bool rightPressed,bool leftValid=true,bool rightValid=true)
            =>(bool)typeof(VrUiInput).GetMethod("HandleControllerBack",Private).Invoke(input,new object[]{leftPressed,leftValid,rightPressed,rightValid});
        private bool ControllerBack(int hand,bool pressed,bool valid=true)
            =>ControllerBack(hand==0&&pressed,hand==1&&pressed,hand!=0||valid,hand!=1||valid);
        private void Tick(Vector2 left,float elapsed=.25f,bool valid=true,bool typing=false)
        {
            var axes=(Vector2[])typeof(VrUiInput).GetField("stickAxes",Private).GetValue(input);axes[0]=left;
            var available=(bool[])typeof(VrUiInput).GetField("stickValid",Private).GetValue(input);available[0]=valid;
            typeof(VrUiInput).GetMethod("ProcessStickControls",Private).Invoke(input,new object[]{typing,Time.unscaledTime,elapsed});
        }
        private ViewSettings Saved()=>new PlayerStore(path).Find(app.Current.uri).view;
        private void Grip(int hand,bool pressed)=>((bool[])typeof(VrUiInput).GetField("gripHeld",Private).GetValue(input))[hand]=pressed;
        private void TickHand(int hand,Vector2 axis,float elapsed=.25f,bool valid=true,bool typing=false)
        {
            ((Vector2[])typeof(VrUiInput).GetField("stickAxes",Private).GetValue(input))[hand]=axis;
            ((bool[])typeof(VrUiInput).GetField("stickValid",Private).GetValue(input))[hand]=valid;
            typeof(VrUiInput).GetMethod("ProcessStickControls",Private).Invoke(input,new object[]{typing,Time.unscaledTime,elapsed});
        }
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
        public void HiddenFlatAndCurvedScreensMoveNearerOnStickDown(float curvature)
        {
            Grip(0,true);
            app.Presenter.Settings.curvature=curvature;Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
            Tick(Vector2.up);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
            Tick(Vector2.down,100);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(1.2f));Tick(Vector2.up,100);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(10));
            Assert.That(app.Current.formatLocked,Is.False);Assert.That(app.Presenter.Settings.curvature,Is.EqualTo(curvature));
        }
        [TestCase(Projection.Equirect180)][TestCase(Projection.Equirect360)][TestCase(Projection.Fisheye)][TestCase(Projection.Cubemap3x2)][TestCase(Projection.Eac3x2)]
        public void HiddenPanoramasEnlargeOnStickDownAndRespectZoomLimits(Projection projection)
        {
            Grip(0,true);
            app.Presenter.Settings.projection=projection;Tick(Vector2.down);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1.125f));
            Tick(Vector2.up);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));
            Tick(Vector2.down,100);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(2));Tick(Vector2.up,100);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(.5f));
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
            Grip(0,true);
            Tick(Vector2.down);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));Assert.That(Saved().distance,Is.EqualTo(3.5f));
            Tick(Vector2.zero);Assert.That(Saved().distance,Is.EqualTo(2.5f));
        }
        [Test] public void VisibleFullAndQuickPanelsKeepVerticalInputForTheirLists()
        {
            ui.Show();Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
            ui.Hide();ui.ShowQuick();Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
        }
        [TestCase(0)][TestCase(1)]
        public void EitherGripAndStickMovesOnlyTheVisibleMainPanel(int hand)
        {
            ui.Show();head.transform.rotation=Quaternion.Euler(12,35,0);ui.Show();
            var before=ui.Canvas.transform.position;var rotation=ui.Canvas.transform.rotation;var scale=ui.Canvas.transform.localScale;
            Grip(hand,true);TickHand(hand,Vector2.down);
            Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(1.5f));
            Assert.That(Vector3.Distance(ui.Canvas.transform.position,before-ui.Canvas.transform.forward*.5f),Is.LessThan(.0001));
            Assert.That(ui.Canvas.transform.rotation,Is.EqualTo(rotation));Assert.That(ui.Canvas.transform.localScale,Is.EqualTo(scale));
            Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));Assert.That(app.Current.formatLocked,Is.False);
            TickHand(hand,Vector2.up);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
            TickHand(hand,Vector2.down,100);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(1.2f));
            TickHand(hand,Vector2.up,100);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(6));
        }
        [TestCase(0)][TestCase(1)]
        public void PanelDistanceWorksWithoutAFilmAndPersistsAtGestureEnd(int hand)
        {
            Set("Current",null);ui.Show();Grip(hand,true);TickHand(hand,Vector2.up);
            Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2.5f));Assert.That(new PlayerStore(path).Settings.mainPanelDistance,Is.EqualTo(2));
            TickHand(hand,Vector2.zero);Assert.That(new PlayerStore(path).Settings.mainPanelDistance,Is.EqualTo(2.5f));
            ui.Hide();head.transform.position=new Vector3(2,1,3);ui.Show();
            Assert.That(Vector3.Distance(ui.Canvas.transform.position,head.transform.position+new Vector3(0,-.05f,2.5f)),Is.LessThan(.0001));
        }
        [TestCase(0)][TestCase(1)]
        public void ChangingGripModeRequiresNeutralBeforeReusingAHeldStick(int hand)
        {
            ui.Show();TickHand(hand,Vector2.down);Grip(hand,true);TickHand(hand,Vector2.down);
            Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
            TickHand(hand,Vector2.zero);TickHand(hand,Vector2.up);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2.5f));
            Grip(hand,false);TickHand(hand,Vector2.up);
            var gesture=(StickTransportGesture)typeof(VrUiInput).GetField("transport",Private).GetValue(input);
            Assert.That(gesture.Scroll,Is.Zero);Assert.That(new PlayerStore(path).Settings.mainPanelDistance,Is.EqualTo(2.5f));
            TickHand(hand,Vector2.zero);TickHand(hand,Vector2.up);Assert.That(gesture.Scroll,Is.EqualTo(1));
        }
        [TestCase(0)][TestCase(1)]
        public void PanelGripDoesNotSeekOrUseTheOtherHandsUnmodifiedStick(int hand)
        {
            ui.Show();Grip(hand,true);TickHand(hand,Vector2.right);Assert.That(backend.seeks,Is.Zero);
            TickHand(hand,Vector2.zero);TickHand(1-hand,Vector2.down);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
            TickHand(hand,Vector2.down);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(1.5f));Assert.That(backend.seeks,Is.Zero);
        }
        [Test] public void TwoGrippedSticksMoveThePanelOnlyOnceAndDoNotScrollTheList()
        {
            ui.Show();ui.SelectTab("信息");var body=(RectTransform)typeof(PlayerUI).GetField("infoBody",Private).GetValue(ui);body.anchoredPosition=new Vector2(0,80);
            Grip(0,true);Grip(1,true);input.SetDiagnosticStick(1,Vector2.up);Tick(Vector2.up);
            Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2.5f));Assert.That(body.anchoredPosition.y,Is.EqualTo(80));
        }
        [TestCase("OnApplicationFocus",false)][TestCase("OnApplicationPause",true)]
        public void InterruptingPanelMovementSavesAndRequiresNeutral(string message,bool value)
        {
            ui.Show();Grip(0,true);Tick(Vector2.down);typeof(VrUiInput).GetMethod(message,Private).Invoke(input,new object[]{value});
            Assert.That(new PlayerStore(path).Settings.mainPanelDistance,Is.EqualTo(1.5f));Tick(Vector2.up);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(1.5f));
            Tick(Vector2.zero);Tick(Vector2.up);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
        }
        [TestCase(0)][TestCase(1)]
        public void PanelTrackingLossAndFocusedTextBlockMovement(int hand)
        {
            ui.Show();Grip(hand,true);TickHand(hand,Vector2.down,typing:true);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
            TickHand(hand,Vector2.zero);TickHand(hand,Vector2.down);TickHand(hand,Vector2.up,valid:false);
            Assert.That(new PlayerStore(path).Settings.mainPanelDistance,Is.EqualTo(1.5f));TickHand(hand,Vector2.up);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(1.5f));
            TickHand(hand,Vector2.zero);TickHand(hand,Vector2.up);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
        }
        [Test] public void QuickAndHiddenSurfacesNeverMoveTheMainPanel()
        {
            Grip(0,true);Tick(Vector2.down);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
            Tick(Vector2.zero);ui.ShowQuick();Tick(Vector2.up);Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));
            Assert.That(app.AdjustMainPanelDistance(1,.25f),Is.False);
        }
        [Test] public void SurfaceChangeRequiresNeutralBeforeHiddenDistanceAdjustmentResumes()
        {
            Grip(0,true);
            Tick(Vector2.down);ui.Show();typeof(VrUiInput).GetMethod("CancelAllPointers",Private).Invoke(input,null);Assert.That(Saved().distance,Is.EqualTo(3));
            ui.Hide();Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
            Tick(Vector2.zero);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));
        }
        [TestCase("OnApplicationFocus",false)][TestCase("OnApplicationPause",true)]
        public void LifecycleInterruptionPersistsAndSuppressesHeldDistanceInput(string message,bool value)
        {
            Grip(0,true);
            Tick(Vector2.down);typeof(VrUiInput).GetMethod(message,Private).Invoke(input,new object[]{value});Assert.That(Saved().distance,Is.EqualTo(3));
            Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));Tick(Vector2.zero);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));
        }
        [Test] public void LostTrackingPersistsAndNeedsNeutralBeforeAdjustingAgain()
        {
            Grip(0,true);
            Tick(Vector2.down);Tick(Vector2.down,valid:false);Assert.That(Saved().distance,Is.EqualTo(3));
            Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));Tick(Vector2.zero);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(2.5f));
        }
        [Test] public void HeldPointerBlocksDistanceAndRequiresNeutralAfterRelease()
        {
            Grip(0,true);
            var held=(bool[])typeof(VrUiInput).GetField("held",Private).GetValue(input);held[0]=true;Tick(Vector2.down);held[0]=false;Tick(Vector2.down);
            Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Tick(Vector2.zero);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
        }
        [Test] public void TwoVerticalSticksDoNotDoubleDistanceAdjustment()
        {
            Grip(0,true);Grip(1,true);
            input.SetDiagnosticStick(1,Vector2.down);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
        }
        [Test] public void HorizontalStickStillSeeksWithoutChangingDistance()
        {
            Tick(Vector2.right);Assert.That(backend.seeks,Is.EqualTo(1));Assert.That(backend.Snapshot.position,Is.EqualTo(10000));Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
        }
        [TestCase(Projection.Flat)][TestCase(Projection.Equirect360)]
        public void HiddenVideoRequiresTheSameHandsGripBeforeDistanceInput(Projection projection)
        {
            app.Presenter.Settings.projection=projection;
            Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));
            Grip(0,true);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));
            Tick(Vector2.zero);Grip(0,false);Grip(1,true);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));
            TickHand(1,Vector2.zero);TickHand(1,Vector2.down);Assert.That(projection==Projection.Flat?app.Presenter.Settings.distance:app.Presenter.Settings.panoramaZoom,Is.EqualTo(projection==Projection.Flat?3:1.125f));
        }
        [TestCase(0)][TestCase(1)]
        public void GrippedHorizontalStickScalesFlatScreenWithoutMovingOrSeeking(int hand)
        {
            var view=app.Presenter.Settings;view.curvature=.4f;app.Current.formatLocked=true;
            Grip(hand,true);TickHand(hand,Vector2.right);Assert.That(view.width,Is.EqualTo(5));Assert.That(Saved().width,Is.EqualTo(4.5f));
            TickHand(hand,Vector2.left);Assert.That(view.width,Is.EqualTo(4.5f));
            TickHand(hand,Vector2.right,100);Assert.That(view.width,Is.EqualTo(10));TickHand(hand,Vector2.left,100);Assert.That(view.width,Is.EqualTo(1));
            Assert.That(view.distance,Is.EqualTo(3.5f));Assert.That(view.panoramaZoom,Is.EqualTo(1));Assert.That(view.curvature,Is.EqualTo(.4f));Assert.That(app.Current.formatLocked);Assert.That(backend.seeks,Is.Zero);
            TickHand(hand,Vector2.zero);Assert.That(Saved().width,Is.EqualTo(1));
        }
        [TestCase(0)][TestCase(1)]
        public void GrippedHorizontalStickScalesPanelAndPersistsWithoutAFilm(int hand)
        {
            Set("Current",null);ui.Show();var position=ui.Canvas.transform.position;var rotation=ui.Canvas.transform.rotation;
            Grip(hand,true);TickHand(hand,Vector2.right);Assert.That(store.Settings.mainPanelScale,Is.EqualTo(1.125f));Assert.That(new PlayerStore(path).Settings.mainPanelScale,Is.EqualTo(1));
            Assert.That(ui.Canvas.transform.localScale.x,Is.EqualTo(.00145f*1.125f).Within(.000001f));
            TickHand(hand,Vector2.left);Assert.That(store.Settings.mainPanelScale,Is.EqualTo(1));
            TickHand(hand,Vector2.right,100);Assert.That(store.Settings.mainPanelScale,Is.EqualTo(2));TickHand(hand,Vector2.left,100);Assert.That(store.Settings.mainPanelScale,Is.EqualTo(.5f));
            TickHand(hand,Vector2.zero);Assert.That(new PlayerStore(path).Settings.mainPanelScale,Is.EqualTo(.5f));
            ui.Hide();ui.Show();Assert.That(ui.Canvas.transform.localScale.x,Is.EqualTo(.00145f*.5f).Within(.000001f));
            Assert.That(store.Settings.mainPanelDistance,Is.EqualTo(2));Assert.That(ui.Canvas.transform.position,Is.EqualTo(position));Assert.That(ui.Canvas.transform.rotation,Is.EqualTo(rotation));Assert.That(backend.seeks,Is.Zero);
        }
        [TestCase(Projection.Equirect180)][TestCase(Projection.Equirect360)][TestCase(Projection.Fisheye)][TestCase(Projection.Cubemap3x2)][TestCase(Projection.Eac3x2)]
        public void GrippedPanoramaHorizontalInputDoesNotSeekOrScale(Projection projection)
        {
            app.Presenter.Settings.projection=projection;Grip(0,true);Tick(Vector2.right,100);
            Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));Assert.That(app.Presenter.Settings.width,Is.EqualTo(4.5f));Assert.That(backend.seeks,Is.Zero);
            Tick(Vector2.down);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1));
            Tick(Vector2.zero);Tick(Vector2.down);Assert.That(app.Presenter.Settings.panoramaZoom,Is.EqualTo(1.125f));
        }
        [TestCase(false)][TestCase(true)]
        public void ScalingUsesOneAxisUntilNeutralAndSavesOnGripRelease(bool panel)
        {
            if(panel)ui.Show();Grip(0,true);Tick(Vector2.right);Tick(Vector2.down);
            Assert.That(panel?store.Settings.mainPanelScale:app.Presenter.Settings.width,Is.EqualTo(panel?1.125f:5));
            Assert.That(panel?store.Settings.mainPanelDistance:app.Presenter.Settings.distance,Is.EqualTo(panel?2:3.5f));
            Grip(0,false);Tick(Vector2.right);Assert.That(backend.seeks,Is.Zero);
            Assert.That(panel?new PlayerStore(path).Settings.mainPanelScale:Saved().width,Is.EqualTo(panel?1.125f:5));
            Tick(Vector2.right);Assert.That(backend.seeks,Is.Zero);Tick(Vector2.zero);Tick(Vector2.right);Assert.That(backend.seeks,Is.EqualTo(1));
        }
        [TestCase(false)][TestCase(true)]
        public void TwoGrippedSticksDoNotDoubleScaling(bool panel)
        {
            if(panel)ui.Show();Grip(0,true);Grip(1,true);input.SetDiagnosticStick(1,Vector2.right);Tick(Vector2.right);
            Assert.That(panel?store.Settings.mainPanelScale:app.Presenter.Settings.width,Is.EqualTo(panel?1.125f:5));Assert.That(backend.seeks,Is.Zero);
        }
        [TestCase(1)][TestCase(2)][TestCase(3)]
        public void ExistingSettingsKeepPanelSizeAndPerFilmWidth(int version)
        {
            var restored=PlayerStore.ParseSettings("{\"version\":"+version+",\"history\":[{\"uri\":\"file:///old.mp4\",\"view\":{\"width\":7}}]}");
            Assert.That(restored.mainPanelScale,Is.EqualTo(1));Assert.That(restored.history[0].view.width,Is.EqualTo(7));Assert.That(restored.version,Is.EqualTo(3));
            restored.mainPanelScale=1.4f;Assert.That(PlayerStore.ParseSettings(JsonUtility.ToJson(restored)).mainPanelScale,Is.EqualTo(1.4f));
        }
        [Test] public void NoFilmOrFocusedTextPreventsDistanceAdjustment()
        {
            Grip(0,true);
            Tick(Vector2.down,typing:true);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));Tick(Vector2.zero);Set("Current",null);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
        }
        [Test] public void SimultaneousControllerBackPressesCloseOnlyOneLayer()
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");
            Assert.That(ControllerBack(true,true));Assert.That(ui.Quick.Popup,Is.Empty);Assert.That(ui.Quick.Visible);
            Assert.That(ControllerBack(true,true),Is.False);Assert.That(ui.Quick.Visible);
            Assert.That(ControllerBack(false,false),Is.False);
            Assert.That(ControllerBack(false,true));Assert.That(ui.AnyVisible,Is.False);
        }
        [Test] public void DiagnosticSimultaneousBackUsesThePhysicalGateBeforeStickInput()
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");input.SetDiagnosticStick(0,Vector2.right);input.SetDiagnosticBack(2,true,true);
            typeof(VrUiInput).GetMethod("ProcessDiagnosticInput",Private).Invoke(input,null);
            Assert.That(ui.Quick.Popup,Is.Empty);Assert.That(ui.Quick.Visible);Assert.That(backend.seeks,Is.Zero);
            Assert.That(ControllerBack(true,true),Is.False);Assert.That(ui.Quick.Visible);
            Assert.That(ControllerBack(false,false),Is.False);Assert.That(ControllerBack(false,true));Assert.That(ui.AnyVisible,Is.False);
        }
        [Test] public void DiagnosticInputResetClearsBackAndRequiresRelease()
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");input.SetDiagnosticBack(2,true,true);input.ResetDiagnosticInput();
            var pressed=(bool[])typeof(VrUiInput).GetField("diagnosticBackPressed",Private).GetValue(input);
            var valid=(bool[])typeof(VrUiInput).GetField("diagnosticBackValid",Private).GetValue(input);
            Assert.That(pressed,Is.All.False);Assert.That(valid,Is.All.False);
            Assert.That(ControllerBack(true,true),Is.False);Assert.That(ui.Quick.Popup,Is.EqualTo("mode"));
            Assert.That(ControllerBack(false,false),Is.False);Assert.That(ControllerBack(true,true));Assert.That(ui.Quick.Popup,Is.Empty);
        }
        [Test] public void DiagnosticBackCannotReturnWithoutApplicationFocus()
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");Set("HasFocus",false);input.SetDiagnosticBack(2,true,true);
            typeof(VrUiInput).GetMethod("ProcessDiagnosticInput",Private).Invoke(input,null);
            Assert.That(ui.Quick.Popup,Is.EqualTo("mode"));Set("HasFocus",true);
            Assert.That(ControllerBack(true,true),Is.False);Assert.That(ControllerBack(false,false),Is.False);
            Assert.That(ControllerBack(true,true));Assert.That(ui.Quick.Popup,Is.Empty);
        }
        [TestCase(0)][TestCase(1)]
        public void HeldControllerBackRequiresReleaseBeforeClosingTheNextLayer(int hand)
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");
            Assert.That(ControllerBack(hand,true));Assert.That(ui.Quick.Popup,Is.Empty);Assert.That(ui.Quick.Visible);
            for(int i=0;i<3;i++)Assert.That(ControllerBack(hand,true),Is.False);
            Assert.That(ui.Quick.Visible);Assert.That(ControllerBack(hand,false),Is.False);
            Assert.That(ControllerBack(hand,true));Assert.That(ui.AnyVisible,Is.False);
        }
        [TestCase("OnApplicationFocus",false,0)][TestCase("OnApplicationFocus",false,1)]
        [TestCase("OnApplicationPause",true,0)][TestCase("OnApplicationPause",true,1)]
        public void LifecycleRecoveryRequiresControllerBackRelease(string message,bool interrupted,int hand)
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");Set("HasFocus",false);
            typeof(VrUiInput).GetMethod(message,Private).Invoke(input,new object[]{interrupted});
            Set("HasFocus",true);typeof(VrUiInput).GetMethod(message,Private).Invoke(input,new object[]{!interrupted});
            Assert.That(ControllerBack(hand,true),Is.False);Assert.That(ui.Quick.Popup,Is.EqualTo("mode"));
            Assert.That(ControllerBack(hand,false),Is.False);Assert.That(ControllerBack(hand,true));
            Assert.That(ui.Quick.Popup,Is.Empty);Assert.That(ui.Quick.Visible);
        }
        [TestCase(0)][TestCase(1)]
        public void TrackingRecoveryRequiresControllerBackRelease(int hand)
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");
            Assert.That(ControllerBack(hand,true,false),Is.False);Assert.That(ControllerBack(hand,true),Is.False);
            Assert.That(ui.Quick.Popup,Is.EqualTo("mode"));Assert.That(ControllerBack(hand,false),Is.False);
            Assert.That(ControllerBack(hand,true));Assert.That(ui.Quick.Popup,Is.Empty);Assert.That(ui.Quick.Visible);
        }
        [Test] public void EscapeStillReturnsWhenTypingBlocksTransportShortcuts()
        {
            ui.ShowQuick();ui.Quick.TogglePopup("mode");
            var shortcut=typeof(VrUiInput).GetMethod("HandleKeyboardShortcut",Private);
            shortcut.Invoke(input,new object[]{UnityEngine.InputSystem.Key.RightArrow,true});Assert.That(backend.seeks,Is.Zero);
            shortcut.Invoke(input,new object[]{UnityEngine.InputSystem.Key.Escape,true});Assert.That(ui.Quick.Popup,Is.Empty);Assert.That(ui.Quick.Visible);
            Set("HasFocus",false);shortcut.Invoke(input,new object[]{UnityEngine.InputSystem.Key.Escape,true});Assert.That(ui.Quick.Visible);
        }
        [TestCase(false)][TestCase(true)]
        public void ControllerBackCancelsOwnedFullAndQuickSeeksWithoutCommitting(bool quick)
        {
            if(quick)ui.ShowQuick();else{ui.Show();ui.SelectTab("投影");}
            var fullSeek=quick?null:ui.Canvas.GetComponentInChildren<SeekGesture>(true);
            var quickSeek=quick?ui.Quick.Canvas.GetComponentInChildren<QuickSeekSurface>(true):null;
            var target=quick?quickSeek.gameObject:fullSeek.gameObject;
            var pointer=((PointerEventData[])typeof(VrUiInput).GetField("pointers",Private).GetValue(input))[0];
            pointer.pointerPress=pointer.pointerDrag=target;pointer.eligibleForClick=true;pointer.dragging=true;
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);
            Assert.That(quick?quickSeek.Dragging:fullSeek.Dragging);
            Assert.That(ControllerBack(true,false));Assert.That(ui.AnyVisible,Is.False);
            Assert.That(quick?quickSeek.Dragging:fullSeek.Dragging,Is.False);Assert.That(backend.seeks,Is.Zero);
            Assert.That(pointer.pointerPress,Is.Null);Assert.That(pointer.pointerDrag,Is.Null);Assert.That(pointer.dragging,Is.False);Assert.That(pointer.eligibleForClick,Is.False);
            if(quick)quickSeek.OnPointerUp(pointer);else fullSeek.OnPointerUp(pointer);
            Assert.That(backend.seeks,Is.Zero);
        }
        [TestCase(Projection.Flat)][TestCase(Projection.Equirect360)]
        public void ControllerBackPersistsDistanceGestureAndRequiresNeutralBeforeResuming(Projection projection)
        {
            Grip(0,true);
            app.Presenter.Settings.projection=projection;Tick(Vector2.down);
            float before=projection==Projection.Flat?app.Presenter.Settings.distance:app.Presenter.Settings.panoramaZoom;
            Assert.That(projection==Projection.Flat?Saved().distance:Saved().panoramaZoom,Is.EqualTo(projection==Projection.Flat?3.5f:1));
            Assert.That(ControllerBack(true,false));Assert.That(ui.AnyVisible,Is.False);
            Assert.That(projection==Projection.Flat?Saved().distance:Saved().panoramaZoom,Is.EqualTo(before));
            Tick(Vector2.down);Assert.That(projection==Projection.Flat?app.Presenter.Settings.distance:app.Presenter.Settings.panoramaZoom,Is.EqualTo(before));
            Tick(Vector2.zero);Tick(Vector2.down);
            Assert.That(projection==Projection.Flat?app.Presenter.Settings.distance:app.Presenter.Settings.panoramaZoom,Is.EqualTo(projection==Projection.Flat?2.5f:1.25f));
            Assert.That(app.Current.formatLocked,Is.False);
        }
        [Test] public void ClosingQuickPanelWithControllerBackRequiresNeutralBeforeHiddenDistanceAdjustment()
        {
            Grip(0,true);
            ui.ShowQuick();Tick(Vector2.down);Assert.That(ControllerBack(true,false));Assert.That(ui.AnyVisible,Is.False);
            Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3.5f));
            Tick(Vector2.zero);Tick(Vector2.down);Assert.That(app.Presenter.Settings.distance,Is.EqualTo(3));
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
