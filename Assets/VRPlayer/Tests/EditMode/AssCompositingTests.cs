using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class AssCompositingTests
    {
        [TestCase(0,0)][TestCase(1,0)][TestCase(1,1)][TestCase(2,0)][TestCase(2,1)]
        public void OverlayUsesWholeScreenCoordinatesForBothStereoEyes(int stereo,int eye)
        {
            var overlay=new Texture2D(4,4,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point};
            var pixels=new Color32[16];for(int y=2;y<4;y++)for(int x=0;x<2;x++)pixels[y*4+x]=new Color32(255,0,0,255);
            overlay.SetPixels32(pixels);overlay.Apply();
            var material=new Material(Shader.Find("VRPlayer/VideoProjection"));
            var target=RenderTexture.GetTemporary(8,8,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var image=new Texture2D(8,8,TextureFormat.RGBA32,false,true);var previous=RenderTexture.active;
            float oldEye=Shader.GetGlobalFloat("_VRPlayerEye");
            try{
                Shader.SetGlobalFloat("_VRPlayerEye",eye);material.SetInt("_Stereo",stereo);material.SetFloat("_AssEnabled",1);material.SetTexture("_AssTex",overlay);
                Graphics.Blit(Texture2D.blackTexture,target,material);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,8,8),0,0);image.Apply();
                Assert.That(image.GetPixel(1,6).r,Is.GreaterThan(.9));Assert.That(image.GetPixel(6,6).r,Is.LessThan(.05));Assert.That(image.GetPixel(1,1).r,Is.LessThan(.05));
                material.SetInt("_Projection",2);Graphics.Blit(Texture2D.blackTexture,target,material);image.ReadPixels(new Rect(0,0,8,8),0,0);image.Apply();
                Assert.That(image.GetPixel(1,6).r,Is.LessThan(.05),"Immersive shader must reject ASS independently of the UI");
            }finally{Shader.SetGlobalFloat("_VRPlayerEye",oldEye);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(material);Object.DestroyImmediate(overlay);Object.DestroyImmediate(image);}
        }
    }
}
