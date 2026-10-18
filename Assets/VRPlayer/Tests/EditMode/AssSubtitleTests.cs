using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace VRPlayer.Tests
{
    public sealed class AssSubtitleTests
    {
        public const string Script="[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Noto Sans CJK SC,32,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,10,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{\\move(50,180,500,180)\\fad(200,200)}你好 ASS, with comma\n";

        [TestCase("film.ass")][TestCase("film.SSA")][TestCase("film.srt")]
        public void BrowsersRecognizeSupportedExternalSubtitles(string name)=>Assert.That(new FileEntry{name=name}.IsSubtitle,Is.True);
        [TestCase("film.ass.txt")][TestCase("film.vtt")][TestCase("film.ass/")]
        public void BrowsersRejectUnsupportedSubtitleNames(string name)=>Assert.That(new FileEntry{name=name}.IsSubtitle,Is.False);
        [Test] public void DirectoriesAreNeverSubtitles()=>Assert.That(new FileEntry{name="folder.ass",directory=true}.IsSubtitle,Is.False);
        [Test] public void MatcherKeepsChinesePreferenceAcrossFormats()
        {
            var files=new[]{new FileEntry{name="film.srt"},new FileEntry{name="film.zh.ass"},new FileEntry{name="film2.zh.ass"}};
            Assert.That(SubtitleMatcher.Find("film.mkv",files).Select(x=>x.name),Is.EqualTo(new[]{"film.zh.ass","film.srt"}));
        }
        [TestCase(Projection.Flat,true)][TestCase(Projection.Equirect180,false)][TestCase(Projection.Equirect360,false)]
        [TestCase(Projection.Fisheye,false)][TestCase(Projection.Cubemap3x2,false)][TestCase(Projection.Eac3x2,false)]
        public void AssDisplayIsRestrictedToFlatVideo(Projection projection,bool expected)=>Assert.That(AssPresentation.Supported(projection),Is.EqualTo(expected));
        [TestCase(.25f)][TestCase(1f)][TestCase(1.7777778f)][TestCase(8f)]
        public void RasterPreservesVideoAspectWithinUploadBudget(float aspect)
        {
            var size=AssPresentation.FrameSize(aspect);
            Assert.That(size.x,Is.LessThanOrEqualTo(1920));Assert.That(size.y,Is.LessThanOrEqualTo(1080));
            Assert.That(size.x/(float)size.y,Is.EqualTo(aspect).Within(.01));
        }
        [Test] public void PositiveAssDelayUsesEarlierScriptTime()
        {
            Assert.That(AssPresentation.Timestamp(2500,1),Is.EqualTo(1500));
            Assert.That(AssPresentation.Timestamp(500,-1),Is.EqualTo(1500));
            Assert.That(AssPresentation.Timestamp(500,1),Is.EqualTo(-1));
        }
        [Test] public void LibassParsesCompleteScriptAndRejectsSrtAsAss()
        {
            var document=AssDocument.Parse("\uFEFF"+Script);
            Assert.That(document.EventCount,Is.EqualTo(1));
            Assert.That(System.Text.Encoding.UTF8.GetString(document.Utf8),Does.Contain("\\move(50,180,500,180)"));
            Assert.That(AssDocument.Parse("1\n00:00:01,000 --> 00:00:02,000\nSRT"),Is.Null);
        }
        [Test] public async Task InvalidAssCandidateFallsBackToValidScript()
        {
            var result=await SubtitleLoader.Load(new[]{"bad.ass","valid.ass"},(uri,ct)=>Task.FromResult(new FileResult{ok=true,text=uri=="bad.ass"?"not an ass script":Script}),CancellationToken.None);
            Assert.That(result.uri,Is.EqualTo("valid.ass"));Assert.That(result.ass.EventCount,Is.EqualTo(1));Assert.That(result.document,Is.Null);
        }
        [Test] public void CancelledAssReadCannotCommitOrTryNextCandidate()
        {
            using var cancellation=new CancellationTokenSource();int reads=0;
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(),async()=>await SubtitleLoader.Load(new[]{"old.ass","next.ass"},(uri,ct)=>{reads++;cancellation.Cancel();return Task.FromResult(new FileResult{ok=true,text=Script});},cancellation.Token));
            Assert.That(reads,Is.EqualTo(1));
        }
        [TestCase("srt")][TestCase("ass")][TestCase("ssa")]
        public void EmbyIdentityRetainsSubtitleFormatWithoutCredentials(string format)
        {
            string value=EmbySubtitleIdentity.Create("account","movie","source",4,format);
            Assert.That(EmbySubtitleIdentity.TryParse(value,out _,out _,out _,out int index,out string parsed),Is.True);
            Assert.That(index,Is.EqualTo(4));Assert.That(parsed,Is.EqualTo(format));
            Assert.That(EmbySubtitleIdentity.TryParse(value+"?token=private",out _,out _,out _,out _),Is.False);
        }
    }
}
