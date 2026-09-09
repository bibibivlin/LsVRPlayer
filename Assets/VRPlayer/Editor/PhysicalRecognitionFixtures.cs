using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace VRPlayer.Editor
{
    // Two ray cameras observe the same textured room and foreground boxes. No image shifts,
    // metadata or filename evidence are involved. These are synthetic mechanism fixtures.
    public static class PhysicalRecognitionFixtures
    {
        [Serializable] private sealed class Manifest {public string sourceType="Synthetic ray-traced rooms, not independent real films";public Sample[] samples;}
        [Serializable] private sealed class Sample {public string name;public int width=512,height=256,stereo=1,projection;public string[] frames;}
        public static string Generate(string root)
        {
            Directory.CreateDirectory(root);var samples=new List<Sample>();
            for(int seed=0;seed<8;seed++)for(int projection=0;projection<4;projection++){
                var sample=new Sample{name=$"room-{seed:00}-projection-{projection}",projection=projection,frames=new string[3]};
                for(int t=0;t<3;t++){
                    byte[] image=Render(seed,projection,t);
                    sample.frames[t]=sample.name+$"-{t}.gray";File.WriteAllBytes(Path.Combine(root,sample.frames[t]),image);
                }
                samples.Add(sample);
            }
            File.WriteAllText(Path.Combine(root,"manifest.json"),JsonUtility.ToJson(new Manifest{samples=samples.ToArray()},true));
            return $"Generated {samples.Count} physical stereo clips with three views each.";
        }
        public static byte[] Render(int seed,int projection,int time)
        {
            const int side=256;var output=new byte[side*side*2];
            for(int eye=0;eye<2;eye++)for(int y=0;y<side;y++)for(int x=0;x<side;x++){
                float u=(x+.5f)/side*2-1,v=(y+.5f)/side*2-1;
                Vector3 direction;
                if(projection==0)direction=new Vector3(u*1.2f,v*1.2f,1).normalized;
                else if(projection==3){float radius=Mathf.Sqrt(u*u+v*v);if(radius>1)continue;float angle=radius*Mathf.PI*.5f;direction=new Vector3(u/Mathf.Max(radius,.0001f)*Mathf.Sin(angle),v/Mathf.Max(radius,.0001f)*Mathf.Sin(angle),Mathf.Cos(angle));}
                else{float longitude=u*Mathf.PI*(projection==2?1:.5f),latitude=v*Mathf.PI*.5f;direction=new Vector3(Mathf.Sin(longitude)*Mathf.Cos(latitude),Mathf.Sin(latitude),Mathf.Cos(longitude)*Mathf.Cos(latitude));}
                var origin=new Vector3((eye==0?-.085f:.085f)+time*.04f,.07f*seed,0);
                float nearest=float.MaxValue;Vector3 point=default;int face=0;
                // Six enclosing planes and three boxes introduce independent depth regions.
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2){
                    float plane=sign*(axis==1?2:axis==2?5:3);float ray=direction[axis];if(Mathf.Abs(ray)<.00001f)continue;
                    float distance=(plane-origin[axis])/ray;if(distance<=0||distance>=nearest)continue;
                    nearest=distance;point=origin+direction*distance;face=axis;
                }
                for(int box=0;box<3;box++){
                    var center=new Vector3(-1.7f+box*1.55f,-.8f+box*.65f,2.1f+box*.55f);var size=new Vector3(.55f,.75f,.35f);
                    for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2){
                        float ray=direction[axis];if(Mathf.Abs(ray)<.00001f)continue;
                        float distance=(center[axis]+sign*size[axis]-origin[axis])/ray;if(distance<=0||distance>=nearest)continue;
                        Vector3 hit=origin+direction*distance;int a=(axis+1)%3,b=(axis+2)%3;
                        if(Mathf.Abs(hit[a]-center[a])>size[a]||Mathf.Abs(hit[b]-center[b])>size[b])continue;
                        nearest=distance;point=hit;face=axis;
                    }
                }
                float s=point[(face+1)%3],r=point[(face+2)%3];
                float value=Noise(s*13,r*13,seed+face*51)*.55f+Noise(s*29,r*29,seed+83)*.25f+Noise(s*4,r*4,seed+112)*.2f;
                // World-aligned strips create straight physical edges, curved only by projection.
                if(Mathf.Abs((r+.07f*seed)*1.9f-Mathf.Round((r+.07f*seed)*1.9f))<.045f)value*=.3f;
                output[y*side*2+eye*side+x]=(byte)(35+190*value);
            }
            return output;
        }
        private static float Noise(float x,float y,int seed)
        {
            int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);float fx=x-ix,fy=y-iy;
            return Mathf.Lerp(Mathf.Lerp(Hash(ix,iy,seed),Hash(ix+1,iy,seed),fx),Mathf.Lerp(Hash(ix,iy+1,seed),Hash(ix+1,iy+1,seed),fx),fy);
        }
        private static float Hash(int x,int y,int seed)
        {
            unchecked{uint value=(uint)(x*374761393+y*668265263+seed*144269);value=(value^(value>>13))*1274126177;return (value^(value>>16))/(float)uint.MaxValue;}
        }
    }
}
