using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed class CaptionPresenter : MonoBehaviour
    {
        private TextMeshProUGUI label;
        private Image background;
        private Material backgroundMaterial;
        private Shader textShader;
        private bool overlayText;
        private readonly Dictionary<int,Material> materials=new Dictionary<int,Material>();
        private int styleHash;
        private float width,height;
        private bool initialized;
        public string Text=>label==null?"":label.text;
        public bool BackgroundVisible=>background!=null&&background.gameObject.activeSelf;
        public static float DistanceScale(float distance)=>Mathf.Clamp(distance,.1f,6)/3;
        public void Initialize(float w=1300,float h=300,bool overlay=false,bool opticalCenter=false)
        {
            width=w;height=h;
            overlayText=overlay;textShader=Shader.Find("VRPlayer/Subtitle SDF");
            background=UiFactory.Box(transform,"Subtitle background",0,0,w,h,Color.clear).GetComponent<Image>();
            if(overlay){backgroundMaterial=new Material(Shader.Find("VRPlayer/Subtitle Background"));background.material=backgroundMaterial;}
            label=UiFactory.Label(transform,"",0,0,w,h,42,TextAlignmentOptions.Center,opticalCenter);
            label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Overflow;
            label.extraPadding=true;
            var material=new Material(UiFactory.Font.material){name="Subtitle independent SDF"};
            if(textShader!=null){material.shader=textShader;material.SetFloat("_ZTest",overlayText?8:4);material.renderQueue=3000;}
            label.fontSharedMaterial=material;materials[material.mainTexture.GetInstanceID()]=material;
        }
        public void SetText(string text,float size,SubtitleStyle style)
        {
            if(label==null)return;text??="";style??=new SubtitleStyle();
            int hash=System.HashCode.Combine(style.textColor,style.outlineColor,style.outline,style.outlineWidth,style.shadow,style.shadowStrength,style.background,style.backgroundOpacity)^style.backgroundColor.GetHashCode();
            if(initialized&&label.text==text&&Mathf.Approximately(label.fontSize,size)&&hash==styleHash)return;
            initialized=true;styleHash=hash;label.text=text;label.fontSize=size;label.color=style.textColor;
            label.ForceMeshUpdate();
            // Own one material per atlas. Never mutate the menu font or TMP's shared fallback presets.
            var shared=label.fontSharedMaterials;
            for(int i=0;i<shared.Length;i++){
                if(shared[i]==null||shared[i].mainTexture==null)continue;
                int texture=shared[i].mainTexture.GetInstanceID();
                if(!materials.TryGetValue(texture,out var material)){material=new Material(shared[i]){name="Subtitle atlas SDF"};materials[texture]=material;}
                if(textShader!=null){material.shader=textShader;material.SetFloat("_ZTest",overlayText?8:4);material.renderQueue=3000;}
                material.SetFloat(ShaderUtilities.ID_OutlineWidth,style.outline?style.outlineWidth:0);
                if(style.outline)material.EnableKeyword("OUTLINE_ON");else material.DisableKeyword("OUTLINE_ON");
                material.SetColor(ShaderUtilities.ID_OutlineColor,style.outlineColor);
                if(style.shadow){material.EnableKeyword("UNDERLAY_ON");material.SetColor(ShaderUtilities.ID_UnderlayColor,new Color(0,0,0,style.shadowStrength));material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX,.3f);material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY,-.3f);material.SetFloat(ShaderUtilities.ID_UnderlaySoftness,.25f);}
                else material.DisableKeyword("UNDERLAY_ON");
                shared[i]=material;
            }
            label.fontSharedMaterials=shared;label.UpdateMeshPadding();label.ForceMeshUpdate();
            bool visible=style.background!=SubtitleBackground.None&&!string.IsNullOrWhiteSpace(text);background.gameObject.SetActive(visible);
            if(visible){
                Vector2 preferred=label.GetPreferredValues(text,width-32,0);
                float w=style.background==SubtitleBackground.FullLine?width:Mathf.Min(width,preferred.x+32),h=Mathf.Min(height,preferred.y+20);
                background.rectTransform.sizeDelta=new Vector2(w,h);background.rectTransform.anchoredPosition=new Vector2((width-w)/2,-(height-h)/2);
                Color color=style.backgroundColor;color.a=style.backgroundOpacity;background.color=color;
            }
        }
        public void Position(Camera head,Transform presenter,UserSettings settings)
        {
            float ratio=DistanceScale(settings.subtitleDistance);
            Quaternion rotation=settings.subtitleFollow?head.transform.rotation:presenter.rotation;
            // Anchor to the current head position; positional lag is particularly harmful at 10 cm.
            transform.SetPositionAndRotation(head.transform.position+rotation*new Vector3(0,settings.subtitleOffset*ratio,settings.subtitleDistance),rotation);
            transform.localScale=Vector3.one*(.0015f*ratio);
        }
        private void OnDestroy(){foreach(var material in materials.Values)if(material!=null)Destroy(material);if(backgroundMaterial!=null)Destroy(backgroundMaterial);}
    }
}
