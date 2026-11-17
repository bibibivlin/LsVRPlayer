using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class FlatSubtitleRenderingTests
    {
        private const int Width = 320, Height = 180;
        private const float Aspect = Width / (float)Height;
        private Camera head;
        private VideoPresenter video;
        private RenderTexture target;
        private Texture2D pixels;
        private FlatSubtitleSettings settings;

        [SetUp]
        public void Setup()
        {
            head = new GameObject("Subtitle render test camera").AddComponent<Camera>();
            head.enabled = false;
            head.stereoTargetEye = StereoTargetEyeMask.None;
            head.transform.position = new Vector3(0, 500, 0);
            head.orthographic = true;
            head.orthographicSize = .5f;
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = Color.black;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            head.targetTexture = target;
            pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
            video = new GameObject("Subtitle render test screen").AddComponent<VideoPresenter>();
            video.Initialize(head);
            video.Settings.width = Aspect;
            video.Settings.heightOffset = 0;
            settings = new FlatSubtitleSettings { size = 60, style = new SubtitleStyle { textColor = Color.white, outline = false } };
        }

        [TearDown]
        public void Cleanup()
        {
            head.targetTexture = null;
            Object.DestroyImmediate(video.gameObject);
            Object.DestroyImmediate(head.gameObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
        }

        private void Render(string text, Texture ass = null)
        {
            video.UpdateVideo(Texture2D.blackTexture, false, Width, Height, 1);
            video.UpdateSubtitles(text, ass, settings, Aspect);
            var previous = RenderTexture.active;
            try
            {
                head.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
            }
            finally { RenderTexture.active = previous; }
        }

        private RectInt SubtitlePixels()
        {
            int left = Width, right = -1, bottom = Height, top = -1;
            var image = pixels.GetPixels32();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (image[y * Width + x].r <= 64) continue;
                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x);
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y);
            }
            return right < left ? new RectInt() : new RectInt(left, bottom, right - left + 1, top - bottom + 1);
        }

        [Test]
        public void TextCaptionMovesAndResizesWithinTheVideoPicture()
        {
            Render("字幕\nCaption");
            var lower = SubtitlePixels();
            Assert.That(lower.height, Is.GreaterThan(5));
            Assert.That(lower.yMin, Is.InRange(1, Height / 5));

            settings.verticalPosition = 1;
            Render("字幕\nCaption");
            var upper = SubtitlePixels();
            Assert.That(upper.yMax, Is.InRange(Height * 4 / 5, Height - 1));
            Assert.That(upper.height, Is.EqualTo(lower.height).Within(2));

            settings.size = 100;
            Render("字幕\nCaption");
            var bigger = SubtitlePixels();
            Assert.That(bigger.width, Is.GreaterThan(upper.width));
            Assert.That(bigger.height, Is.GreaterThan(upper.height));
            Assert.That(bigger.yMax, Is.LessThan(Height));
        }

        [Test]
        public void TextCaptionFollowsTheCurvedScreenAndClearsWhenProjectionChanges()
        {
            video.Settings.curvature = 80;
            settings.verticalPosition = .5f;
            Render("Curved screen");
            var original = SubtitlePixels();
            Assert.That(original.width, Is.GreaterThan(10));

            video.Settings.heightOffset = .15f;
            video.Settings.distance = 4.5f;
            Render("Curved screen");
            Assert.That(SubtitlePixels().yMin - original.yMin, Is.EqualTo(.15f * Height).Within(2));

            video.Settings.projection = Projection.Equirect180;
            Render("Curved screen");
            Assert.That(SubtitlePixels().width, Is.Zero);
            video.Settings.projection = Projection.Flat;
            Render("Curved screen");
            Assert.That(SubtitlePixels().width, Is.GreaterThan(10));
            Render("");
            Assert.That(SubtitlePixels().width, Is.Zero);
        }

        [Test]
        public void TranslucentCaptionBackgroundKeepsItsOpacityWhenComposited()
        {
            settings.verticalPosition = .5f;
            settings.style.textColor = Color.clear;
            settings.style.background = SubtitleBackground.FullLine;
            settings.style.backgroundColor = Color.white;
            settings.style.backgroundOpacity = .4f;
            Render("Caption");
            Assert.That(pixels.GetPixel(Width / 4, Height / 2).r, Is.EqualTo(.4f).Within(.02f));
            Assert.That(pixels.GetPixel(Width / 4, Height / 4).r, Is.LessThan(.01f));
        }

        [Test]
        public void AssLayerScalesAndMovesInVideoCoordinates()
        {
            var script = new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            try
            {
                var colors = new Color32[64];
                colors[10] = colors[11] = new Color32(255, 255, 255, 255);
                script.SetPixels32(colors);
                script.Apply();
                Render("", script);
                var original = SubtitlePixels();
                Assert.That(original.yMin, Is.EqualTo(Height / 8).Within(1));

                settings.assScale = 1.5f;
                settings.assOffset = .2f;
                Render("", script);
                var adjusted = SubtitlePixels();
                Assert.That(adjusted.width, Is.EqualTo(original.width * 1.5f).Within(2));
                Assert.That(adjusted.yMin, Is.EqualTo((.125f * 1.5f + .2f) * Height).Within(1));
            }
            finally { Object.DestroyImmediate(script); }
        }
    }
}
