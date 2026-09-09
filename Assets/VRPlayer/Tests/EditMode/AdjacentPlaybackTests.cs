using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class AdjacentPlaybackTests
    {
        private GameObject root;
        private PlayerApp app;
        private PlayerStore store;
        private string folder, directory;
        private FileEntry first, second, third;

        [SetUp] public void SetUp()
        {
            folder=Path.Combine(Path.GetTempPath(),"VRPlayer-adjacent-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            directory=new Uri(folder).AbsoluteUri;
            first=Entry("A.360.OU.mp4");second=Entry("B.180.SBS.mp4");third=Entry("C.mp4");
            store=new PlayerStore(Path.Combine(folder,"settings.json"));store.Settings.formatDetection=FormatDetectionMode.Metadata;
            root=new GameObject("Adjacent playback regression");app=root.AddComponent<PlayerApp>();
            SetProperty("Store",store);SetProperty("Backend",new TestBackend());
            SetProperty("Presenter",root.AddComponent<VideoPresenter>());SetProperty("UI",root.AddComponent<PlayerUI>());
            SetProperty("Source",FileSource.Directory);SetProperty("DirectoryUri",directory);
            SetProperty("Files",new List<FileEntry>{third,second,first});root.SetActive(false);
        }

        private FileEntry Entry(string name)
        {
            string path=Path.Combine(folder,name);File.WriteAllBytes(path,Array.Empty<byte>());
            return new FileEntry{name=name,uri=new Uri(path).AbsoluteUri};
        }
        private void SetProperty(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);

        [TearDown] public void TearDown()
        {
            // The presenter is owned by this test object, so avoid runtime Destroy in EditMode.
            SetProperty("Presenter",null);UnityEngine.Object.DestroyImmediate(root);
            foreach(string path in Directory.GetFiles(folder))File.Delete(path);Directory.Delete(folder);
        }

        [TestCase(1)][TestCase(-1)]
        public void AdjacentButtonOverridesSavedTargetFormatAndPersistsIt(int direction)
        {
            var source=direction>0?first:second;var target=direction>0?second:first;
            var saved=store.Touch(target,directory);saved.formatLocked=true;saved.formatCache=new FormatDetectionCache();
            saved.view=new ViewSettings{projection=Projection.Flat,stereo=StereoLayout.Mono,distance=7,brightness=1.4f,monoEye=true};
            saved.subtitleSelection=SubtitleSelection.Off;saved.subtitleDelay=2;saved.position=24000;
            app.Open(source,directory);
            app.ChangeFormat(view=>{view.projection=Projection.Fisheye;view.stereo=StereoLayout.TopBottom;view.half=false;view.swapEyes=true;view.fisheyeFov=210;view.distance=2;view.brightness=.7f;});
            ViewSettings original=app.Current.view;
            app.Next(direction);
            Assert.That(app.Current.uri,Is.EqualTo(target.uri));Assert.That(app.Current.formatLocked);Assert.That(app.Current.formatCache,Is.Null);
            Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Fisheye));Assert.That(app.Current.view.stereo,Is.EqualTo(StereoLayout.TopBottom));
            Assert.That(app.Current.view.half,Is.False);Assert.That(app.Current.view.swapEyes);Assert.That(app.Current.view.fisheyeFov,Is.EqualTo(210));
            Assert.That(app.Current.view.distance,Is.EqualTo(7));Assert.That(app.Current.view.brightness,Is.EqualTo(1.4f));Assert.That(app.Current.view.monoEye);
            Assert.That(app.Current.subtitleSelection,Is.EqualTo(SubtitleSelection.Off));Assert.That(app.Current.subtitleDelay,Is.EqualTo(2));Assert.That(app.Backend.Snapshot.position,Is.EqualTo(24000));
            Assert.That(app.Current.view,Is.Not.SameAs(original));
            var reloaded=new PlayerStore(Path.Combine(folder,"settings.json")).Find(target.uri);
            Assert.That(reloaded.formatLocked);Assert.That(reloaded.formatLockOrigin,Is.EqualTo(FormatLockOrigin.Inherited));Assert.That(reloaded.view.projection,Is.EqualTo(Projection.Fisheye));
            app.ChangeFormat(view=>view.projection=Projection.Flat);Assert.That(original.projection,Is.EqualTo(Projection.Fisheye));
        }

        [Test] public void BrowsingAnotherFolderDoesNotReplaceAdjacentPlaylist()
        {
            app.Open(first,directory);app.ChangeFormat(view=>view.projection=Projection.Fisheye);
            SetProperty("DirectoryUri",directory+"/other");SetProperty("Files",new List<FileEntry>{new FileEntry{name="Foreign.mp4",uri=directory+"/other/Foreign.mp4"}});
            app.Next(1);Assert.That(app.Current.uri,Is.EqualTo(second.uri));
            app.Next(1);Assert.That(app.Current.uri,Is.EqualTo(third.uri));Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Fisheye));
            app.Next(-1);Assert.That(app.Current.uri,Is.EqualTo(second.uri));Assert.That(app.Current.directory,Is.EqualTo(directory));
        }

        [Test] public async Task OpeningHistoryBuildsPlaylistFromPlaybackFolder()
        {
            SetProperty("Source",FileSource.Recent);SetProperty("DirectoryUri","");
            SetProperty("Files",new List<FileEntry>{first,new FileEntry{name="Foreign.mp4",uri=directory+"/other/Foreign.mp4"}});
            bool listed=false;app.Changed+=()=>{if(app.SubtitleCandidates.Count==0&&app.Current!=null)listed=true;};
            app.Open(first,directory);app.ChangeFormat(view=>view.projection=Projection.Fisheye);
            // Wait for the real asynchronous folder read to publish the video siblings.
            var field=typeof(PlayerApp).GetField("playlist",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int i=0;i<100&&((List<FileEntry>)field.GetValue(app)).Count<3;i++)await Task.Delay(10);
            Assert.That(listed);Assert.That(((List<FileEntry>)field.GetValue(app)).Count,Is.EqualTo(3));
            app.Next(1);Assert.That(app.Current.uri,Is.EqualTo(second.uri));
        }

        [Test] public void InheritedFormatRejectsLateEvidenceAndExplicitRedetectionUnlocksIt()
        {
            app.Open(first,directory);app.ChangeFormat(view=>view.projection=Projection.Fisheye);app.Next(1);
            app.AcceptFrameEvidence(new MediaFormatHints{stereo=2,score=1},new MediaFormatHints{projection=2,score=1});
            app.Backend.Snapshot.stereoMode=1;app.Backend.Snapshot.formatHints=new MediaFormatHints{projection=2,stereo=2};
            typeof(PlayerApp).GetMethod("TickFormatDetection",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,new object[]{app.Backend.Snapshot});
            Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Fisheye));
            app.RedetectFormat();Assert.That(app.Current.formatLocked,Is.False);Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Equirect180));
            app.UndoFormatDetection();Assert.That(app.Current.formatLocked);Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Fisheye));
        }

        [Test] public void OrdinaryOpenUsesTargetSettingsInsteadOfInheriting()
        {
            var target=store.Touch(second,directory);target.formatLocked=true;target.view.projection=Projection.Equirect180;
            app.Open(first,directory);app.ChangeFormat(view=>view.projection=Projection.Fisheye);app.Open(second,directory);
            Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Equirect180));
        }

        [Test] public void StereoOnlyEvidenceContinuesProjectionAnalysisAndRejectsStaleProgress()
        {
            app.Open(third,directory);
            app.AcceptFrameEvidence(new MediaFormatHints{source=FormatEvidence.Frames,stereo=1,score=1,margin=1,consistentFrames=3},null);
            Assert.That(app.Current.view.stereo,Is.EqualTo(StereoLayout.SideBySide));Assert.That(app.NeedsFrameAnalysis);
            app.ReportFrameAnalysis(app.Generation-1,20,new FrameAnalysis{valid=true},"obsolete");Assert.That(app.AnalysisAttempts,Is.Zero);
            app.SetDirectoryVr180Preference(true);Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Equirect180));Assert.That(app.NeedsFrameAnalysis,Is.False);
            app.SetDirectoryVr180Preference(false);Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Flat));Assert.That(app.NeedsFrameAnalysis);
            app.ChangeFormat(view=>view.projection=Projection.Fisheye);
            app.ReportFrameAnalysis(app.Generation,20,new FrameAnalysis{valid=true},"obsolete");Assert.That(app.AnalysisAttempts,Is.Zero);
            Assert.That(app.NeedsFrameAnalysis,Is.False);Assert.That(app.Current.formatLockOrigin,Is.EqualTo(FormatLockOrigin.Manual));
        }

        [Test] public void AutomaticSequentialAdvanceKeepsPerFilmDetection()
        {
            app.Open(first,directory);app.ChangeFormat(view=>view.projection=Projection.Fisheye);app.Next(1,false);
            Assert.That(app.Current.formatLocked,Is.False);Assert.That(app.Current.view.projection,Is.EqualTo(Projection.Equirect180));
        }

        [TestCase(-1)][TestCase(0)][TestCase(3)]
        public void InvalidAdjacentNavigationKeepsCurrentFilm(int delta)
        {
            app.Open(first,directory);int generation=app.Generation;app.Next(delta);
            Assert.That(app.Current.uri,Is.EqualTo(first.uri));Assert.That(app.Generation,Is.EqualTo(generation));
        }

        private sealed class TestBackend : IMediaBackend
        {
            public MediaSnapshot Snapshot{get;private set;}=new MediaSnapshot();
            public Texture Texture=>null;public bool RequiresGammaConversion=>false;public int RenderedFrames=>0;
            public void Open(string uri,long position,int generation)=>Snapshot=new MediaSnapshot{generation=generation,position=position,duration=120000,state=3,playing=true};
            public void Tick(){} public void Play(){} public void Pause(){} public void Stop(){} public void Seek(long millis){}
            public void SetVolume(float value){} public void SetSpeed(float value){} public void SetRepeat(bool value){} public void SelectTrack(string id,int type){} public void Dispose(){}
        }
    }
}
