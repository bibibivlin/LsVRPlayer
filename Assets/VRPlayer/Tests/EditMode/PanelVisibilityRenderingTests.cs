using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class PanelVisibilityRenderingTests
    {
        [TestCase("surface",false)][TestCase("image",false)][TestCase("artwork",false)][TestCase("text",false)]
        [TestCase("surface",true)][TestCase("image",true)][TestCase("artwork",true)][TestCase("text",true)]
        [TestCase("pointer",false)][TestCase("pointer",true)]
        public void PanelContentsRemainVisibleWhenMovedBehindTheVideoScreen(string content,bool videoOpened)
        {
            var cameraObject=new GameObject("Panel depth regression camera",typeof(Camera));
            var camera=cameraObject.GetComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.stereoTargetEye=StereoTargetEyeMask.None;
            var video=GameObject.CreatePrimitive(PrimitiveType.Quad);video.layer=31;video.transform.position=new Vector3(0,0,3.5f);video.transform.localScale=new Vector3(5,5,1);
            var material=new Material(Shader.Find("VRPlayer/VideoProjection"));material.mainTexture=videoOpened?Texture2D.whiteTexture:Texture2D.blackTexture;material.SetFloat("_GammaInput",0);
            video.GetComponent<Renderer>().sharedMaterial=material;
            var panel=new GameObject("World panel beyond video",typeof(RectTransform),typeof(Canvas));panel.layer=31;
            var canvas=panel.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
            var root=(RectTransform)panel.transform;root.sizeDelta=new Vector2(800,800);root.localScale=Vector3.one*.002f;
            Graphic graphic;
            if(content=="surface")graphic=UiFactory.RoundedBox(root,"Panel",0,0,800,800,Color.red).GetComponent<Graphic>();
            else if(content=="artwork")graphic=UiFactory.Rect(root,"Poster",0,0,800,800).gameObject.AddComponent<RawImage>();
            else if(content=="text")graphic=UiFactory.Label(root,"MMMM",0,0,800,800,120,TMPro.TextAlignmentOptions.Center);
            else graphic=UiFactory.Box(root,"Control",0,0,800,800,Color.red).GetComponent<Graphic>();
            graphic.gameObject.layer=31;graphic.color=Color.red;
            GameObject pointer=null;Material pointerMaterial=null;
            if(content=="pointer"){
                pointer=GameObject.CreatePrimitive(PrimitiveType.Sphere);pointer.layer=31;pointer.transform.localScale=Vector3.one*.3f;
                pointerMaterial=VrUiInput.CreatePointerMaterial();pointer.GetComponent<Renderer>().sharedMaterial=pointerMaterial;
            }
            var target=new RenderTexture(128,128,24);var pixels=new Texture2D(128,128,TextureFormat.RGB24,false);camera.targetTexture=target;
            var previous=RenderTexture.active;
            try{
                // Cross the real 3.5 m video plane, including the blank screen at startup.
                foreach(float distance in new[]{2f,3.4f,3.6f,6f}){
                    root.position=new Vector3(0,0,distance);
                    if(pointer!=null)pointer.transform.position=new Vector3(0,0,distance-.005f);
                    if(graphic is TMPro.TextMeshProUGUI label)label.ForceMeshUpdate();
                    Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();
                    int red=0;foreach(var pixel in pixels.GetPixels32())if(pixel.r>pixel.g+40&&pixel.r>pixel.b+40)red++;
                    Assert.That(red,Is.GreaterThan(20),$"{content}, videoOpened={videoOpened}, panel={distance} m must remain visible");
                    if(pointer!=null){
                        int cyan=0;foreach(var pixel in pixels.GetPixels32())if(pixel.g>pixel.r+40&&pixel.b>pixel.r+40)cyan++;
                        Assert.That(cyan,Is.GreaterThan(20),$"Controller pointer must remain visible at {distance} m");
                    }
                }
            }finally{
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                foreach(var owned in new Object[]{panel,video,cameraObject,material,target,pixels,pointer,pointerMaterial})Object.DestroyImmediate(owned);
            }
        }
    }
}
