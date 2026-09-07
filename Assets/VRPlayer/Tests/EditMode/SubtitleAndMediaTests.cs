using System;
using System.IO;
using NUnit.Framework;

namespace VRPlayer.Tests
{
    public sealed class SubtitleAndMediaTests
    {
        [TestCase("电影.2024.srt")]
        [TestCase("电影.2024.chs.srt")]
        [TestCase("电影.2024.zh.srt")]
        [TestCase("电影.2024.zh-CN.srt")]
        [TestCase("电影.2024.zh-Hans.srt")]
        [TestCase("电影.2024.zho.srt")]
        [TestCase("电影.2024.chi.srt")]
        [TestCase("电影.2024.chs.eng.srt")]
        [TestCase("电影.2024.zh.forced.srt")]
        [TestCase("电影.2024 [chs].SRT")]
        [TestCase("电影.2024_zh.srt")]
        public void MatchesSameStemAndLanguageSuffix(string subtitle)=>Assert.That(SubtitleMatcher.Matches("电影.2024.mkv",subtitle),Is.True);
        [TestCase("电影.20240.srt")]
        [TestCase("电影.2024.续集.srt")]
        [TestCase("电影.2024other.zh.srt")]
        [TestCase("电影.2024.zh.ass")]
        public void RejectsOtherMoviesAndFormats(string subtitle)=>Assert.That(SubtitleMatcher.Matches("电影.2024.mkv",subtitle),Is.False);
        [Test] public void DoesNotConfuseEpisodes()=>Assert.That(SubtitleMatcher.Matches("节目.S01E01.mp4","节目.S01E02.zh.srt"),Is.False);
        [Test] public void ParsesBomCrlfMultilineWithoutFinalBlankLine()
        {
            var document=SrtDocument.Parse("\uFEFF1\r\n00:00:01,000 --> 00:00:03,500\r\n你好\r\nHello");
            Assert.That(document.Cues.Count,Is.EqualTo(1));Assert.That(document.TextAt(2),Is.EqualTo("你好\nHello"));Assert.That(document.TextAt(3.5),Is.Empty);
        }
        [Test] public void PositiveDelayMeansLaterDisplay()
        {
            var document=SrtDocument.Parse("1\n00:00:01,000 --> 00:00:02,000\nDelayed");
            Assert.That(document.TextAt(1.5,1),Is.Empty);Assert.That(document.TextAt(2.5,1),Is.EqualTo("Delayed"));Assert.That(document.TextAt(.5,-1),Is.EqualTo("Delayed"));
        }
        [Test] public void FindsOverlappingCuesAfterForwardAndBackwardSeek()
        {
            var document=SrtDocument.Parse("1\n00:00:01,000 --> 00:00:10,000\nLong\n\n2\n00:00:02,000 --> 00:00:03,000\nShort\n\n3\n00:00:04,000 --> 00:00:06,000\nMiddle");
            Assert.That(document.TextAt(5),Is.EqualTo("Long\nMiddle"));Assert.That(document.TextAt(2.5),Is.EqualTo("Long\nShort"));Assert.That(document.TextAt(8),Is.EqualTo("Long"));
        }
        [Test] public void SkipsMalformedBlocksAndKeepsLaterValidCue()
        {
            var document=SrtDocument.Parse("1\n00:00:09,000 --> 00:00:02,000\nInvalid\n\n2\n00:00:10,000 --> 00:00:11,000\nValid");Assert.That(document.InvalidBlocks,Is.EqualTo(1));Assert.That(document.TextAt(10),Is.EqualTo("Valid"));
        }
        [Test] public void SupportsLongHourAndDecimalTimestamp()=>Assert.That(SrtDocument.Parse("100:00:00.125 --> 100:00:01.125\nLong film").TextAt(360000.5),Is.EqualTo("Long film"));
        [Test] public void RejectsInvalidMinutes()=>Assert.That(SrtDocument.Parse("1\n00:75:00,000 --> 01:30:00,000\nBad").Cues.Count,Is.Zero);
        [Test] public void KeepsFilenameDotsAndDetectsIndependentProjectionAndStereo()
        {
            var view=ViewSettings.Detect("Trip.2024.180.SBS.mp4");Assert.That(view.projection,Is.EqualTo(Projection.Equirect180));Assert.That(view.stereo,Is.EqualTo(StereoLayout.SideBySide));Assert.That(ViewSettings.Detect("my360trip.mp4").projection,Is.EqualTo(Projection.Flat));
        }
        [Test] public void CorrectsFullAndHalfStereoAspect()
        {
            var view=new ViewSettings{stereo=StereoLayout.SideBySide,half=false};Assert.That(view.Aspect(3840,1080),Is.EqualTo(16f/9f).Within(.001));view.half=true;Assert.That(view.Aspect(1920,1080),Is.EqualTo(16f/9f).Within(.001));view.stereo=StereoLayout.TopBottom;view.half=false;Assert.That(view.Aspect(1920,2160),Is.EqualTo(16f/9f).Within(.001));
        }
        [Test] public void SmbParentPreservesServerAndEscapedSegments()=>Assert.That(FileLibrary.Parent("smb://test/Chinese%20Movies/film.mp4"),Is.EqualTo("smb://test/Chinese%20Movies/"));
        [Test] public void CorruptedHistoryDoesNotPreventStartup()
        {
            string root=Path.Combine(Path.GetTempPath(),"vrplayer-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);string path=Path.Combine(root,"settings.json");
            try{File.WriteAllText(path,"not json");var store=new PlayerStore(path);Assert.That(store.Settings.history,Is.Empty);store.Settings.volume=.42f;store.Save();Assert.That(new PlayerStore(path).Settings.volume,Is.EqualTo(.42f).Within(.001));}
            finally{Directory.Delete(root,true);}
        }
        [Test] public void ChangedMediaFileInvalidatesResumePosition()
        {
            string path=Path.Combine(Path.GetTempPath(),"vrplayer-"+Guid.NewGuid()+".json");var store=new PlayerStore(path);
            var record=store.Touch(new FileEntry{uri="file:///movie.mp4",name="movie.mp4",size=100,modified=1},"file:///");record.position=9000;
            Assert.That(store.Touch(new FileEntry{uri=record.uri,name=record.name,size=200,modified=2},"file:///").position,Is.Zero);
        }
    }
}
