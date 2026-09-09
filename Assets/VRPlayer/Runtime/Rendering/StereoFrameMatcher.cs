using System;
using System.Collections.Generic;
using System.Threading;

namespace VRPlayer
{
    public struct StereoCorrespondence
    {
        public float u,v,rightU,rightV,quality;
    }
    public sealed class StereoMatchResult
    {
        public float score,coverage;
        public int textured,regions;
        public string reason="";
        public readonly List<StereoCorrespondence> points=new List<StereoCorrespondence>();
    }
    // Census seeds a coarse search; normalized correlation refines both axes and subpixel disparity.
    public static class StereoFrameMatcher
    {
        private sealed class Eye
        {
            public int w,h;public float[] pixels;public uint[] census;
            public float At(int x,int y)=>pixels[y*w+x];
        }
        public static StereoMatchResult Match(byte[] image,int width,int height,bool sbs,CancellationToken token)
            =>MatchPass(image,width,height,sbs,false,token);
        public static StereoMatchResult Refine(byte[] image,int width,int height,bool sbs,StereoMatchResult result,CancellationToken token)
        {
            // Only refine already supported matches. Blurring unrelated or repetitive views
            // can erase the differences which correctly rejected a mirror or ordinary split.
            if(result.score>=.65f&&result.score<.9f){
                var refined=MatchPass(image,width,height,sbs,true,token);
                if(refined.score>result.score&&HasLocalConsistency(refined.points))return refined;
            }
            return result;
        }
        private static bool HasLocalConsistency(List<StereoCorrespondence> points)
        {
            int coherent=0;
            foreach(var p in points){
                int nearby=0,consistent=0;
                foreach(var q in points){
                    float distance=(p.u-q.u)*(p.u-q.u)+(p.v-q.v)*(p.v-q.v);
                    if(distance<.0001f||distance>.035f)continue;nearby++;
                    if(Math.Abs((p.rightU-p.u)-(q.rightU-q.u))<.04f&&Math.Abs((p.rightV-p.v)-(q.rightV-q.v))<.02f)consistent++;
                }
                if(nearby>=2&&consistent>=nearby*.65f)coherent++;
            }
            return coherent>=points.Count*.7f;
        }
        private static StereoMatchResult MatchPass(byte[] image,int width,int height,bool sbs,bool smooth,CancellationToken token)
        {
            int ew=sbs?width/2:width,eh=sbs?height:height/2;
            float scale=Math.Min(1,384f/Math.Max(ew,eh));int w=Math.Max(24,(int)(ew*scale)),h=Math.Max(24,(int)(eh*scale));
            var a=Resize(image,width,ew,eh,0,0,w,h);var b=Resize(image,width,ew,eh,sbs?ew:0,sbs?0:eh,w,h);
            if(smooth){Smooth(a);Smooth(b);}
            var ca=Half(a);var cb=Half(b);var result=new StereoMatchResult();int[] regions=new int[9];int moving=0;double quality=0;
            int range=Math.Max(4,(int)(ca.w*.12f));
            for(int gy=0;gy<9;gy++){
                token.ThrowIfCancellationRequested();
                for(int gx=0;gx<11;gx++){
                    int x=6+(w-13)*gx/10,y=6+(h-13)*gy/8;float feature=-1;int px=x,py=y;
                    for(int yy=Math.Max(6,y-h/25);yy<=Math.Min(h-7,y+h/25);yy+=3)for(int xx=Math.Max(6,x-w/30);xx<=Math.Min(w-7,x+w/30);xx+=3){float strength=Corner(a,xx,yy);if(strength>feature){feature=strength;px=xx;py=yy;}}
                    x=px;y=py;
                    if(Variance(a,x,y)<25)continue;result.textured++;
                    int cx=x/2,cy=y/2,bx=cx,by=cy;float bestCost=float.MaxValue;
                    for(int dy=-6;dy<=6;dy++)for(int dx=-range;dx<=range;dx++){
                        int xx=cx+dx,yy=cy+dy;if(xx<3||yy<3||xx>=cb.w-3||yy>=cb.h-3)continue;
                        uint difference=ca.census[cy*ca.w+cx]^cb.census[yy*cb.w+xx];
                        float cost=Bits(difference)*.15f+(float)(1-Correlation(ca,cx,cy,cb,xx,yy,3))*30;
                        if(cost<bestCost){bestCost=cost;bx=xx;by=yy;}
                    }
                    int rx=bx*2,ry=by*2;double best=-1;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){
                        int xx=bx*2+dx,yy=by*2+dy;if(xx<4||yy<4||xx>=w-4||yy>=h-4)continue;
                        double score=Correlation(a,x,y,b,xx,yy,3);
                        if(score>best){best=score;rx=xx;ry=yy;}
                    }
                    if(best<.86)continue;
                    // The reverse match must return to the same neighbourhood, not another repeated texture.
                    double reverse=-1;int backX=x,backY=y;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-4;dx<=4;dx++){
                        int xx=x+dx,yy=y+dy;if(xx<4||yy<4||xx>=w-4||yy>=h-4)continue;
                        double score=Correlation(b,rx,ry,a,xx,yy,3);if(score>reverse){reverse=score;backX=xx;backY=yy;}
                    }
                    if(Math.Abs(backX-x)>1||Math.Abs(backY-y)>1)continue;
                    float sx=Subpixel(Correlation(a,x,y,b,rx-1,ry,3),best,Correlation(a,x,y,b,rx+1,ry,3));
                    float sy=Subpixel(Correlation(a,x,y,b,rx,ry-1,3),best,Correlation(a,x,y,b,rx,ry+1,3));
                    if(Math.Abs(rx+sx-x)>.08f)moving++;
                    regions[Math.Min(2,gy/3)*3+Math.Min(2,gx/4)]++;quality+=best;
                    result.points.Add(new StereoCorrespondence{u=(x+.5f)/w,v=(y+.5f)/h,rightU=(rx+sx+.5f)/w,rightV=(ry+sy+.5f)/h,quality=(float)best});
                }
            }
            foreach(int count in regions)if(count>=2)result.regions++;
            result.coverage=result.points.Count/(float)Math.Max(1,result.textured);
            if(result.textured<18||result.points.Count<18||result.regions<5){result.reason="有效纹理或匹配区域不足";return result;}
            if(moving<Math.Max(5,result.points.Count/6)){result.reason="左右画面近似复制，缺少可确认视差";return result;}
            if(result.coverage<.35f){result.reason="左右对应不足，可能是普通分屏";return result;}
            float mean=(float)(quality/result.points.Count);
            result.score=Math.Min(1,.65f*Math.Min(1,result.coverage/.5f)+.35f*Math.Min(1,mean/.96f));
            result.reason="多区域双向匹配与亚像素视差";return result;
        }
        private static Eye Resize(byte[] source,int stride,int sw,int sh,int ox,int oy,int w,int h)
        {
            var eye=new Eye{w=w,h=h,pixels=new float[w*h]};
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                float sx=Math.Max(0,(x+.5f)*sw/w-.5f),sy=Math.Max(0,(y+.5f)*sh/h-.5f);int ix=(int)sx,iy=(int)sy;
                float fx=sx-ix,fy=sy-iy;int nx=Math.Min(sw-1,ix+1),ny=Math.Min(sh-1,iy+1);
                eye.pixels[y*w+x]=(source[(iy+oy)*stride+ix+ox]*(1-fx)+source[(iy+oy)*stride+nx+ox]*fx)*(1-fy)+(source[(ny+oy)*stride+ix+ox]*(1-fx)+source[(ny+oy)*stride+nx+ox]*fx)*fy;
            }
            return eye;
        }
        private static Eye Half(Eye source)
        {
            var eye=new Eye{w=source.w/2,h=source.h/2};eye.pixels=new float[eye.w*eye.h];eye.census=new uint[eye.pixels.Length];
            for(int y=0;y<eye.h;y++)for(int x=0;x<eye.w;x++)eye.pixels[y*eye.w+x]=(source.At(x*2,y*2)+source.At(x*2+1,y*2)+source.At(x*2,y*2+1)+source.At(x*2+1,y*2+1))*.25f;
            for(int y=2;y<eye.h-2;y++)for(int x=2;x<eye.w-2;x++){
                uint descriptor=0;float center=eye.At(x,y);for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){if(dx==0&&dy==0)continue;descriptor=(descriptor<<1)|(eye.At(x+dx,y+dy)<center?1u:0u);}eye.census[y*eye.w+x]=descriptor;
            }
            return eye;
        }
        private static void Smooth(Eye eye)
        {
            // Suppress codec noise and subpixel resampling differences before local matching.
            var horizontal=new float[eye.pixels.Length];
            for(int y=0;y<eye.h;y++)for(int x=0;x<eye.w;x++)horizontal[y*eye.w+x]=(eye.At(Math.Max(0,x-1),y)+2*eye.At(x,y)+eye.At(Math.Min(eye.w-1,x+1),y))*.25f;
            for(int y=0;y<eye.h;y++)for(int x=0;x<eye.w;x++)eye.pixels[y*eye.w+x]=(horizontal[Math.Max(0,y-1)*eye.w+x]+2*horizontal[y*eye.w+x]+horizontal[Math.Min(eye.h-1,y+1)*eye.w+x])*.25f;
        }
        private static int Bits(uint x){x-=(x>>1)&0x55555555;x=(x&0x33333333)+((x>>2)&0x33333333);return (int)((((x+(x>>4))&0x0f0f0f0f)*0x01010101)>>24);}
        private static float Variance(Eye a,int x,int y)
        {
            double sum=0,square=0;for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++){float v=a.At(x+dx,y+dy);sum+=v;square+=v*v;}return (float)(square/49-sum*sum/(49*49));
        }
        private static float Corner(Eye a,int x,int y)
        {
            float xx=0,yy=0,xy=0;for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){float gx=a.At(x+dx+1,y+dy)-a.At(x+dx-1,y+dy),gy=a.At(x+dx,y+dy+1)-a.At(x+dx,y+dy-1);xx+=gx*gx;yy+=gy*gy;xy+=gx*gy;}return (xx*yy-xy*xy)/(xx+yy+1);
        }
        private static double Correlation(Eye a,int ax,int ay,Eye b,int bx,int by,int radius)
        {
            double sa=0,sb=0,aa=0,bb=0,ab=0;int n=(radius*2+1)*(radius*2+1);
            for(int y=-radius;y<=radius;y++)for(int x=-radius;x<=radius;x++){double av=a.At(ax+x,ay+y),bv=b.At(bx+x,by+y);sa+=av;sb+=bv;aa+=av*av;bb+=bv*bv;ab+=av*bv;}
            return (ab-sa*sb/n)/Math.Sqrt(Math.Max(1,(aa-sa*sa/n)*(bb-sb*sb/n)));
        }
        private static float Subpixel(double before,double center,double after)
        {
            double denominator=before-2*center+after;if(denominator>=-.0001)return 0;return (float)Math.Max(-.5,Math.Min(.5,.5*(before-after)/denominator));
        }
    }
}
