using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRPlayer.Tests
{
    public sealed class CinemaBackgroundSettingsTests
    {
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)]
        public void ExistingSettingsKeepBlackBackgroundAndMigrations(int version)
        {
            var settings = PlayerStore.ParseSettings("{\"version\":"+version+",\"favorites\":[{\"uri\":\"smb://stable/Films\"}],\"history\":[{\"uri\":\"file:///movie.mp4\",\"subtitle\":\"file:///movie.srt\",\"position\":42000}]}");
            Assert.That(settings.background.mode,Is.EqualTo(CinemaBackgroundMode.Black));
            Assert.That(settings.background.grayLevel,Is.EqualTo(16));
            Assert.That(settings.version,Is.EqualTo(3));
            Assert.That(settings.history[0].position,Is.EqualTo(42000));
            Assert.That(settings.favorites[0].uri,Is.EqualTo("smb://stable/Films"));
            if (version < 3) Assert.That(settings.history[0].subtitleSelection,Is.EqualTo(SubtitleSelection.Manual));
        }
        [Test] public void MissingNullAndInvalidBackgroundSettingsAreSafe()
        {
            Assert.That(PlayerStore.ParseSettings("{\"version\":3,\"background\":null}").background,Is.Not.Null);
            var settings = PlayerStore.ParseSettings("{\"version\":3,\"background\":{\"mode\":999,\"grayLevel\":999,\"featherDegrees\":-1}}").background;
            Assert.That(settings.mode,Is.EqualTo(CinemaBackgroundMode.Black));
            Assert.That(settings.grayLevel,Is.EqualTo(48));Assert.That(settings.featherDegrees,Is.EqualTo(5));
            settings.grayLevel=float.NaN;settings.featherDegrees=float.PositiveInfinity;settings.Normalize();
            Assert.That(settings.grayLevel,Is.EqualTo(16));Assert.That(settings.featherDegrees,Is.EqualTo(12));
        }
        [TestCase(CinemaBackgroundMode.Black)][TestCase(CinemaBackgroundMode.NeutralGray)]
        [TestCase(CinemaBackgroundMode.SoftSurround)][TestCase(CinemaBackgroundMode.QuietTheater)][TestCase(CinemaBackgroundMode.MatteWall)]
        public void GlobalBackgroundSurvivesRestartAndFilmChanges(CinemaBackgroundMode mode)
        {
            string path=Path.Combine(Path.GetTempPath(),"VRPlayer-background-"+Guid.NewGuid()+".json");
            try
            {
                var store=new PlayerStore(path);store.Settings.background=new CinemaBackgroundSettings{mode=mode,grayLevel=24,featherDegrees=18};
                store.Touch(new FileEntry{uri="file:///first.mp4"},"").view.distance=4;
                store.Touch(new FileEntry{uri="file:///second.mp4"},"").view.panoramaZoom=1.4f;store.Save();
                var loaded=new PlayerStore(path);
                Assert.That(loaded.Settings.background.mode,Is.EqualTo(mode));Assert.That(loaded.Settings.background.grayLevel,Is.EqualTo(24));Assert.That(loaded.Settings.background.featherDegrees,Is.EqualTo(18));
                Assert.That(loaded.Find("file:///first.mp4").view.distance,Is.EqualTo(4));Assert.That(loaded.Find("file:///second.mp4").view.panoramaZoom,Is.EqualTo(1.4f));
            }
            finally { if(File.Exists(path))File.Delete(path); }
        }
    }

    public sealed class CinemaBackgroundRenderingTests
    {
        private GameObject root,head;
        private CinemaBackground background;
        private Camera camera;
        private RenderTexture target;
        private Texture2D pixels;
        private readonly ViewSettings view=new ViewSettings();
        [SetUp] public void Setup()
        {
            head=new GameObject("Cinema test camera",typeof(Camera));camera=head.GetComponent<Camera>();camera.enabled=false;
            camera.cullingMask=1<<31;camera.fieldOfView=90;camera.aspect=1;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.Depth;
            root=new GameObject("Cinema test background");root.layer=31;background=root.AddComponent<CinemaBackground>();background.Initialize(camera);
            target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();camera.targetTexture=target;
            pixels=new Texture2D(128,128,TextureFormat.RGB24,false);view.projection=Projection.Flat;view.width=4.5f;view.distance=3.5f;view.heightOffset=0;view.curvature=0;
        }
        private Color Render(int x=64,int y=64)
        {
            camera.Render();var previous=RenderTexture.active;
            try { RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();return pixels.GetPixel(x,y); }
            finally { RenderTexture.active=previous; }
        }
        private void Apply(CinemaBackgroundMode mode,float gray=32) => background.Apply(new CinemaBackgroundSettings{mode=mode,grayLevel=gray},view,16f/9);
        [TearDown] public void Cleanup()
        {
            camera.targetTexture=null;target.Release();
            foreach(var item in new Object[]{root,head,target,pixels})Object.DestroyImmediate(item);
        }
        [Test] public void BlackIsExactlyBlackAndNeutralGrayMatchesSrgbCode()
        {
            Apply(CinemaBackgroundMode.Black);Assert.That(Render().maxColorComponent,Is.LessThan(.001f));Assert.That(background.Visible,Is.False);
            foreach(float gray in new[]{8f,16,24,32,48})
            {
                Apply(CinemaBackgroundMode.NeutralGray,gray);
                Assert.That(Render().r,Is.EqualTo(gray/255f).Within(2/255f));
                Assert.That(Render(4,4).r,Is.EqualTo(gray/255f).Within(2/255f));
            }
        }
        [TestCase(Projection.Equirect180)][TestCase(Projection.Equirect360)][TestCase(Projection.Fisheye)]
        [TestCase(Projection.Cubemap3x2)][TestCase(Projection.Eac3x2)]
        public void PanoramasDisableBackgroundAndFlatRestoresIt(Projection projection)
        {
            Apply(CinemaBackgroundMode.QuietTheater);Assert.That(background.Visible,Is.True);
            view.projection=projection;Apply(CinemaBackgroundMode.QuietTheater);Assert.That(background.Visible,Is.False);Assert.That(Render().r,Is.Zero);
            view.projection=Projection.Flat;Apply(CinemaBackgroundMode.QuietTheater);Assert.That(background.Visible,Is.True);Assert.That(Render().r,Is.GreaterThan(.01f));
        }
        [Test] public void SurroundFallsOffOutsideScreenAndRespondsToScreenDistance()
        {
            Apply(CinemaBackgroundMode.SoftSurround);Assert.That(Render().r,Is.GreaterThan(.1f));
            Assert.That(Render(127,127).r,Is.LessThan(.01f));float close=Render(110,64).r;
            view.distance=6;Apply(CinemaBackgroundMode.SoftSurround);Assert.That(Render(110,64).r,Is.LessThan(close-.01f));
        }
        [Test] public void HeadRotationDoesNotRecenterSurround()
        {
            Apply(CinemaBackgroundMode.SoftSurround);Assert.That(Render().r,Is.GreaterThan(.1f));
            camera.transform.rotation=Quaternion.Euler(0,90,0);Apply(CinemaBackgroundMode.SoftSurround);Assert.That(Render().r,Is.LessThan(.001f));
            camera.transform.rotation=Quaternion.identity;Apply(CinemaBackgroundMode.SoftSurround);Assert.That(Render().r,Is.GreaterThan(.1f));
        }
        [Test] public void MatteWallHasNoScreenHaloOrRepeatingPanels()
        {
            Apply(CinemaBackgroundMode.MatteWall,48);float center=Render().r,side=Render(110,64).r;
            Assert.That(center,Is.EqualTo(side).Within(2/255f));
            view.width=8;view.distance=1;view.heightOffset=2;
            background.Apply(new CinemaBackgroundSettings{mode=CinemaBackgroundMode.MatteWall,grayLevel=48,featherDegrees=25},view,16f/9);
            Assert.That(Render().r,Is.EqualTo(center).Within(1/255f));
            Assert.That(Render(110,64).r,Is.EqualTo(side).Within(1/255f));
            Assert.That(Render(64,0).r,Is.LessThan(center));
        }
        [Test] public void BackgroundNeverCoversVideoAndReleaseRestoresCamera()
        {
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.layer=31;quad.transform.position=Vector3.forward*3;quad.transform.localScale=Vector3.one*2;
            var material=new Material(Shader.Find("VRPlayer/VideoProjection"));material.mainTexture=Texture2D.whiteTexture;material.SetFloat("_GammaInput",0);quad.GetComponent<Renderer>().sharedMaterial=material;
            try { foreach(CinemaBackgroundMode mode in Enum.GetValues(typeof(CinemaBackgroundMode))) { Apply(mode);Assert.That(Render().r,Is.GreaterThan(.98f)); } }
            finally { Object.DestroyImmediate(quad);Object.DestroyImmediate(material); }
            Object.DestroyImmediate(root);Assert.That(camera.clearFlags,Is.EqualTo(CameraClearFlags.Depth));
        }
        [Test] public void CurvedAndTallScreensRemainInsideSurroundBounds()
        {
            var flat=CinemaBackground.ScreenBounds(view,16f/9);view.width=8;view.curvature=80;view.heightOffset=1;
            var curved=CinemaBackground.ScreenBounds(view,1);
            Assert.That(curved.x,Is.GreaterThan(flat.x));Assert.That(curved.y,Is.GreaterThan(flat.y));Assert.That(curved.z,Is.GreaterThan(0));
        }
    }
}
