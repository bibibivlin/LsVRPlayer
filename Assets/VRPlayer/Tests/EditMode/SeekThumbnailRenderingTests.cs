using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class SeekThumbnailRenderingTests
    {
        [TestCase(false)][TestCase(true)]
        public void ThumbnailCornersShareTopFrameCentersAndPreserveTheInset(bool singleEye)
        {
            var root=new GameObject("Thumbnail render regression",typeof(RectTransform),typeof(Canvas));
            var cameraObject=new GameObject("Thumbnail render camera",typeof(Camera));
            var texture=new Texture2D(4,2,TextureFormat.RGB24,false){filterMode=FilterMode.Point};
            var target=new RenderTexture(200,140,24){antiAliasing=4};Texture2D pixels=null;
            var previous=RenderTexture.active;
            try{
                root.hideFlags=HideFlags.HideAndDontSave;cameraObject.hideFlags=HideFlags.HideAndDontSave;
                var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                var canvasRect=(RectTransform)root.transform;canvasRect.sizeDelta=new Vector2(400,280);canvasRect.position=new Vector3(0,0,3);
                var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=140;camera.aspect=200f/140;
                camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=Color.red;camera.allowHDR=false;camera.stereoTargetEye=StereoTargetEyeMask.None;canvas.worldCamera=camera;
                var preview=SeekThumbnailPreview.Create(canvasRect,null,0,400,0);var rect=(RectTransform)preview.transform;
                Assert.That(rect.sizeDelta,Is.EqualTo(new Vector2(344,244)));
                for(int y=0;y<2;y++)for(int x=0;x<4;x++)texture.SetPixel(x,y,singleEye&&x<2?Color.red:Color.blue);
                texture.Apply();var image=preview.GetComponentInChildren<RoundedRawImage>(true);image.texture=texture;image.enabled=true;
                var outer=rect.GetComponent<RoundedPanelGraphic>();var picture=image.rectTransform.rect;
                Assert.That(outer.radius,Is.EqualTo(30));
                Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(new Vector2(12,-12)));
                Assert.That(image.Radius,Is.EqualTo(outer.radius-12),"Outer radius must equal inner radius plus the original inset.");
                foreach(bool right in new[]{false,true}){
                    var innerCenter=new Vector2(right?picture.xMax-image.Radius:picture.xMin+image.Radius,picture.yMax-image.Radius);
                    var actual=(Vector2)rect.InverseTransformPoint(image.rectTransform.TransformPoint(innerCenter));
                    var expected=new Vector2(right?rect.rect.xMax-outer.radius:rect.rect.xMin+outer.radius,rect.rect.yMax-outer.radius);
                    Assert.That(Vector2.Distance(actual,expected),Is.LessThan(.001f),"Top image and frame corners must be concentric.");
                }
                image.uvRect=singleEye?new Rect(.5f,0,.5f,1):new Rect(0,0,1,1);
                foreach(var label in preview.GetComponentsInChildren<TextMeshProUGUI>(true))label.gameObject.SetActive(false);
                preview.gameObject.SetActive(true);foreach(var child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                target.Create();camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                pixels=new Texture2D(200,140,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,200,140),0,0);pixels.Apply();
                string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Docs/Captures/SeekThumbnails"));Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,singleEye?"CornerRender-Eye.png":"CornerRender-Mono.png"),pixels.EncodeToPNG());
                Color Pixel(int x,int top)=>pixels.GetPixel(x,139-top);
                Assert.That(Pixel(86,51).b,Is.GreaterThan(.9f),"The requested image/eye must remain visible.");
                foreach(var point in new[]{new Vector2Int(7,7),new Vector2Int(164,7),new Vector2Int(7,94),new Vector2Int(164,94)})
                    Assert.That(Pixel(point.x,point.y).b,Is.LessThan(.6f),"A thumbnail corner must reveal its frame: "+point);
                Assert.That(Pixel(86,7).b,Is.GreaterThan(.9f),"Keep the original top inset.");
                Assert.That(Pixel(7,51).b,Is.GreaterThan(.9f),"Keep the original left inset.");
                Assert.That(Pixel(4,51).r,Is.LessThan(.6f),"Keep the visible outer frame.");
                Assert.That(Pixel(4,51).b,Is.LessThan(.6f),"The image must not replace the outer frame.");
            }finally{
                RenderTexture.active=previous;cameraObject.GetComponent<Camera>().targetTexture=null;
                Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(texture);
                if(pixels!=null)Object.DestroyImmediate(pixels);target.Release();Object.DestroyImmediate(target);
            }
        }
    }
}
