using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class PlaybackImprovementsTests
    {
        [Test] public void ChineseThenBareThenOtherLanguageIsStable()
        {
            var names=new[]{"zh电影.en.srt","zh电影.srt","zh电影.cht.srt","zh电影.zh-Hant.en.srt","zh电影.chs.srt","zh电影.续集.srt"};
            Assert.That(SubtitleMatcher.Find("zh电影.mkv",names.Select(name=>new FileEntry{name=name,uri=name})).Select(x=>x.name),Is.EqualTo(new[]{"zh电影.chs.srt","zh电影.cht.srt","zh电影.zh-Hant.en.srt","zh电影.srt","zh电影.en.srt"}));
        }
        [TestCase("cht")][TestCase("zh-TW")][TestCase("zh-Hant")][TestCase("chi")][TestCase("zho")][TestCase("chs.eng")]
        public void TraditionalAndBilingualSuffixesAreChinese(string suffix)=>Assert.That(SubtitleMatcher.Priority("film.mp4","film."+suffix+".srt"),Is.Zero);
        [TestCase("简体中文")][TestCase("繁體中文")][TestCase("中英双语")][TestCase("chs&eng")][TestCase("zhCN")]
        public void RecognizedChineseLabelsMatchAndSortFirst(string suffix){Assert.That(SubtitleMatcher.Matches("film.mp4","film."+suffix+".srt"));Assert.That(SubtitleMatcher.Priority("film.mp4","film."+suffix+".srt"),Is.Zero);}
        [Test] public void ChineseCharactersInMovieNameDoNotChangeOtherLanguageRank()=>Assert.That(SubtitleMatcher.Priority("zh.chs.mp4","zh.chs.en.srt"),Is.EqualTo(2));
        [Test] public async Task BrokenAutomaticSubtitleFallsBackToNextCandidate()
        {
            var result=await SubtitleLoader.Load(new[]{"broken","valid"},(uri,token)=>Task.FromResult(new FileResult{ok=true,text=uri=="broken"?"no timestamps":"1\n00:00:01,000 --> 00:00:03,000\n字幕"}),CancellationToken.None);
            Assert.That(result.uri,Is.EqualTo("valid"));Assert.That(result.document.Cues.Count,Is.EqualTo(1));
        }
        [Test] public void CancelledReadCannotLoadOrTryAnotherSubtitle()
        {
            using var cancellation=new CancellationTokenSource();int reads=0;
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(),async()=>await SubtitleLoader.Load(new[]{"old","next"},(uri,token)=>{reads++;cancellation.Cancel();return Task.FromResult(new FileResult{ok=true,text="1\n00:00:01,000 --> 00:00:03,000\nOld"});},cancellation.Token));Assert.That(reads,Is.EqualTo(1));
        }
        [Test] public void VersionTwoHistoryKeepsManualChoicesAndViews()
        {
            var settings=PlayerStore.ParseSettings("{\"version\":2,\"history\":[{\"uri\":\"smb://id/a.mp4\",\"subtitle\":\"track:1:0\",\"view\":{\"projection\":2}},{\"uri\":\"file:///b.mp4\",\"subtitle\":\"\"}]}");
            Assert.That(settings.version,Is.EqualTo(3));Assert.That(settings.history[0].subtitleSelection,Is.EqualTo(SubtitleSelection.Manual));Assert.That(settings.history.All(x=>x.formatLocked));Assert.That(settings.history[1].subtitleSelection,Is.EqualTo(SubtitleSelection.Auto));Assert.That(settings.history[0].view.projection,Is.EqualTo(Projection.Equirect360));Assert.That(settings.autoLoadSubtitles,Is.False);
            settings.history[1].subtitleSelection=SubtitleSelection.Off;settings.autoLoadSubtitles=true;
            var again=PlayerStore.ParseSettings(JsonUtility.ToJson(settings));Assert.That(again.autoLoadSubtitles);Assert.That(again.history[1].subtitleSelection,Is.EqualTo(SubtitleSelection.Off));
        }
        [Test] public void NewRecordsUseAutomaticSelectionWithoutLockingFormat()
        {
            var store=new PlayerStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));var record=store.Touch(new FileEntry{uri="file:///film.mp4",name="film.mp4"},"file:///");Assert.That(record.formatLocked,Is.False);Assert.That(record.subtitleSelection,Is.EqualTo(SubtitleSelection.Auto));
        }
        [TestCase("smb://stable-id/中文%20目录/a.mp4","smb://stable-id/%E4%B8%AD%E6%96%87%20%E7%9B%AE%E5%BD%95/")]
        [TestCase("content://provider/document/id",null)]
        public void MissingHistoryDirectoryUsesOnlyDerivableParent(string uri,string expected)=>Assert.That(PlayerApp.ContainingDirectory(new PlaybackRecord{uri=uri}),Is.EqualTo(expected));
        [Test] public void SafHistoryUsesSavedDirectory()=>Assert.That(PlayerApp.ContainingDirectory(new PlaybackRecord{uri="content://p/document/child",directory="content://p/tree/root"}),Is.EqualTo("content://p/tree/root"));
        [Test] public void StickImmediatelySeeksThenRepeatsAfterDelay()
        {
            var gesture=new StickTransportGesture();int Tick(float time,Vector2 left)=>gesture.Update(left,true,Vector2.zero,true,false,time);
            Assert.That(Tick(0,Vector2.right),Is.EqualTo(10));Assert.That(Tick(.49f,Vector2.right),Is.Zero);Assert.That(Tick(.5f,Vector2.right),Is.EqualTo(10));Assert.That(Tick(.79f,Vector2.right),Is.Zero);Assert.That(Tick(.81f,Vector2.right),Is.EqualTo(10));Assert.That(Tick(.9f,Vector2.zero),Is.Zero);Assert.That(Tick(1,Vector2.left),Is.EqualTo(-10));
        }
        [Test] public void VerticalGestureCannotTurnIntoASeekUntilNeutral()
        {
            var gesture=new StickTransportGesture();Assert.That(gesture.Update(new Vector2(.3f,.8f),true,Vector2.zero,true,false,0),Is.Zero);Assert.That(gesture.Scroll,Is.GreaterThan(0));Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,1),Is.Zero);
            gesture.Update(Vector2.zero,true,Vector2.zero,true,false,2);Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,3),Is.EqualTo(10));
        }
        [Test] public void ModalOrDisconnectedControllerRequiresRecentering()
        {
            var gesture=new StickTransportGesture();gesture.Update(Vector2.right,true,Vector2.zero,true,false,0);
            Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,true,1),Is.Zero);Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,2),Is.Zero);
            gesture.Update(Vector2.zero,true,Vector2.zero,true,false,3);Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,4),Is.EqualTo(10));
            Assert.That(gesture.Update(Vector2.right,false,Vector2.zero,true,false,5),Is.Zero);Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,6),Is.Zero);
        }
        [Test] public void TwoControllersCannotDoubleSeek()=>Assert.That(new StickTransportGesture().Update(Vector2.right,true,Vector2.right,true,false,0),Is.EqualTo(10));
        [Test] public void RapidSeeksAccumulateBeforeSnapshotCatchesUp()
        {
            var seek=new RelativeSeekTarget();Assert.That(seek.Relative(10000,10000,100000,0),Is.EqualTo(20000));Assert.That(seek.Relative(10000,10000,100000,.1f),Is.EqualTo(30000));Assert.That(seek.Relative(30020,-10000,100000,.2f),Is.EqualTo(20020));Assert.That(seek.Absolute(-1,100000,1),Is.Zero);Assert.That(seek.Absolute(200000,100000,2),Is.EqualTo(99999));
        }
        [TestCase("film.180.SBS.mp4",1,1)][TestCase("film.360.OU.mp4",2,2)][TestCase("film.Fisheye.mp4",3,-1)][TestCase("film.EAC.mp4",5,-1)][TestCase("film.Cubemap3x2.mp4",4,-1)][TestCase("film.3D.VR.8K.mp4",-1,-1)][TestCase("my360trip.mp4",-1,-1)]
        public void FilenameHintsKeepProjectionSeparate(string name,int projection,int stereo){var hints=FormatDetector.Filename(name);Assert.That(hints.projection,Is.EqualTo(projection));Assert.That(hints.stereo,Is.EqualTo(stereo));}
        [Test] public void MetadataFillsFieldsWithoutLosingIndependentFilenameHints()
        {
            var result=FormatDetector.Resolve(FormatDetector.Filename("movie.180.SBS.mp4"),new MediaFormatHints{source=FormatEvidence.Metadata,projection=2});Assert.That(result.selected.projection,Is.EqualTo(2));Assert.That(result.selected.stereo,Is.EqualTo(1));
        }
        [Test] public void UnsupportedMetadataBlocksLowerPriorityProjectionGuess(){var result=FormatDetector.Resolve(FormatDetector.Filename("movie.180.SBS.mp4"),new MediaFormatHints{source=FormatEvidence.Metadata,blockedFields=1});Assert.That(result.selected.projection,Is.EqualTo(-1));Assert.That(result.selected.stereo,Is.EqualTo(1));}
        [Test] public void ConflictingFilenameTagsRemainUnknown(){var hints=FormatDetector.Filename("film.180.360.SBS.OU.mp4");Assert.That(hints.blockedFields,Is.EqualTo(3));Assert.That(FormatDetector.Resolve(hints,new MediaFormatHints{source=FormatEvidence.Frames,projection=1,stereo=1}).selected.projection,Is.EqualTo(-1));}
        [Test] public void ApplyingAutomaticFormatPreservesViewingSettings(){var view=new ViewSettings{distance=5,width=7,brightness=1.3f,monoEye=true};FormatDetector.Resolve(FormatDetector.Filename("film.180.SBS.mp4")).Apply(view);Assert.That(view.distance,Is.EqualTo(5));Assert.That(view.width,Is.EqualTo(7));Assert.That(view.brightness,Is.EqualTo(1.3f));Assert.That(view.monoEye);}
        [Test] public void CachedDetectionRequiresFileIdentityAndAlgorithm(){var file=new FileEntry{uri="smb://stable/a.mp4",size=100,modified=10,metadataVerified=true};var cache=new FormatDetectionCache{uri=file.uri,size=100,modified=10,algorithm=FormatDetector.AlgorithmVersion};Assert.That(cache.Matches(file));file.modified=11;Assert.That(cache.Matches(file),Is.False);file.modified=0;Assert.That(cache.Matches(file),Is.False);}
        [Test] public void HistoryMetadataCannotValidateAChangedFileCache()
        {
            var file=new FileEntry{uri="smb://stable/a.mp4",size=100,modified=10};var cache=new FormatDetectionCache{uri=file.uri,size=100,modified=10,algorithm=FormatDetector.AlgorithmVersion};
            Assert.That(cache.Matches(file),Is.False);file.metadataVerified=true;Assert.That(cache.Matches(file));
            var restored=JsonUtility.FromJson<FileEntry>(JsonUtility.ToJson(file));Assert.That(restored.metadataVerified,Is.False);
            Assert.That(cache.Matches(restored),Is.False);cache.algorithm++;Assert.That(cache.Matches(file),Is.False);
        }
        [Test] public void FrameApplicationRequiresThreeConsistentFramesAndMargin(){var hints=new MediaFormatHints{source=FormatEvidence.Frames,stereo=1,score=.95f,margin=.2f,consistentFrames=2};Assert.That(FormatDetector.MeetsFrameThreshold(hints),Is.False);hints.consistentFrames=3;Assert.That(FormatDetector.MeetsFrameThreshold(hints));hints.margin=.1f;Assert.That(FormatDetector.MeetsFrameThreshold(hints),Is.False);}
        [Test] public void GeometryAloneNeverChoosesVr(){var hints=FormatDetector.Packing(3840,1920,-1,-1);Assert.That(hints.HasValues,Is.False);}
        [Test] public void FullStereoPackingCorrectsCinemaAspect(){Assert.That(FormatDetector.Packing(3840,1080,1,-1).half,Is.Zero);Assert.That(FormatDetector.Packing(1920,2160,2,-1).half,Is.Zero);}
        [Test] public void ConflictsRetainCandidatesForExplanationAndReview()
        {
            var hints=FormatDetector.Filename("film.180.360.SBS.OU.mp4");
            var result=FormatDetector.Resolve(hints,new MediaFormatHints{source=FormatEvidence.Frames,projection=1,score=.95f});
            Assert.That(result.candidates[0].projectionCandidates,Is.EqualTo(new[]{1,2}));
            Assert.That(result.candidates[0].stereoCandidates,Is.EqualTo(new[]{1,2}));
            StringAssert.Contains("VR180 / VR360",result.explanation);Assert.That(result.selected.projection,Is.EqualTo(-1));
            hints.projectionCandidates[0]=0;Assert.That(result.candidates[0].projectionCandidates[0],Is.EqualTo(1));
        }
        [Test] public void SamePriorityMetadataConflictBlocksWeakerTags()
        {
            var result=FormatDetector.Resolve(new MediaFormatHints{source=FormatEvidence.Metadata,stereo=1},new MediaFormatHints{source=FormatEvidence.Metadata,stereo=2},FormatDetector.Filename("film.SBS.mp4"));
            Assert.That(result.selected.stereo,Is.EqualTo(-1));Assert.That(result.selected.blockedFields&2,Is.EqualTo(2));Assert.That(result.candidates.Length,Is.EqualTo(3));
        }
        [TestCase("HSBS",1,1)][TestCase("FSBS",1,0)][TestCase("HOU",2,1)][TestCase("HTB",2,1)][TestCase("FOU",2,0)][TestCase("FTB",2,0)]
        public void PackingTagsSelectTheirIndependentFields(string tag,int stereo,int half){var hints=FormatDetector.Filename("film."+tag+".mp4");Assert.That(hints.stereo,Is.EqualTo(stereo));Assert.That(hints.half,Is.EqualTo(half));Assert.That(hints.projection,Is.EqualTo(-1));}
        [Test] public void ReversingHeldStickRequiresNeutralBeforeAnotherSeek()
        {
            var gesture=new StickTransportGesture();Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,0),Is.EqualTo(10));
            Assert.That(gesture.Update(Vector2.left,true,Vector2.zero,true,false,1),Is.Zero);Assert.That(gesture.Update(Vector2.left,true,Vector2.zero,true,false,2),Is.Zero);
            gesture.Update(Vector2.zero,true,Vector2.zero,true,false,3);Assert.That(gesture.Update(Vector2.left,true,Vector2.zero,true,false,4),Is.EqualTo(-10));
        }
        [Test] public void OtherHandCannotInheritAStillDeflectedGesture()
        {
            var gesture=new StickTransportGesture();Assert.That(gesture.Update(Vector2.zero,true,Vector2.right,true,false,0),Is.EqualTo(10));
            gesture.Update(Vector2.left,true,Vector2.right,true,false,.1f);Assert.That(gesture.Update(Vector2.left,true,Vector2.zero,true,false,.2f),Is.Zero);
            Assert.That(gesture.Update(Vector2.left,true,Vector2.zero,true,false,1),Is.Zero);gesture.Update(Vector2.zero,true,Vector2.zero,true,false,2);
            Assert.That(gesture.Update(Vector2.left,true,Vector2.zero,true,false,3),Is.EqualTo(-10));
        }
        [Test] public void HiddenSelectedTextFieldDoesNotBlockTransport()
        {
            var root=new GameObject("Input regression",typeof(RectTransform));
            try{
                var field=root.AddComponent<TMPro.TMP_InputField>();
                typeof(TMPro.TMP_InputField).GetField("m_AllowInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(field,true);
                Assert.That(VrUiInput.IsTyping(root));root.SetActive(false);Assert.That(VrUiInput.IsTyping(root),Is.False);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void HidingModalPanelReleasesTransportSuppression()
        {
            var root=new GameObject("Modal regression");var canvasObject=new GameObject("Canvas",typeof(Canvas));var ui=root.AddComponent<PlayerUI>();
            var canvasProperty=typeof(PlayerUI).GetProperty("Canvas");
            try{
                canvasProperty.SetValue(ui,canvasObject.GetComponent<Canvas>());
                typeof(PlayerUI).GetField("modal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(ui,"tracks");
                Assert.That(ui.BlocksTransport);canvasObject.SetActive(false);Assert.That(ui.BlocksTransport,Is.False);canvasObject.SetActive(true);Assert.That(ui.BlocksTransport);
            }finally{canvasProperty.SetValue(ui,null);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(canvasObject);}
        }
        [TestCase("OnApplicationFocus",false)][TestCase("OnApplicationPause",true)]
        public void LifecycleInterruptStopsHeldGestureBeforeAnotherInputTick(string message,bool value)
        {
            var root=new GameObject("Focus regression",typeof(UnityEngine.EventSystems.EventSystem));
            try{
                var input=root.AddComponent<VrUiInput>();
                var gesture=(StickTransportGesture)typeof(VrUiInput).GetField("transport",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(input);
                Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,0),Is.EqualTo(10));
                typeof(VrUiInput).GetMethod(message,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(input,new object[]{value});Assert.That(gesture.Owner,Is.EqualTo(-1));
                Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,1),Is.Zero);
                gesture.Update(Vector2.zero,true,Vector2.zero,true,false,2);Assert.That(gesture.Update(Vector2.right,true,Vector2.zero,true,false,3),Is.EqualTo(10));
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
