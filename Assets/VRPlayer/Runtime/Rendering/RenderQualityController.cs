using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.PXR;

namespace VRPlayer
{
    public enum RenderQuality { Balanced, High, Ultra }
    public sealed class RenderQualityController : MonoBehaviour
    {
        private PlayerApp app;
        private Coroutine applying;
        private readonly List<XRDisplaySubsystem> displays=new List<XRDisplaySubsystem>();
        public float Fps{get;private set;}
        public float EffectiveScale=>XRSettings.eyeTextureResolutionScale;
        public string Description=>$"眼缓冲：{XRSettings.eyeTextureWidth} × {XRSettings.eyeTextureHeight} / 眼    倍率：{EffectiveScale:0.##}    MSAA：{QualitySettings.antiAliasing}×\n应用帧率：{Fps:0.0} fps    目标：72 Hz";
        public static float Scale(RenderQuality quality)=>quality==RenderQuality.Ultra?1.5f:quality==RenderQuality.High?1.25f:1;
        public void Initialize(PlayerApp owner)
        {
            app=owner;var manager=FindFirstObjectByType<PXR_Manager>();
            if(manager!=null){manager.useRecommendedAntiAliasingLevel=false;manager.adaptiveResolution=false;}
            Apply();
        }
        public void Select(RenderQuality quality){app.Store.Settings.renderQuality=quality;app.Store.Save();Apply();}
        private void Update(){float sample=1/Mathf.Max(.001f,Time.unscaledDeltaTime);Fps=Fps==0?sample:Mathf.Lerp(Fps,sample,1-Mathf.Exp(-Time.unscaledDeltaTime*2));}
        public void Apply(){if(app==null)return;if(applying!=null)StopCoroutine(applying);applying=StartCoroutine(ApplyWhenReady());}
        private IEnumerator ApplyWhenReady()
        {
            QualitySettings.antiAliasing=4;app.headCamera.allowMSAA=true;app.headCamera.allowDynamicResolution=false;
            Application.targetFrameRate=72;
            if(!Application.isEditor){float deadline=Time.realtimeSinceStartup+10;while(!XRSettings.isDeviceActive&&Time.realtimeSinceStartup<deadline)yield return null;}
            displays.Clear();SubsystemManager.GetSubsystems(displays);
            foreach(var display in displays)if(display.running)display.SetMSAALevel(4);
            XRSettings.renderViewportScale=1;
            int requested=(int)app.Store.Settings.renderQuality;
            for(int level=requested;level>=0;level--){
                float scale=Scale((RenderQuality)level);XRSettings.eyeTextureResolutionScale=scale;
                yield return null;yield return null;
                if(!XRSettings.isDeviceActive||Mathf.Abs(EffectiveScale-scale)<.02f){if(level!=requested)app.SetStatus($"当前设备已回退到渲染倍率 {EffectiveScale:0.##}");break;}
            }
            applying=null;
        }
        private void OnApplicationPause(bool paused){if(!paused)Apply();}
    }
}
