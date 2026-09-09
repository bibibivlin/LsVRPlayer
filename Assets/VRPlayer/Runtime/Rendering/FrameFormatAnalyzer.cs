using System;
using System.Collections.Generic;
using System.Threading;

namespace VRPlayer
{
    [Serializable] public sealed class FrameAnalysis
    {
        public bool valid,sceneChanged;
        public float sbs,tb,vr180,vr360,fisheye,projectionMargin,coverage;
        public int stereo=-1,matches,regions,projectionCurves;
        public string reason="画面亮度或纹理不足";
        public float flatError,equirectError;
    }
    public static class FrameFormatAnalyzer
    {
        public static FrameAnalysis Analyze(byte[] gray,int width,int height,int knownStereo,CancellationToken token)
        {
            var result=new FrameAnalysis();if(gray==null||width<64||height<64||gray.Length!=width*height)return result;
            token.ThrowIfCancellationRequested();double sum=0,square=0;foreach(byte value in gray){sum+=value;square+=value*value;}
            double mean=sum/gray.Length;if(mean<6||mean>249||square/gray.Length-mean*mean<36)return result;
            result.valid=true;
            var horizontal=StereoFrameMatcher.Match(gray,width,height,true,token);
            var vertical=knownStereo==1?new StereoMatchResult():StereoFrameMatcher.Match(gray,width,height,false,token);
            if(Math.Max(horizontal.score,vertical.score)<.9f&&Math.Abs(horizontal.score-vertical.score)>=.15f){
                if(horizontal.score>vertical.score)horizontal=StereoFrameMatcher.Refine(gray,width,height,true,horizontal,token);
                else vertical=StereoFrameMatcher.Refine(gray,width,height,false,vertical,token);
            }
            result.sbs=horizontal.score;result.tb=vertical.score;
            result.stereo=knownStereo>=0?knownStereo:Math.Max(result.sbs,result.tb)>=.9f?(result.sbs>result.tb?1:2):0;
            var best=result.sbs>=result.tb?horizontal:vertical;result.matches=best.points.Count;result.regions=best.regions;result.coverage=best.coverage;result.reason=best.reason;
            if(result.stereo==1&&horizontal.points.Count>=24){
                var projection=ProjectionFrameScorer.Score(horizontal.points);
                result.vr180=projection.vr180;result.projectionMargin=projection.margin;result.flatError=projection.flatError;result.equirectError=projection.equirectError;
                if(result.vr180>0)result.reason+=" · "+projection.reason;
                var edges=SphericalEdgeEvidence.Analyze(gray,width,height,token);result.projectionCurves=edges.curves;
                if(edges.score>result.vr180){result.vr180=edges.score;result.projectionMargin=edges.margin;result.reason+=" · 多区域球面边缘形变";}
                if(result.vr180==0&&Math.Abs(width/(2f*height)-1)<.12f){result.vr180=.68f;result.reason+=" · 单眼近方形，VR180 待确认";}
            }
            int ew=result.stereo==1?width/2:width,eh=result.stereo==2?height/2:height;
            double seam=0,interior=0;
            for(int y=eh/5;y<eh*4/5;y+=2){seam+=Math.Abs(gray[y*width]-gray[y*width+ew-1]);interior+=Math.Abs(gray[y*width+ew/3]-gray[y*width+ew*2/3]);}
            float continuity=(float)Math.Max(0,1-seam/Math.Max(1,interior));
            if(Math.Abs(ew/(float)eh-2)<.12f&&continuity>.5f)result.vr360=Math.Min(.86f,.65f+continuity*.2f);
            int outside=0,dark=0,inside=0,bright=0;
            for(int y=0;y<eh;y+=3)for(int x=0;x<ew;x+=3){double dx=2*(x+.5)/ew-1,dy=2*(y+.5)/eh-1,r=dx*dx+dy*dy;byte value=gray[y*width+x];if(r>1.12){outside++;if(value<18)dark++;}else if(r<.75){inside++;if(value>28)bright++;}}
            if(outside>0&&inside>0&&dark/(float)outside>.96f&&bright/(float)inside>.7f){result.fisheye=.86f;result.vr180=0;result.projectionMargin=0;}
            return result;
        }
    }
    public sealed class FrameEvidenceAccumulator
    {
        private readonly Queue<FrameAnalysis> window=new Queue<FrameAnalysis>();
        public void Reset()=>window.Clear();
        public MediaFormatHints Add(FrameAnalysis result)
        {
            if(result==null||!result.valid)return null;if(result.sceneChanged)Reset();window.Enqueue(result);if(window.Count>5)window.Dequeue();
            int sbs=0,tb=0;float hs=0,vs=0;
            foreach(var frame in window){if(frame.sbs>=.9f&&frame.sbs-frame.tb>=.15f){sbs++;hs+=frame.sbs;}if(frame.tb>=.9f&&frame.tb-frame.sbs>=.15f){tb++;vs+=frame.tb;}}
            bool horizontal=sbs>=tb;int count=horizontal?sbs:tb,opposite=horizontal?tb:sbs;
            float score=count>0?(horizontal?hs:vs)/count:0;
            bool dominant=count>=Math.Ceiling(window.Count*.75)&&opposite==0;
            return new MediaFormatHints{source=FormatEvidence.Frames,reason="多区域双向匹配与跨帧一致性",stereo=count>0?(horizontal?1:2):-1,score=dominant?score:Math.Min(.89f,score),margin=opposite==0?score:0,consistentFrames=count};
        }
        public MediaFormatHints ProjectionSuggestion()
        {
            if(window.Count<3)return null;int votes=0;float sum=0,margin=0,fish=0,full=0,partial=0;
            foreach(var frame in window){if(frame.stereo==1&&frame.vr180>=.9f&&frame.projectionMargin>=.15f){votes++;sum+=frame.vr180;margin+=frame.projectionMargin;}fish+=frame.fisheye;full+=frame.vr360;partial+=frame.vr180;}
            if(votes>=3&&votes>=Math.Ceiling(window.Count*.75))return new MediaFormatHints{source=FormatEvidence.Frames,projection=1,stereo=1,score=sum/votes,margin=margin/votes,consistentFrames=votes,reason="跨帧双眼球面几何支持 VR180 SBS"};
            float top=Math.Max(fish,full)/window.Count;
            if(partial/window.Count>Math.Max(.6f,top))return new MediaFormatHints{source=FormatEvidence.Frames,projection=1,stereo=1,score=Math.Min(.89f,partial/window.Count),consistentFrames=votes,reason="SBS 与画面特征支持 VR180 候选，投影证据尚不足"};
            return top>=.6f?new MediaFormatHints{source=FormatEvidence.Frames,projection=fish>=full?3:2,score=top,consistentFrames=window.Count,reason="画面边界与接缝（建议）"}:null;
        }
    }
}
