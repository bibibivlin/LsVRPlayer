using System;
using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRPlayer.Tests
{
    public sealed class CinemaComfortSettingsTests
    {
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)]
        public void OldSettingsKeepOriginalRenderingAndMigrations(int version)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":"+version+",\"history\":[{\"uri\":\"file:///movie.mp4\",\"subtitle\":\"file:///movie.srt\",\"position\":42000,\"view\":{\"brightness\":0.8}}]}");
            Assert.That(settings.cinemaComfort.highlights,Is.EqualTo(HighlightCompression.Off));
            Assert.That(settings.cinemaComfort.subtitleBrightness,Is.EqualTo(1));
            Assert.That(settings.version,Is.EqualTo(3));Assert.That(settings.history[0].position,Is.EqualTo(42000));
            Assert.That(settings.history[0].view.brightness,Is.EqualTo(.8f));
            if(version<3)Assert.That(settings.history[0].subtitleSelection,Is.EqualTo(SubtitleSelection.Manual));
        }

        [Test] public void MissingNullInvalidAndPartialComfortSettingsAreSafe()
        {
            foreach(string json in new[]{"{}","{\"cinemaComfort\":null}","{\"cinemaComfort\":{\"highlights\":2}}"})
                Assert.That(PlayerStore.ParseSettings(json).cinemaComfort.subtitleBrightness,Is.EqualTo(1));
            var settings=PlayerStore.ParseSettings("{\"cinemaComfort\":{\"highlights\":999,\"subtitleBrightness\":-1}}").cinemaComfort;
            Assert.That(settings.highlights,Is.EqualTo(HighlightCompression.Off));Assert.That(settings.subtitleBrightness,Is.EqualTo(.3f));
            foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {
                settings.subtitleBrightness=value;settings.Normalize();Assert.That(settings.subtitleBrightness,Is.EqualTo(1));
            }
        }

        [Test] public void ComfortAndMatteWallSurviveRestartWithoutChangingFilmState()
        {
            string path=Path.Combine(Path.GetTempPath(),"VRPlayer-comfort-"+Guid.NewGuid()+".json");
            try
            {
                var store=new PlayerStore(path);store.Settings.cinemaComfort.highlights=HighlightCompression.Medium;store.Settings.cinemaComfort.subtitleBrightness=.65f;
                store.Settings.background.mode=CinemaBackgroundMode.MatteWall;
                var film=store.Touch(new FileEntry{uri="file:///first.mp4"},"");film.view.brightness=1.2f;film.formatLocked=true;film.subtitleSelection=SubtitleSelection.Off;
                store.Touch(new FileEntry{uri="file:///second.mp4"},"").view.brightness=.8f;store.Save();
                var loaded=new PlayerStore(path);
                Assert.That(loaded.Settings.cinemaComfort.highlights,Is.EqualTo(HighlightCompression.Medium));
                Assert.That(loaded.Settings.cinemaComfort.subtitleBrightness,Is.EqualTo(.65f));
                Assert.That(loaded.Settings.background.mode,Is.EqualTo(CinemaBackgroundMode.MatteWall));
                Assert.That(loaded.Find(film.uri).view.brightness,Is.EqualTo(1.2f));Assert.That(loaded.Find(film.uri).formatLocked,Is.True);
                Assert.That(loaded.Find(film.uri).subtitleSelection,Is.EqualTo(SubtitleSelection.Off));
            }
            finally { if(File.Exists(path))File.Delete(path); }
        }
    }

    public sealed class CinemaComfortRenderingTests
    {
        private Material video,subtitle;
        private Texture2D input,overlay,readback;
        private RenderTexture target;
        private const int Width=256;

        [SetUp] public void Setup()
        {
            video=new Material(Shader.Find("VRPlayer/VideoProjection"));video.SetFloat("_GammaInput",0);
            subtitle=new Material(Shader.Find("VRPlayer/Subtitle SDF"));
            input=new Texture2D(Width,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            overlay=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            readback=new Texture2D(Width,1,TextureFormat.RGBAFloat,false,true);
            target=new RenderTexture(Width,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
        }
        [TearDown] public void Cleanup()
        {
            target.Release();foreach(var item in new Object[]{video,subtitle,input,overlay,readback,target})Object.DestroyImmediate(item);
        }
        private Color[] Render(Material material,Func<int,Color> color)
        {
            var pixels=new Color[Width];for(int i=0;i<Width;i++)pixels[i]=color(i);
            input.SetPixels(pixels);input.Apply();var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=target;GL.Clear(true,true,Color.clear);Graphics.Blit(input,target,material);
                RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,Width,1),0,0);readback.Apply();return readback.GetPixels();
            }
            finally { RenderTexture.active=previous; }
        }
        private void Mode(HighlightCompression mode)=>video.SetVector("_ComfortHighlights",CinemaComfortSettings.HighlightParameters(mode));

        [TestCase(.3f)][TestCase(1f)][TestCase(1.8f)]
        public void OffPreservesExistingVideoGain(float gain)
        {
            Mode(HighlightCompression.Off);video.SetFloat("_Brightness",gain);
            var output=Render(video,i=>new Color(i/255f,.2f,.05f,1));
            for(int i=0;i<Width;i++)
            {
                Assert.That(output[i].r,Is.EqualTo(i/255f*gain).Within(.003f));
                Assert.That(output[i].g,Is.EqualTo(.2f*gain).Within(.003f));
            }
        }

        [TestCase(HighlightCompression.Gentle,.85f)][TestCase(HighlightCompression.Medium,.75f)][TestCase(HighlightCompression.Strong,.65f)]
        public void ShoulderPreservesDarkTonesAndMapsReferenceWhite(HighlightCompression mode,float expectedWhite)
        {
            Mode(mode);var output=Render(video,i=>new Color(i/255f,i/255f,i/255f,1));
            Assert.That(output[0].r,Is.Zero);Assert.That(output[255].r,Is.EqualTo(expectedWhite).Within(.003f));
            for(int i=1;i<Width;i++)
            {
                Assert.That(output[i].r,Is.GreaterThan(output[i-1].r));
                Assert.That(output[i].r,Is.LessThanOrEqualTo(i/255f+.002f));
                if(i/255f<=.35f)Assert.That(output[i].r,Is.EqualTo(i/255f).Within(.002f));
            }
        }

        [TestCase(HighlightCompression.Gentle)][TestCase(HighlightCompression.Medium)][TestCase(HighlightCompression.Strong)]
        public void BrightnessAboveOneStaysSmoothAndPreservesRgbRatios(HighlightCompression mode)
        {
            Mode(mode);video.SetFloat("_Brightness",1.8f);
            var output=Render(video,i=>new Color(i/255f,i/510f,i/1020f,1));
            for(int i=1;i<Width;i++)
            {
                Assert.That(output[i].r,Is.GreaterThan(output[i-1].r));Assert.That(output[i].r,Is.LessThan(1));
                Assert.That(output[i].g,Is.EqualTo(output[i].r*.5f).Within(.002f));Assert.That(output[i].b,Is.EqualTo(output[i].r*.25f).Within(.002f));
            }
            float before=output[141].r-output[140].r,after=output[143].r-output[142].r;
            Assert.That(after,Is.EqualTo(before).Within(.0003f));
        }

        [Test] public void AssBrightnessIsIndependentOfFilmGainAndPreservesCoverage()
        {
            overlay.SetPixel(0,0,new Color(.5f,.5f,.5f,.5f));overlay.Apply();
            video.SetTexture("_AssTex",overlay);video.SetFloat("_AssEnabled",1);video.SetFloat("_SubtitleBrightness",.6f);
            video.SetFloat("_Brightness",.5f);Mode(HighlightCompression.Strong);
            var output=Render(video,i=>new Color(.2f,.2f,.2f,1));
            // Half-covered white at 60% + half of the .1 film pixel: .30 + .05.
            Assert.That(output[128].r,Is.EqualTo(.35f).Within(.004f));
            video.SetFloat("_SubtitleBrightness",1);
            Assert.That(Render(video,i=>new Color(.2f,.2f,.2f,1))[128].r,Is.EqualTo(.55f).Within(.004f));
        }

        [Test] public void SdfSubtitleBrightnessScalesColorWithoutChangingAlpha()
        {
            subtitle.SetColor("_FaceColor",new Color(.8f,.5f,.2f,.75f));
            var original=Render(subtitle,i=>Color.white)[128];
            subtitle.SetFloat("_SubtitleBrightness",.6f);var dimmed=Render(subtitle,i=>Color.white)[128];
            Assert.That(original.a,Is.GreaterThan(.7f));Assert.That(dimmed.a,Is.EqualTo(original.a).Within(.001f));
            Assert.That(dimmed.r,Is.EqualTo(original.r*.6f).Within(.003f));Assert.That(dimmed.g,Is.EqualTo(original.g*.6f).Within(.003f));
        }

        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
        public void PanoramaPixelsBypassHighlightCompression(int projection)
        {
            video.SetInt("_Projection",projection);Mode(HighlightCompression.Medium);
            var compressed=Render(video,i=>Color.white);Mode(HighlightCompression.Off);var original=Render(video,i=>Color.white);
            for(int i=0;i<Width;i++)Assert.That(compressed[i].r,Is.EqualTo(original[i].r).Within(.001f));
        }

        [Test] public void CaptionBrightnessChangesWithoutNewTextAndDoesNotTintMenuFont()
        {
            var owner=new GameObject("Comfort caption fixture");var caption=owner.AddComponent<CaptionPresenter>();
            caption.Initialize();var style=new SubtitleStyle();
            try
            {
                caption.SetText("Subtitle 字幕",42,style);
                var label=owner.GetComponentInChildren<TextMeshProUGUI>();var shared=UiFactory.Font.material;
                Color original=label.color;caption.SetText("Subtitle 字幕",42,style,.6f);
                Assert.That(label.color,Is.EqualTo(original));Assert.That(label.fontSharedMaterial,Is.Not.SameAs(shared));
                Assert.That(label.fontSharedMaterial.GetFloat("_SubtitleBrightness"),Is.EqualTo(.6f));
                if(shared.HasProperty("_SubtitleBrightness"))Assert.That(shared.GetFloat("_SubtitleBrightness"),Is.EqualTo(1));
            }
            finally
            {
                var materials=owner.GetComponentInChildren<TextMeshProUGUI>().fontSharedMaterials;
                Object.DestroyImmediate(owner);foreach(var material in materials)if(material!=null&&material!=UiFactory.Font.material)Object.DestroyImmediate(material);
            }
        }
    }
}
