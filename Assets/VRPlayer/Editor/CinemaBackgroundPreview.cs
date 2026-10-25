using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VRPlayer.Editor
{
    public static class CinemaBackgroundPreview
    {
        private static void Set(PlayerApp app,string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);

        [MenuItem("VRPlayer/Validation/Capture Cinema Backgrounds")]
        public static void Capture()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before capturing fixtures.");
            string folder=Path.GetFullPath("Docs/Captures/CinemaBackground");Directory.CreateDirectory(folder);
            var previousLanguage=Localization.Preference;
            var owner=new GameObject("Background UI fixture"){hideFlags=HideFlags.HideAndDontSave};owner.SetActive(false);
            var app=owner.AddComponent<PlayerApp>();PlayerUI ui=null;
            var head=new GameObject("Background capture camera",typeof(Camera)){hideFlags=HideFlags.HideAndDontSave};
            var camera=head.GetComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.farClipPlane=100;
            var target=new RenderTexture(1440,1040,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=4};target.Create();camera.targetTexture=target;
            var backdrop=new GameObject("Background render fixture"){hideFlags=HideFlags.HideAndDontSave,layer=31};var background=backdrop.AddComponent<CinemaBackground>();background.Initialize(camera);
            var screen=GameObject.CreatePrimitive(PrimitiveType.Quad);screen.hideFlags=HideFlags.HideAndDontSave;screen.layer=31;screen.transform.position=new Vector3(0,0,3.5f);screen.transform.localScale=new Vector3(4.5f,4.5f*9/16,1);
            var videoMaterial=new Material(Shader.Find("VRPlayer/VideoProjection"));videoMaterial.SetFloat("_GammaInput",0);screen.GetComponent<Renderer>().sharedMaterial=videoMaterial;
            var image=new Texture2D(320,180,TextureFormat.RGB24,false);var colors=new Color[320*180];
            for(int y=0;y<180;y++)for(int x=0;x<320;x++)colors[y*320+x]=x>195&&x<240&&y>65&&y<110?Color.white:y<28&&y>23&&x>90&&x<230?new Color(.7f,.7f,.7f):new Color(.025f,.028f,.035f);
            image.SetPixels(colors);image.Apply();videoMaterial.mainTexture=image;
            var overflow=new System.Collections.Generic.List<string>();
            void Save(string name)
            {
                if(ui!=null&&ui.Visible)
                {
                    foreach(var item in ui.Canvas.GetComponentsInChildren<Transform>(true))item.gameObject.layer=31;
                    Canvas.ForceUpdateCanvases();
                    foreach(var label in ui.Canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
                    {
                        label.ForceMeshUpdate();
                        if(label.isTextTruncated)overflow.Add(name+": "+label.text);
                    }
                }
                camera.Render();var previous=RenderTexture.active;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
                finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
            }
            try
            {
                var view=new ViewSettings{heightOffset=0};camera.fieldOfView=90;
                foreach(CinemaBackgroundMode mode in Enum.GetValues(typeof(CinemaBackgroundMode)))
                {
                    background.Apply(new CinemaBackgroundSettings{mode=mode,grayLevel=24},view,16f/9);Save(mode.ToString());
                }
                backdrop.SetActive(false);screen.SetActive(false);camera.orthographic=true;camera.orthographicSize=.86f;
                var store=new PlayerStore(Path.Combine(folder,"fixture-settings.json"));store.Settings.background.mode=CinemaBackgroundMode.SoftSurround;
                Set(app,"Store",store);Set(app,"Backend",new AndroidMediaBackend());Set(app,"Presenter",owner.AddComponent<VideoPresenter>());Set(app,"RenderQuality",owner.AddComponent<RenderQualityController>());
                app.headCamera=camera;ui=owner.AddComponent<PlayerUI>();Set(app,"UI",ui);ui.Initialize(app,camera);
                foreach(var language in new[]{AppLanguage.Chinese,AppLanguage.English,AppLanguage.Japanese})
                {
                    app.SetLanguage(language);ui.SelectTab("设置");Save("Settings-"+language);
                    ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Background settings").onClick.Invoke();Save("Background-"+language);
                    ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Cinema comfort settings").onClick.Invoke();Save("Comfort-"+language);
                }
                File.WriteAllLines(Path.Combine(folder,"truncated-labels.txt"),overflow);
                Debug.Log("Cinema background fixtures captured; truncated labels: "+overflow.Count);
            }
            finally
            {
                if(ui!=null)
                {
                    var quick=ui.Quick.Canvas;typeof(QuickControlsUI).GetProperty("Canvas").SetValue(ui.Quick,null);Object.DestroyImmediate(quick.gameObject);
                    var canvas=ui.Canvas;typeof(PlayerUI).GetProperty("Canvas").SetValue(ui,null);Object.DestroyImmediate(canvas.gameObject);
                    Object.DestroyImmediate(Field<RectTransform>(ui,"transportRoot").gameObject);
                }
                Set(app,"Presenter",null);Object.DestroyImmediate(owner);Object.DestroyImmediate(backdrop);Object.DestroyImmediate(screen);
                camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(head);Object.DestroyImmediate(videoMaterial);Object.DestroyImmediate(image);
                Localization.SetLanguage(previousLanguage);
            }
        }
    }
}
