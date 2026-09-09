using System;
using System.Collections.Generic;
using System.Threading;

namespace VRPlayer
{
    public sealed class SphericalEdgeScore
    {
        public float score,margin;public int curves;
    }
    // Straight world edges become tan(latitude)=a*cos(longitude)+b*sin(longitude).
    // Require several long, distinct arcs and compare the same observed edges with a straight line.
    public static class SphericalEdgeEvidence
    {
        private struct Edge {public int x,y;public float strength;}
        public static SphericalEdgeScore Analyze(byte[] gray,int width,int height,CancellationToken token)
        {
            const int w=96,h=96;int ew=width/2;var image=new float[w*h];var energy=new float[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                int startX=x*ew/w,endX=Math.Max(startX+1,(x+1)*ew/w),startY=y*height/h,endY=Math.Max(startY+1,(y+1)*height/h);
                float sum=0;int count=0;
                for(int sy=startY;sy<endY;sy++)for(int sx=startX;sx<endX;sx++){sum+=gray[sy*width+sx]+gray[sy*width+sx+ew];count+=2;}
                // Area filtering avoids aliasing fine flat textures into spurious curved edges.
                image[y*w+x]=sum/count;
            }
            var edges=new List<Edge>();
            for(int y=3;y<h-3;y++)for(int x=5;x<w-5;x++){
                float dy=Math.Abs(image[(y+1)*w+x]-image[(y-1)*w+x]);
                float dx=Math.Abs(image[y*w+x+1]-image[y*w+x-1]);energy[y*w+x]=Math.Max(0,dy-dx*.3f);
            }
            for(int x=8;x<w-8;x+=4)for(int band=0;band<4;band++){
                int start=5+band*21,end=start+20;float top=8;int at=-1;
                for(int y=start;y<end;y++){if(energy[y*w+x]>top){top=energy[y*w+x];at=y;}}
                if(at>=0)edges.Add(new Edge{x=x,y=at,strength=top});
            }
            var accepted=new List<double>();double total=0;
            for(int i=0;i<edges.Count;i++){
                token.ThrowIfCancellationRequested();var p=edges[i];
                for(int j=i+1;j<edges.Count;j++){
                    var q=edges[j];if(Math.Abs(p.x-q.x)<35||Math.Abs(p.y-q.y)>35)continue;
                    double l1=(p.x+.5-w/2)/w*Math.PI,l2=(q.x+.5-w/2)/w*Math.PI;
                    double c1=Math.Cos(l1),s1=Math.Sin(l1),c2=Math.Cos(l2),s2=Math.Sin(l2),det=c1*s2-c2*s1;
                    if(Math.Abs(det)<.2)continue;
                    double t1=Math.Tan((p.y+.5-h/2)/h*Math.PI),t2=Math.Tan((q.y+.5-h/2)/h*Math.PI);
                    double a=(t1*s2-t2*s1)/det,b=(c1*t2-c2*t1)/det;
                    double center=Math.Atan(a)/Math.PI*h+h/2-.5;
                    if(center<8||center>h-9||Math.Abs(center-h/2)<12)continue;
                    double chord=p.y+(q.y-p.y)*(w/2d-p.x)/(q.x-p.x);if(Math.Abs(center-chord)<3)continue;
                    int hit=0,count=0,lineHit=0,backgroundHit=0;double strength=0;
                    for(int x=8;x<w-8;x+=3){
                        double longitude=(x+.5-w/2)/w*Math.PI;double yf=Math.Atan(a*Math.Cos(longitude)+b*Math.Sin(longitude))/Math.PI*h+h/2-.5;
                        int y=(int)Math.Round(yf);if(y<4||y>h-5)continue;count++;
                        int at=y;for(int offset=-1;offset<=1;offset++)if(energy[(y+offset)*w+x]>energy[at*w+x])at=y+offset;
                        float best=energy[at*w+x];
                        double tangent=a*Math.Cos(longitude)+b*Math.Sin(longitude),slope=(-a*Math.Sin(longitude)+b*Math.Cos(longitude))/(1+tangent*tangent);
                        double gx=image[at*w+x+1]-image[at*w+x-1],gy=image[(at+1)*w+x]-image[(at-1)*w+x];
                        double alignment=Math.Abs(gy-gx*slope)/Math.Sqrt(Math.Max(1,(gx*gx+gy*gy)*(1+slope*slope)));
                        // A curve must be a coherent edge, not unrelated texture peaks near a model.
                        if(best>10&&alignment>.94){hit++;strength+=Math.Min(1,best/25);}
                        int lineY=(int)Math.Round(p.y+(q.y-p.y)*(x-p.x)/(double)(q.x-p.x));
                        if(lineY>1&&lineY<h-2&&Math.Max(energy[lineY*w+x],Math.Max(energy[(lineY-1)*w+x],energy[(lineY+1)*w+x]))>10)lineHit++;
                        if(energy[(y-4)*w+x]>10)backgroundHit++;if(energy[(y+4)*w+x]>10)backgroundHit++;
                    }
                    if(count<22||hit/(float)count<.72f||strength/count<.55||(hit-Math.Max(lineHit,backgroundHit*.5))/count<.18)continue;
                    // Distinct vertical bands prevent counting one curved logo repeatedly.
                    bool duplicate=false;foreach(double previous in accepted)if(Math.Abs(previous-center)<9)duplicate=true;
                    if(duplicate)continue;
                    accepted.Add(center);total+=hit/(double)count;
                }
            }
            var result=new SphericalEdgeScore{curves=accepted.Count};
            if(accepted.Count>=2){result.score=(float)Math.Min(.97,.88+.09*total/accepted.Count);result.margin=.2f;}
            return result;
        }
    }
}
