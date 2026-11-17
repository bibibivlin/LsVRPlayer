using UnityEngine;

namespace VRPlayer
{
    public sealed class VideoPresenter : MonoBehaviour
    {
        private Transform cameraTransform,screen,sphere;
        private Material material;
        private Mesh screenMesh;
        private Camera viewCamera;
        private int excludedSubtitleLayer;
        private VideoCaptionCanvas captionCanvas;
        private Quaternion viewingRotation=Quaternion.identity;
        private string geometryKey;
        private CinemaBackground background;
        public CinemaBackgroundSettings BackgroundSettings{get;set;}=new CinemaBackgroundSettings();
        public CinemaComfortSettings ComfortSettings{get;set;}=new CinemaComfortSettings();
        public bool BackgroundVisible=>background!=null&&background.Visible;
        public ViewSettings Settings{get;set;}=new ViewSettings();
        public void ClearSubtitleOverlay()
        {
            if(material==null)return;
            material.SetInt("_SubtitleMode",0);
            material.SetTexture("_SubtitleTex",null);
        }
        public void UpdateSubtitles(string text, Texture ass, FlatSubtitleSettings settings, float aspect)
        {
            if (material == null) return;
            if (Settings.projection != Projection.Flat || (ass == null && string.IsNullOrWhiteSpace(text)))
            {
                ClearSubtitleOverlay();
                return;
            }

            if (ass != null)
            {
                float inverseScale = 1 / settings.assScale;
                // Scale around the horizontal center and lower edge of the video.
                material.SetVector("_SubtitleUV", new Vector4(inverseScale, inverseScale, .5f * (1 - inverseScale), -settings.assOffset * inverseScale));
                material.SetTexture("_SubtitleTex", ass);
                material.SetInt("_SubtitleMode", 2);
                return;
            }

            if (captionCanvas == null)
            {
                var rasterizer = new GameObject("Video subtitle rasterizer");
                rasterizer.transform.SetParent(transform, false);
                captionCanvas = rasterizer.AddComponent<VideoCaptionCanvas>();
                captionCanvas.Initialize();
            }
            Texture caption = captionCanvas.Render(text, settings, AssPresentation.FrameSize(aspect));
            material.SetVector("_SubtitleUV", new Vector4(1, 1, 0, 0));
            material.SetTexture("_SubtitleTex", caption);
            material.SetInt("_SubtitleMode", caption != null ? 1 : 0);
        }
        public void Initialize(Camera camera)
        {
            viewCamera=camera;
            excludedSubtitleLayer=camera.cullingMask&(1<<VideoCaptionCanvas.Layer);
            camera.cullingMask&=~excludedSubtitleLayer;
            cameraTransform=camera.transform;if(camera.GetComponent<VideoEyeSelector>()==null)camera.gameObject.AddComponent<VideoEyeSelector>();
            material=new Material(Shader.Find("VRPlayer/VideoProjection")){name="Video projection material"};
            var quad=new GameObject("Cinema Screen",typeof(MeshFilter),typeof(MeshRenderer));quad.transform.SetParent(transform,false);quad.GetComponent<MeshRenderer>().sharedMaterial=material;screen=quad.transform;
            var globe=GameObject.CreatePrimitive(PrimitiveType.Sphere);globe.name="Immersive Video";globe.transform.SetParent(transform,false);Release(globe.GetComponent<Collider>());globe.GetComponent<MeshRenderer>().sharedMaterial=material;sphere=globe.transform;sphere.localScale=Vector3.one*60;
            viewingRotation=Quaternion.Euler(0,cameraTransform.eulerAngles.y,0);
            transform.SetPositionAndRotation(cameraTransform.position,viewingRotation);
            var backdrop=new GameObject("Cinema Background");backdrop.transform.SetParent(transform,false);
            background=backdrop.AddComponent<CinemaBackground>();background.Initialize(camera);
            sphere.gameObject.SetActive(false);
        }
        public void UpdateVideo(Texture texture,bool gamma,int width,int height,float pixelRatio)
        {
            if(material==null)return;
            transform.position=cameraTransform.position;transform.rotation=viewingRotation;
            background?.Apply(BackgroundSettings,Settings,Settings.Aspect(width,height,pixelRatio));
            bool flat=Settings.projection==Projection.Flat;screen.gameObject.SetActive(flat);sphere.gameObject.SetActive(!flat);
            screen.localPosition=new Vector3(0,Settings.heightOffset,Settings.distance);
            string key=$"{Settings.width:F3}/{Settings.curvature:F3}/{Settings.Aspect(width,height,pixelRatio):F3}";
            if(key!=geometryKey){geometryKey=key;RebuildScreen(Settings.width,Settings.width/Settings.Aspect(width,height,pixelRatio),Settings.curvature);}
            if(texture!=null)material.mainTexture=texture;
            material.SetInt("_Projection",(int)Settings.projection);material.SetInt("_Stereo",(int)Settings.stereo);material.SetFloat("_SwapEyes",Settings.swapEyes?1:0);material.SetFloat("_MonoEye",Settings.monoEye?1:0);material.SetFloat("_GammaInput",gamma?1:0);material.SetFloat("_FisheyeFov",Settings.fisheyeFov);material.SetFloat("_Brightness",Settings.brightness);
            material.SetFloat("_PanoramaZoom",PlayerStore.NormalizeZoom(Settings.panoramaZoom));
            material.SetVector("_ComfortHighlights",CinemaComfortSettings.HighlightParameters(flat?ComfortSettings.highlights:HighlightCompression.Off));
            material.SetFloat("_SubtitleBrightness",flat?CinemaComfortSettings.NormalizeSubtitleBrightness(ComfortSettings.subtitleBrightness):1);
            // Keep the angular mapping fixed in the video's frame as the viewer turns.
            material.SetVector("_PanoramaForward",Vector3.forward);
        }
        private void RebuildScreen(float width,float height,float curve)
        {
            const int segments=64;var vertices=new Vector3[(segments+1)*2];var uvs=new Vector2[vertices.Length];var triangles=new int[segments*6];
            for(int i=0;i<=segments;i++){float u=i/(float)segments;float x=(u-.5f)*width,z=0;if(curve>.01f){float radius=width/Mathf.Deg2Rad/curve;float angle=(u-.5f)*curve*Mathf.Deg2Rad;x=Mathf.Sin(angle)*radius;z=(Mathf.Cos(angle)-1)*radius;}
                vertices[i*2]=new Vector3(x,-height*.5f,z);vertices[i*2+1]=new Vector3(x,height*.5f,z);uvs[i*2]=new Vector2(u,0);uvs[i*2+1]=new Vector2(u,1);
                if(i<segments){int t=i*6,a=i*2;triangles[t]=a;triangles[t+1]=a+1;triangles[t+2]=a+2;triangles[t+3]=a+2;triangles[t+4]=a+1;triangles[t+5]=a+3;}}
            Release(screenMesh);screenMesh=new Mesh{name="Curved cinema screen",vertices=vertices,uv=uvs,triangles=triangles};screenMesh.RecalculateBounds();screen.GetComponent<MeshFilter>().sharedMesh=screenMesh;
        }
        private void OnDestroy()
        {
            if(viewCamera!=null)viewCamera.cullingMask|=excludedSubtitleLayer;
            Release(material);Release(screenMesh);
        }
        private static void Release(Object value)
        {
            if(value==null)return;
            if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
        }
    }
    public sealed class VideoEyeSelector : MonoBehaviour
    {
        private Camera source;
        private void Awake()=>source=GetComponent<Camera>();
        private void OnPreRender()=>Shader.SetGlobalFloat("_VRPlayerEye",source.stereoActiveEye==Camera.MonoOrStereoscopicEye.Right?1:0);
    }
}
