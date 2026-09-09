using System;
using System.Collections.Generic;

namespace VRPlayer
{
    public sealed class ProjectionFrameScore
    {
        public float vr180,margin,flatError,equirectError,fishError,vr360Error;
        public string reason="投影证据不足";
    }
    public static class ProjectionFrameScorer
    {
        // Parallel horizontal stereo baseline: y/z must agree after unprojecting each eye.
        // A fitted vertical offset tolerates small capture alignment errors; no format is inferred from aspect alone.
        public static ProjectionFrameScore Score(IReadOnlyList<StereoCorrespondence> points)
        {
            var result=new ProjectionFrameScore();if(points.Count<24)return result;
            var flat=new List<double>();var equi=new List<double>();var fish=new List<double>();var full=new List<double>();int peripheral=0;
            foreach(var p in points){
                if(p.quality<.8f||p.u<.08f||p.u>.92f||p.v<.1f||p.v>.9f||p.rightU<.03f||p.rightU>.97f)continue;
                if(Math.Abs(p.u-.5)>.2&&Math.Abs(p.v-.5)>.12)peripheral++;
                flat.Add(p.rightV-p.v);
                equi.Add(p.rightV-PredictEquirect(p,Math.PI));
                full.Add(p.rightV-PredictEquirect(p,Math.PI*2));
                fish.Add(p.rightV-PredictFish(p));
            }
            if(flat.Count<20||peripheral<8)return result;
            result.flatError=Error(flat);result.equirectError=Error(equi);result.fishError=Error(fish);result.vr360Error=Error(full);
            double rival=Math.Min(result.flatError,Math.Min(result.fishError,result.vr360Error));
            if(rival<.35||result.equirectError>.65){result.reason="平面或其他投影仍可解释匹配";return result;}
            float advantage=(float)(1-result.equirectError/Math.Max(.001,rival));
            result.margin=Math.Max(0,advantage);
            result.vr180=advantage>.25f?Math.Min(1,.75f+.25f*Math.Min(1,advantage/.55f)):0;
            result.reason="双眼球面几何优于平面／鱼眼／360 候选";return result;
        }
        private static double PredictEquirect(StereoCorrespondence p,double horizontal)
        {
            double left=Math.Cos((p.u-.5)*horizontal),right=Math.Cos((p.rightU-.5)*horizontal);
            if(left*right<=0||Math.Abs(left)<.04)return p.v;
            return .5+Math.Atan(Math.Tan((p.v-.5)*Math.PI)*right/left)/Math.PI;
        }
        private static double FishAngle(double u,double v)
        {
            double x=(u-.5)*2,y=(v-.5)*2,r=Math.Sqrt(x*x+y*y);if(r<.00001)return 0;
            double theta=r*Math.PI/2;return Math.Atan2(y/r*Math.Sin(theta),Math.Cos(theta));
        }
        private static double PredictFish(StereoCorrespondence p)
        {
            double target=FishAngle(p.u,p.v),low=Math.Max(.01,p.v-.15),high=Math.Min(.99,p.v+.15);
            for(int i=0;i<15;i++){double middle=(low+high)/2;if(FishAngle(p.rightU,middle)<target)low=middle;else high=middle;}return (low+high)/2;
        }
        private static float Error(List<double> residuals)
        {
            residuals.Sort();double offset=residuals[residuals.Count/2];var errors=new List<double>();foreach(double value in residuals)errors.Add(Math.Abs(value-offset)*192);errors.Sort();
            // Trim outliers without selecting only the few easiest points.
            double sum=0;int n=Math.Max(1,(int)(errors.Count*.8));for(int i=0;i<n;i++)sum+=errors[i];return (float)(sum/n);
        }
    }
}
