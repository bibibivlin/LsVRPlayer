using System;
using System.Threading;
using NUnit.Framework;

namespace VRPlayer.Tests
{
    public sealed class FrameRecognitionTests
    {
        // Different randomized textures for every frame; disparity must generalize across patches.
        public static byte[] Image(int seed,int layout,int variant=0)
        {
            const int width=256,height=192;var image=new byte[width*height];var noise=new byte[320*256];new Random(seed).NextBytes(noise);
            int eyeWidth=layout==1?width/2:width,eyeHeight=layout==2?height/2:height;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                bool second=layout==1?x>=eyeWidth:layout==2&&y>=eyeHeight;int sx=x%eyeWidth,sy=y%eyeHeight;
                if(second){if(variant==1)sx=eyeWidth-1-sx;else if(variant==2)sy+=70;else if(variant!=3)sx+=layout==1?3:6;}
                int at=(sy+20)*320+sx+20;image[y*width+x]=noise[at%noise.Length];
            }
            return image;
        }
        [TestCase(1)][TestCase(2)] public void TexturedStereoHasConsistentHorizontalDisparity(int layout)
        {
            var accumulator=new FrameEvidenceAccumulator();MediaFormatHints evidence=null;
            for(int i=0;i<3;i++)evidence=accumulator.Add(FrameFormatAnalyzer.Analyze(Image(42+i,layout),256,192,-1,CancellationToken.None));
            Assert.That(evidence.stereo,Is.EqualTo(layout));Assert.That(FormatDetector.MeetsFrameThreshold(evidence),Is.True,$"score={evidence.score}, margin={evidence.margin}, frames={evidence.consistentFrames}");
        }
        [TestCase(0,0)][TestCase(1,1)][TestCase(2,1)][TestCase(1,2)][TestCase(2,2)][TestCase(1,3)][TestCase(2,3)]
        public void NormalSplitMirrorAndDuplicateImagesAreNotAutomaticallyStereo(int layout,int variant)
        {
            var accumulator=new FrameEvidenceAccumulator();MediaFormatHints evidence=null;
            for(int i=0;i<3;i++)evidence=accumulator.Add(FrameFormatAnalyzer.Analyze(Image(130+i,layout,variant),256,192,-1,CancellationToken.None));
            Assert.That(FormatDetector.MeetsFrameThreshold(evidence),Is.False,$"layout={layout}, variant={variant}, score={evidence.score}");
        }
        [Test] public void BlackAndUniformFramesAreNotEvidence(){Assert.That(FrameFormatAnalyzer.Analyze(new byte[256*192],256,192,-1,CancellationToken.None).valid,Is.False);var white=new byte[256*192];Array.Fill(white,(byte)128);Assert.That(FrameFormatAnalyzer.Analyze(white,256,192,-1,CancellationToken.None).valid,Is.False);}
        [Test] public void FrameAnalysisHonorsCancellation(){using var source=new CancellationTokenSource();source.Cancel();Assert.Throws<OperationCanceledException>(()=>FrameFormatAnalyzer.Analyze(Image(3,1),256,192,-1,source.Token));}
        [Test] public void UnvalidatedProfileCannotEnableAutomaticApplication(){Assert.That(new FrameRecognitionProfile{algorithm=1,stereoAutoApproved=true,evaluated=10,correct=10}.Approved,Is.False);Assert.That(new FrameRecognitionProfile{algorithm=1,stereoAutoApproved=true,evaluated=100,correct=97}.Approved,Is.False);}
    }
}
