using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class PanoramaZoomRenderingTests
    {
        private GameObject sphere,head;private Camera camera;private Material material;private RenderTexture target;private Texture2D texture,readback;
        [SetUp] public void Setup()
        {
            sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.layer=31;UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());sphere.transform.localScale=Vector3.one*60;
            material=new Material(Shader.Find("VRPlayer/VideoProjection"));sphere.GetComponent<Renderer>().sharedMaterial=material;material.SetFloat("_GammaInput",0);material.SetVector("_PanoramaForward",Vector3.forward);
            head=new GameObject("Render test camera",typeof(Camera));camera=head.GetComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;camera.fieldOfView=90;camera.aspect=1;camera.farClipPlane=100;camera.stereoTargetEye=StereoTargetEyeMask.None;
            target=new RenderTexture(96,96,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;readback=new Texture2D(96,96,TextureFormat.RGB24,false);
            texture=new Texture2D(512,256,TextureFormat.RGB24,false){wrapMode=TextureWrapMode.Clamp};material.mainTexture=texture;
        }
        private Color Render(int x=48,int y=48)
        {
            camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            try{readback.ReadPixels(new Rect(0,0,96,96),0,0);readback.Apply();return readback.GetPixel(x,y);}finally{RenderTexture.active=previous;}
        }
        [TearDown] public void Cleanup()
        {
            Shader.SetGlobalFloat("_VRPlayerEye",0);camera.targetTexture=null;target.Release();
            foreach(var item in new Object[]{sphere,head,material,target,texture,readback})UnityEngine.Object.DestroyImmediate(item);
        }
        [Test] public void AllProjectionAndStereoLayoutsKeepEyesSeparateAtEveryZoom()
        {
            for(int stereo=0;stereo<3;stereo++){
                var pixels=new Color[512*256];for(int y=0;y<256;y++)for(int x=0;x<512;x++)pixels[y*512+x]=stereo==0?Color.blue:(stereo==1?x<256:y>=128)?Color.red:Color.green;
                texture.SetPixels(pixels);texture.Apply();material.SetInt("_Stereo",stereo);
                for(int projection=0;projection<6;projection++)foreach(float zoom in new[]{.5f,1f,2f})for(int eye=0;eye<2;eye++){
                    material.SetInt("_Projection",projection);material.SetFloat("_PanoramaZoom",zoom);Shader.SetGlobalFloat("_VRPlayerEye",eye);
                    Color expected=stereo==0?Color.blue:eye==0?Color.red:Color.green,actual=Render();
                    Assert.That(Vector3.Distance(new Vector3(actual.r,actual.g,actual.b),new Vector3(expected.r,expected.g,expected.b)),Is.LessThan(.03f),$"projection={projection} stereo={stereo} zoom={zoom} eye={eye}");
                }
            }
        }
        [Test] public void PanoramaZoomMagnifiesAroundFixedVideoForwardAndOneIsIdentity()
        {
            var pixels=new Color[512*256];for(int y=0;y<256;y++)for(int x=0;x<512;x++)pixels[y*512+x]=new Color(x/511f,y/255f,0);
            texture.SetPixels(pixels);texture.Apply();material.SetInt("_Projection",2);
            foreach(float angle in new[]{0f,45f,-45f}){
                camera.transform.rotation=Quaternion.Euler(0,angle,0);
                material.SetFloat("_PanoramaZoom",1);float center=.5f,original=Render(64).r;
                material.SetFloat("_PanoramaZoom",2);float enlarged=Render(64).r;
                material.SetFloat("_PanoramaZoom",.5f);float reduced=Render(64).r;
                // Compare wrapped longitude distances around the fixed video forward.
                float Delta(float value){float d=Mathf.Abs(value-center);return Mathf.Min(d,1-d);}
                Assert.That(Delta(enlarged),Is.LessThan(Delta(original)));Assert.That(Delta(reduced),Is.GreaterThan(Delta(original)));
                material.SetFloat("_PanoramaZoom",1);Assert.That(Render(64).r,Is.EqualTo(original).Within(.001f));
            }
        }
        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
        public void HeadTurnsLeaveTheZoomedWorldImageFixedForBothEyes(int projection)
        {
            var pixels=new Color[512*256];for(int y=0;y<256;y++)for(int x=0;x<512;x++)pixels[y*512+x]=new Color(x/511f,y/255f,0);
            texture.SetPixels(pixels);texture.Apply();
            var owner=new GameObject("Fixed panorama presenter");var trackedHead=new GameObject("Tracked head");var screen=new GameObject("Test screen",typeof(MeshFilter));
            var presenter=owner.AddComponent<VideoPresenter>();
            const System.Reflection.BindingFlags fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            void Set(string name,object value)=>typeof(VideoPresenter).GetField(name,fields).SetValue(presenter,value);
            Set("cameraTransform",trackedHead.transform);Set("material",material);Set("screen",screen.transform);Set("sphere",sphere.transform);
            presenter.Settings.projection=(Projection)projection;presenter.Settings.stereo=StereoLayout.SideBySide;
            try{
                foreach(float zoom in new[]{.5f,1f,2f})for(int eye=0;eye<2;eye++){
                    presenter.Settings.panoramaZoom=zoom;Shader.SetGlobalFloat("_VRPlayerEye",eye);trackedHead.transform.rotation=Quaternion.identity;
                    presenter.UpdateVideo(texture,false,512,256,1);Color original=Render(64,58);
                    foreach(var rotation in new[]{new Vector3(0,60,0),new Vector3(35,-45,15),new Vector3(-70,170,0)}){
                        trackedHead.transform.rotation=Quaternion.Euler(rotation);presenter.UpdateVideo(texture,false,512,256,1);
                        Color turned=Render(64,58);Assert.That(turned.r,Is.EqualTo(original.r).Within(.005f));Assert.That(turned.g,Is.EqualTo(original.g).Within(.005f));
                        Assert.That(owner.transform.rotation,Is.EqualTo(Quaternion.identity));
                    }
                }
            }finally{
                Set("material",null);var mesh=typeof(VideoPresenter).GetField("screenMesh",fields).GetValue(presenter) as Mesh;Set("screenMesh",null);
                UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(screen);UnityEngine.Object.DestroyImmediate(trackedHead);UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }
}
