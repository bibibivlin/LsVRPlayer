using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    public enum SurfaceTreatment { Panel, Control, Inset, Transport, Frame, Navigation, Swatch }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedPanelGraphic : MaskableGraphic
    {
        public float radius=28;
        public float border=1;
        public float shadow=.18f;
        private static Material surfaceMaterial;
        private SurfaceTreatment treatment;
        private bool selected;
        private float progress;
        public SurfaceTreatment Treatment{get=>treatment;set{if(treatment==value)return;treatment=value;SetVerticesDirty();}}
        public bool Selected{get=>selected;set{if(selected==value)return;selected=value;SetVerticesDirty();}}
        public float Progress{get=>progress;set{value=Mathf.Clamp01(value);if(Mathf.Abs(progress-value)<.00001f)return;progress=value;SetVerticesDirty();}}
        public override Material defaultMaterial
        {
            get{if(surfaceMaterial==null)surfaceMaterial=Resources.Load<Material>("UI/Surface");return surfaceMaterial!=null&&surfaceMaterial.shader.isSupported?surfaceMaterial:base.defaultMaterial;}
        }
        protected override void OnEnable(){base.OnEnable();EnableChannels();}
        protected override void OnCanvasHierarchyChanged(){base.OnCanvasHierarchyChanged();EnableChannels();}
        private void EnableChannels(){if(canvas!=null)canvas.additionalShaderChannels|=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.TexCoord2;}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();Rect r=rectTransform.rect;var activeMaterial=defaultMaterial;
            if(surfaceMaterial==null||!surfaceMaterial.shader.isSupported){FallbackMesh(mesh,r);return;}
            float padding=shadow>0?22:1;
            var geometry=new Vector4(r.width,r.height,Mathf.Min(radius,Mathf.Min(r.width,r.height)*.5f),border);
            var parameters=new Vector4((float)treatment,selected?1:0,progress,shadow);
            Vector2[] corners={new Vector2(r.xMin-padding,r.yMin-padding),new Vector2(r.xMin-padding,r.yMax+padding),new Vector2(r.xMax+padding,r.yMax+padding),new Vector2(r.xMax+padding,r.yMin-padding)};
            foreach(var point in corners){var vertex=UIVertex.simpleVert;vertex.position=point;vertex.color=color;vertex.uv0=new Vector2((point.x-r.xMin)/Mathf.Max(1,r.width),(point.y-r.yMin)/Mathf.Max(1,r.height));vertex.uv1=geometry;vertex.uv2=parameters;mesh.AddVert(vertex);}
            mesh.AddTriangle(0,1,2);mesh.AddTriangle(2,3,0);
        }
        private void FallbackMesh(VertexHelper mesh,Rect r)
        {
            float rad=Mathf.Min(radius,Mathf.Min(r.width,r.height)*.5f);mesh.AddVert(r.center,color,Vector2.zero);
            for(int corner=0;corner<4;corner++){
                Vector2 center=new Vector2(corner==0||corner==3?r.xMax-rad:r.xMin+rad,corner<2?r.yMax-rad:r.yMin+rad);
                for(int step=0;step<=8;step++){float angle=(corner*90+step*90f/8)*Mathf.Deg2Rad;mesh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*rad,color,Vector2.zero);}
            }
            for(int i=1;i<=36;i++)mesh.AddTriangle(0,i,i==36?1:i+1);
        }
    }
    public enum QuickIcon { Subtitle, Volume, Muted, Previous, Play, Pause, Next, Mode, Speed, Close, Back, Up, Refresh, Star, Clock, Local, Network, Folder, Film, Flat, VR180, Globe, Fisheye, Cubemap, Eac, Sbs, Ou, Eye, Swap, Expand, Contract, Check, Trash, Search, Stop, Home, Add, Forward, Backspace, Open, Edit, ViewPoster, ViewBanner, ViewDisc, ViewLogo, ViewThumb, ViewList, ViewTable }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class QuickIconGraphic : MaskableGraphic
    {
        private QuickIcon icon;
        public QuickIcon Icon{get=>icon;set{if(icon==value)return;icon=value;SetVerticesDirty();}}
        private VertexHelper mesh;
        private Vector2 Point(float x,float y){Rect r=rectTransform.rect;return new Vector2(r.xMin+x*r.width/32,r.yMin+y*r.height/32);}
        private void Triangle(Vector2 a,Vector2 b,Vector2 c){int n=mesh.currentVertCount;mesh.AddVert(a,color,Vector2.zero);mesh.AddVert(b,color,Vector2.zero);mesh.AddVert(c,color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);}
        private void Line(float x,float y,float xx,float yy,float width=2.5f)
        {
            Vector2 a=Point(x,y),b=Point(xx,yy);Vector2 side=new Vector2(-(b-a).y,(b-a).x).normalized*width*rectTransform.rect.width/64;
            Triangle(a-side,a+side,b+side);Triangle(a-side,b+side,b-side);
        }
        private void Rectangle(float left,float bottom,float right,float top){Line(left,bottom,right,bottom,1.8f);Line(right,bottom,right,top,1.8f);Line(right,top,left,top,1.8f);Line(left,top,left,bottom,1.8f);}
        private void Ring(float x,float y,float radius,float start=0,float end=360)
        {
            for(int i=0;i<24;i++){float a=Mathf.Lerp(start,end,i/24f)*Mathf.Deg2Rad,b=Mathf.Lerp(start,end,(i+1)/24f)*Mathf.Deg2Rad;Line(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius,2);}
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();mesh=vh;
            switch(icon){
                case QuickIcon.ViewPoster:Rectangle(8,3,24,29);break;
                case QuickIcon.ViewBanner:Rectangle(3,10,29,22);break;
                case QuickIcon.ViewDisc:Ring(16,16,13);Ring(16,16,3);break;
                case QuickIcon.ViewLogo:Line(6,25,26,25);Line(16,25,16,6);Line(11,6,21,6);break;
                case QuickIcon.ViewThumb:Rectangle(3,7,29,25);Ring(10,18,2);Line(6,11,26,11,1.5f);break;
                case QuickIcon.ViewList:Line(4,24,28,24);Line(4,16,28,16);Line(4,8,28,8);break;
                case QuickIcon.ViewTable:Rectangle(3,5,29,27);Line(16,5,16,27);Line(3,16,29,16);break;
                case QuickIcon.Search:Ring(13,19,9);Line(20,12,29,3);break;
                case QuickIcon.Stop:Rectangle(6,6,26,26);break;
                case QuickIcon.Home:Line(3,17,16,29);Line(16,29,29,17);Line(7,19,7,4);Line(7,4,25,4);Line(25,4,25,19);Rectangle(13,4,19,14);break;
                case QuickIcon.Add:Line(16,4,16,28);Line(4,16,28,16);break;
                case QuickIcon.Forward:Line(9,16,25,16);Line(25,16,17,24);Line(25,16,17,8);break;
                case QuickIcon.Backspace:Line(2,16,10,26);Line(10,26,29,26);Line(29,26,29,6);Line(29,6,10,6);Line(10,6,2,16);Line(15,11,24,21,1.8f);Line(15,21,24,11,1.8f);break;
                case QuickIcon.Open:Line(6,21,6,5);Line(6,5,27,5);Line(27,5,27,16);Line(6,21,12,21);Line(14,26,28,26);Line(28,26,28,20);Line(28,26,15,13);break;
                case QuickIcon.Edit:Line(5,5,7,13,1.8f);Line(7,13,23,29,1.8f);Line(23,29,29,23,1.8f);Line(29,23,13,7,1.8f);Line(13,7,5,5,1.8f);Line(20,26,26,20,1.8f);break;
                case QuickIcon.Back:Line(23,16,7,16);Line(7,16,15,24);Line(7,16,15,8);break;
                case QuickIcon.Up:Line(16,5,16,27);Line(16,27,7,18);Line(16,27,25,18);break;
                case QuickIcon.Refresh:Ring(16,16,11,45,340);Line(24,26,28,21);Line(28,21,21,21);break;
                case QuickIcon.Star:
                    for(int i=0;i<10;i++){float a=(90+i*36)*Mathf.Deg2Rad,b=(90+(i+1)*36)*Mathf.Deg2Rad;float ra=i%2==0?13:6,rb=i%2==0?6:13;Line(16+Mathf.Cos(a)*ra,16+Mathf.Sin(a)*ra,16+Mathf.Cos(b)*rb,16+Mathf.Sin(b)*rb,1.8f);}break;
                case QuickIcon.Clock:Ring(16,16,13);Line(16,16,16,25);Line(16,16,23,12);break;
                case QuickIcon.Local:Line(4,5,28,5);Line(28,5,25,27);Line(25,27,7,27);Line(7,27,4,5);Line(5,13,27,13);Line(9,9,12,9,1.5f);break;
                case QuickIcon.Network:Line(16,23,16,14);Line(6,14,26,14);Line(6,14,6,9);Line(26,14,26,9);Rectangle(11,23,21,30);Rectangle(1,1,11,9);Rectangle(21,1,31,9);break;
                case QuickIcon.Folder:Line(3,6,29,6);Line(29,6,29,23);Line(29,23,16,23);Line(16,23,13,27);Line(13,27,3,27);Line(3,27,3,6);Line(3,22,16,22);break;
                case QuickIcon.Film:Rectangle(4,3,28,29);Line(10,3,10,29,1.5f);Line(22,3,22,29,1.5f);for(int j=0;j<4;j++){Line(4,7+j*6,10,7+j*6,1.5f);Line(22,7+j*6,28,7+j*6,1.5f);}Triangle(Point(13,10),Point(13,22),Point(21,16));break;
                case QuickIcon.Flat:Rectangle(3,6,29,26);break;
                case QuickIcon.VR180:Rectangle(3,8,29,24);Line(12,8,16,12);Line(16,12,20,8);break;
                case QuickIcon.Globe:Ring(16,16,13);Line(3,16,29,16,1.5f);Line(7,23,25,23,1.5f);Line(7,9,25,9,1.5f);Line(13,3,9,16,1.5f);Line(9,16,13,29,1.5f);Line(19,3,23,16,1.5f);Line(23,16,19,29,1.5f);break;
                case QuickIcon.Fisheye:Ring(16,16,13);Ring(16,16,4);break;
                case QuickIcon.Cubemap:Rectangle(3,5,29,27);Line(16,5,16,27);Line(3,16,29,16);break;
                case QuickIcon.Eac:Line(3,5,3,27);Line(29,5,29,27);Line(3,27,16,24);Line(16,24,29,27);Line(3,5,16,8);Line(16,8,29,5);Line(16,8,16,24,1.5f);break;
                case QuickIcon.Sbs:Rectangle(3,6,14,26);Rectangle(18,6,29,26);break;
                case QuickIcon.Ou:Rectangle(4,4,28,14);Rectangle(4,18,28,28);break;
                case QuickIcon.Eye:Line(2,16,9,25);Line(9,25,23,25);Line(23,25,30,16);Line(30,16,23,7);Line(23,7,9,7);Line(9,7,2,16);Ring(16,16,5);break;
                case QuickIcon.Swap:Line(3,11,29,11);Line(3,11,9,5);Line(3,11,9,17);Line(29,23,3,23);Line(29,23,23,17);Line(29,23,23,29);break;
                case QuickIcon.Expand:Line(3,3,11,3);Line(3,3,3,11);Line(29,29,21,29);Line(29,29,29,21);Line(3,29,11,29);Line(3,29,3,21);Line(29,3,21,3);Line(29,3,29,11);break;
                case QuickIcon.Contract:Line(11,11,3,11);Line(11,11,11,3);Line(21,21,29,21);Line(21,21,21,29);Line(11,21,3,21);Line(11,21,11,29);Line(21,11,29,11);Line(21,11,21,3);break;
                case QuickIcon.Check:Line(5,16,12,8,3);Line(12,8,28,26,3);break;
                case QuickIcon.Trash:Rectangle(7,3,25,25);Line(4,25,28,25);Line(12,29,20,29);Line(13,8,13,21,1.5f);Line(19,8,19,21,1.5f);break;
                case QuickIcon.Close:Line(7,7,25,25,3);Line(7,25,25,7,3);break;
                case QuickIcon.Play:Triangle(Point(9,3),Point(9,29),Point(29,16));break;
                case QuickIcon.Pause:Line(10,4,10,28,6);Line(23,4,23,28,6);break;
                case QuickIcon.Previous:Line(5,5,5,27,3);Triangle(Point(26,5),Point(26,27),Point(9,16));break;
                case QuickIcon.Next:Line(27,5,27,27,3);Triangle(Point(6,5),Point(6,27),Point(23,16));break;
                case QuickIcon.Volume:
                case QuickIcon.Muted:
                    Triangle(Point(4,11),Point(4,21),Point(15,21));Triangle(Point(4,11),Point(15,21),Point(15,11));
                    Triangle(Point(10,11),Point(20,4),Point(20,28));Triangle(Point(10,11),Point(20,28),Point(10,21));
                    if(icon==QuickIcon.Volume){Ring(18,16,8,-65,65);Ring(18,16,13,-65,65);}else{Line(24,11,31,21,2);Line(24,21,31,11,2);}break;
                case QuickIcon.Subtitle:
                    Ring(16,16,14);Line(9,22,23,22,2);Line(9,16,23,16,2);Line(9,10,18,10,2);break;
                case QuickIcon.Mode:
                    Line(16,30,29,23);Line(29,23,29,8);Line(29,8,16,1);Line(16,1,3,8);Line(3,8,3,23);Line(3,23,16,30);
                    Line(3,23,16,16);Line(29,23,16,16);Line(16,16,16,1);break;
                case QuickIcon.Speed:
                    Ring(16,11,13,0,180);Line(3,11,29,11,2);
                    for(int i=0;i<5;i++){float a=i*45*Mathf.Deg2Rad;Line(16+Mathf.Cos(a)*10,11+Mathf.Sin(a)*10,16+Mathf.Cos(a)*12,11+Mathf.Sin(a)*12,1.5f);}
                    Line(16,11,24,22,2.5f);Ring(16,11,2);break;
            }
        }
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuHoverOutlineGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();Rect rect=rectTransform.rect;float radius=Mathf.Min(12,Mathf.Min(rect.width,rect.height)*.5f),inner=Mathf.Max(0,radius-1.5f);
            const int steps=6;int points=4*(steps+1);
            for(int corner=0;corner<4;corner++){
                var center=new Vector2(corner==0||corner==3?rect.xMax-radius:rect.xMin+radius,corner<2?rect.yMax-radius:rect.yMin+radius);
                for(int step=0;step<=steps;step++){
                    float angle=(corner*90+step*90f/steps)*Mathf.Deg2Rad;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                    mesh.AddVert(center+direction*radius,color,Vector2.zero);mesh.AddVert(center+direction*inner,color,Vector2.zero);
                }
            }
            for(int i=0;i<points;i++){
                int current=i*2,next=((i+1)%points)*2;mesh.AddTriangle(current,next,next+1);mesh.AddTriangle(current,next+1,current+1);
            }
        }
    }
    public sealed class QuickPointerRegion : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        private readonly HashSet<int> pointers=new HashSet<int>();
        public bool Hovered=>pointers.Count>0;
        public void OnPointerEnter(PointerEventData data)=>pointers.Add(data.pointerId);
        public void OnPointerExit(PointerEventData data)=>pointers.Remove(data.pointerId);
        public void Clear()=>pointers.Clear();
        private void OnDisable()=>Clear();
    }
    public sealed class QuickHoverLabel : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        public Action<int,bool> Change;
        public void OnPointerEnter(PointerEventData data)=>Change?.Invoke(data.pointerId,true);
        public void OnPointerExit(PointerEventData data)=>Change?.Invoke(data.pointerId,false);
    }
    public static class QuickUiFactory
    {
        public static readonly Color Background=UiTheme.Background,Tile=UiTheme.Surface,Blue=UiTheme.Accent;
        public static RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color,bool hit=true,float radius=24)
        {
            var rect=UiFactory.Rect(parent,name,x,y,w,h);var image=rect.gameObject.AddComponent<RoundedPanelGraphic>();image.color=color;image.raycastTarget=hit;image.radius=radius;image.Treatment=SurfaceTreatment.Control;image.shadow=.18f;return rect;
        }
        public static Button Button(Transform parent,string text,float x,float y,float w,float h,Action action,bool selected=false)
        {
            var rect=Box(parent,text,x,y,w,h,Tile);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Graphic>();
            UiFactory.SetColorsImmediate(button,UiTheme.ButtonColors());
            UiFactory.Label(rect,text,8,0,w-16,h,24,TextAlignmentOptions.Center);button.onClick.AddListener(()=>action?.Invoke());Select(button,selected);return button;
        }
        public static Button IconButton(Transform parent,string name,QuickIcon icon,float x,float y,Action action)
        {
            var rect=Box(parent,name,x,y,88,88,Color.clear,true,44);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Graphic>();
            button.targetGraphic.color=Color.white;var colors=button.colors;colors.normalColor=Color.clear;colors.highlightedColor=UiTheme.Elevated;colors.pressedColor=Blue;colors.selectedColor=Color.clear;colors.disabledColor=Color.clear;UiFactory.SetColorsImmediate(button,colors);
            var graphic=UiFactory.Rect(rect,"Icon",23,23,42,42).gameObject.AddComponent<QuickIconGraphic>();graphic.Icon=icon;graphic.raycastTarget=false;
            button.onClick.AddListener(()=>action?.Invoke());return button;
        }
        public static void Select(Button button,bool selected)
        {
            button.targetGraphic.color=selected?UiTheme.Selected:Tile;
            var text=button.GetComponentInChildren<TextMeshProUGUI>();if(button.targetGraphic is RoundedPanelGraphic panel)panel.Selected=selected;
            if(text!=null)text.color=selected?UiTheme.Ink:UiTheme.Text;
        }
        public static OwnedSlider Slider(Transform parent,string name,float x,float y,float width,float min,float max,float value,Action<float> changed,bool thumb=false)
        {
            var rect=Box(parent,name,x,y,width,62,UiTheme.Surface,true,16);
            var slider=rect.gameObject.AddComponent<OwnedSlider>();rect.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var area=UiFactory.Rect(rect,"Fill Area",0,0,width,62);var fill=UiFactory.Box(area,"Fill",0,0,width,62,UiTheme.Accent);
            fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;slider.fillRect=fill;
            slider.targetGraphic=rect.GetComponent<Graphic>();
            if(thumb){
                fill.gameObject.SetActive(false);slider.fillRect=null;
                var handleArea=UiFactory.Rect(rect,"Handle area",38,0,width-76,62);
                var handle=Box(handleArea,"Handle",0,0,76,62,UiTheme.Selected,false,12);handle.pivot=new Vector2(.5f,.5f);handle.anchorMin=handle.anchorMax=new Vector2(0,.5f);handle.anchoredPosition=Vector2.zero;
                slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Graphic>();
            }
            slider.minValue=min;slider.maxValue=max;slider.SetValueWithoutNotify(value);slider.onValueChanged.AddListener(v=>changed?.Invoke(v));return slider;
        }
    }
}
