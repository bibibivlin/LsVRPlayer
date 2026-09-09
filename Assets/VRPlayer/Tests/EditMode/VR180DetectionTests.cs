using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace VRPlayer.Tests
{
    public sealed class VR180DetectionTests
    {
        private static byte[] PerturbedStereo(float disparity,int vertical,bool mixed)
        {
            const int w=512,h=256;var noise=new byte[320*320];new Random(713).NextBytes(noise);var output=new byte[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                float sx=x%256+20+(x>=256?disparity*(mixed&&y>h/2?-1:1):0);int ix=(int)sx,sy=y+20+(x>=256?vertical:0);float t=sx-ix;
                output[y*w+x]=(byte)(noise[sy*320+ix]*(1-t)+noise[sy*320+ix+1]*t);
            }
            return output;
        }
        [TestCase(.3f,0,false)][TestCase(6,3,false)][TestCase(6,0,true)]
        public void RealisticDisparityVariationsDoNotRejectSbs(float disparity,int vertical,bool mixed)
        {
            var result=FrameFormatAnalyzer.Analyze(PerturbedStereo(disparity,vertical,mixed),512,256,-1,CancellationToken.None);
            Assert.That(result.sbs,Is.GreaterThanOrEqualTo(.9f),result.reason);Assert.That(result.tb,Is.LessThan(.6f));
        }
        [Test] public void OrdinaryTranslationalSbsDoesNotProveVr180Projection()
        {
            var result=FrameFormatAnalyzer.Analyze(PerturbedStereo(6,0,false),512,256,-1,CancellationToken.None);
            Assert.That(result.sbs,Is.GreaterThanOrEqualTo(.9f));Assert.That(result.vr180,Is.LessThan(.9f));
        }
        [TestCase(.012f,false)][TestCase(.05f,true)]
        public void SphericalCorrespondencesRequireSeparationFromFisheye(float disparity,bool confirmed)
        {
            var points=new List<StereoCorrespondence>();
            for(int y=0;y<8;y++)for(int x=0;x<10;x++){
                float u=.12f+x*.076f,v=.15f+y*.1f,ru=u+disparity+(x%3)*disparity*.5f;
                float rv=(float)(.5+Math.Atan(Math.Tan((v-.5)*Math.PI)*Math.Cos((ru-.5)*Math.PI)/Math.Cos((u-.5)*Math.PI))/Math.PI);
                points.Add(new StereoCorrespondence{u=u,v=v,rightU=ru,rightV=rv,quality=1});
            }
            var result=ProjectionFrameScorer.Score(points);
            Assert.That(result.vr180>=.9f,Is.EqualTo(confirmed));Assert.That(result.equirectError,Is.LessThan(result.flatError));
            // Small disparities fit several projections within sampling error, so they must abstain.
            if(!confirmed)Assert.That(result.fishError,Is.LessThan(.2f));
        }
        [Test] public void WindowRecoversAfterTexturedFlatIntro()
        {
            var accumulator=new FrameEvidenceAccumulator();for(int i=0;i<5;i++)accumulator.Add(new FrameAnalysis{valid=true});
            MediaFormatHints hints=null;for(int i=0;i<4;i++)hints=accumulator.Add(new FrameAnalysis{valid=true,sbs=.97f,stereo=1});
            Assert.That(FormatDetector.MeetsFrameThreshold(hints));
        }
        [Test] public void SceneCutCannotReuseOldProjectionVotes()
        {
            var accumulator=new FrameEvidenceAccumulator();for(int i=0;i<3;i++)accumulator.Add(new FrameAnalysis{valid=true,stereo=1,sbs=.98f,vr180=.96f,projectionMargin=.25f});
            Assert.That(FormatDetector.MeetsFrameThreshold(accumulator.ProjectionSuggestion()));
            accumulator.Add(new FrameAnalysis{valid=true,sceneChanged=true,sbs=.99f,stereo=1});Assert.That(accumulator.ProjectionSuggestion(),Is.Null);
        }
        [Test] public void DecoderMonoWithoutExplicitContainerFieldIsUnknown()
        {
            var snapshot=new MediaSnapshot{stereoMode=0,formatProbeComplete=true,formatHints=new MediaFormatHints()};
            Assert.That(FormatDetector.StreamHints(snapshot).stereo,Is.EqualTo(-1));snapshot.formatHints.stereo=0;Assert.That(FormatDetector.StreamHints(snapshot).stereo,Is.Zero);
        }
        [Test] public void LayoutConfirmationDoesNotConfirmProjection()
        {
            var result=FormatDetector.Resolve(new MediaFormatHints{source=FormatEvidence.Frames,stereo=1});
            Assert.That(result.StereoStatus,Is.EqualTo(FormatFieldStatus.Confirmed));Assert.That(result.ProjectionStatus,Is.EqualTo(FormatFieldStatus.Unknown));
        }
        [Test] public void StereoApprovalDoesNotApproveVr180FromCalibrationClips()
        {
            var profile=new FrameRecognitionProfile{algorithm=FormatDetector.AlgorithmVersion,stereoAutoApproved=true,evaluated=100,correct=100,vr180AutoApproved=true,vr180Evaluated=5,vr180Correct=5,realPositiveVideos=5};
            Assert.That(profile.Approved);Assert.That(profile.Vr180Approved,Is.False);
        }
        [Test] public void SamplerWaitsThroughIntroWithinBoundedWork()
        {
            var budget=new FrameSamplingBudget();int count=0;for(int i=0;i<60;i++){budget.Tick(1,true);if(budget.TrySample())count++;}
            Assert.That(count,Is.EqualTo(20));Assert.That(budget.Expired);Assert.That(budget.TrySample(),Is.False);
        }
        [Test] public void PausedTimeDoesNotConsumeSamplingWindow()
        {
            var budget=new FrameSamplingBudget();budget.Tick(120,false);Assert.That(budget.ActiveSeconds,Is.Zero);Assert.That(budget.TrySample());
        }
        [Test] public void LateFirstContentReceivesCloselySpacedConfirmationSamples()
        {
            var budget=new FrameSamplingBudget();
            for(int i=0;i<24;i++){budget.Tick(1,true);if(budget.TrySample())budget.RecordResult(false,false);}
            budget.Tick(1,true);Assert.That(budget.TrySample());budget.RecordResult(true,true);
            budget.Tick(1,true);Assert.That(budget.TrySample());budget.RecordResult(true,false);
            budget.Tick(1,true);Assert.That(budget.TrySample());budget.RecordResult(true,false);
            budget.Tick(1,true);Assert.That(budget.TrySample(),Is.False);
        }
        [Test] public void ExplicitFieldsOverrideDirectoryPreference()
        {
            var result=FormatDetector.Resolve(new MediaFormatHints{source=FormatEvidence.DirectoryPreference,projection=1},new MediaFormatHints{source=FormatEvidence.Metadata,projection=2});Assert.That(result.selected.projection,Is.EqualTo(2));
        }
    }
}
