using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    // Geometry-based icons and rounded panels use the normal uGUI stencil material.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedPanelGraphic : MaskableGraphic
    {
        public float radius=28;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Rect r=rectTransform.rect;float rad=Mathf.Min(radius,Mathf.Min(r.width,r.height)*.5f);
            vh.AddVert(r.center,color,Vector2.zero);
            for(int corner=0;corner<4;corner++){
                Vector2 center=new Vector2(corner==0||corner==3?r.xMax-rad:r.xMin+rad,corner<2?r.yMax-rad:r.yMin+rad);
                for(int step=0;step<=8;step++){
                    float angle=(corner*90+step*90f/8)*Mathf.Deg2Rad;
                    vh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*rad,color,Vector2.zero);
                }
            }
            for(int i=1;i<=36;i++)vh.AddTriangle(0,i,i==36?1:i+1);
        }
    }
    public enum QuickIcon { Subtitle, Volume, Muted, Previous, Play, Pause, Next, Mode, Speed }
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
        private void Ring(float x,float y,float radius,float start=0,float end=360)
        {
            for(int i=0;i<24;i++){float a=Mathf.Lerp(start,end,i/24f)*Mathf.Deg2Rad,b=Mathf.Lerp(start,end,(i+1)/24f)*Mathf.Deg2Rad;Line(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius,2);}
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();mesh=vh;
            switch(icon){
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
        public static readonly Color Background=new Color(.14f,.145f,.15f,.97f),Tile=new Color(.205f,.21f,.22f,1),Blue=new Color(.19f,.57f,.98f,1);
        public static RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color,bool hit=true,float radius=24)
        {
            var rect=UiFactory.Rect(parent,name,x,y,w,h);var image=rect.gameObject.AddComponent<RoundedPanelGraphic>();image.color=color;image.raycastTarget=hit;image.radius=radius;return rect;
        }
        public static Button Button(Transform parent,string text,float x,float y,float w,float h,Action action,bool selected=false)
        {
            var rect=Box(parent,text,x,y,w,h,Tile);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Graphic>();
            var colors=button.colors;colors.highlightedColor=new Color(1.35f,1.35f,1.35f,1);colors.pressedColor=Blue;colors.selectedColor=Color.white;button.colors=colors;
            UiFactory.Label(rect,text,8,0,w-16,h,24,TextAlignmentOptions.Center);button.onClick.AddListener(()=>action?.Invoke());Select(button,selected);return button;
        }
        public static Button IconButton(Transform parent,string name,QuickIcon icon,float x,float y,Action action)
        {
            var rect=Box(parent,name,x,y,88,88,Color.clear,true,44);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Graphic>();
            button.targetGraphic.color=Color.white;var colors=button.colors;colors.normalColor=Color.clear;colors.highlightedColor=new Color(.28f,.30f,.34f,1);colors.pressedColor=Blue;colors.selectedColor=Color.clear;colors.disabledColor=Color.clear;button.colors=colors;
            var graphic=UiFactory.Rect(rect,"Icon",23,23,42,42).gameObject.AddComponent<QuickIconGraphic>();graphic.Icon=icon;graphic.raycastTarget=false;
            button.onClick.AddListener(()=>action?.Invoke());return button;
        }
        public static void Select(Button button,bool selected)
        {
            button.targetGraphic.color=selected?new Color(.29f,.30f,.32f,1):Tile;
            var text=button.GetComponentInChildren<TextMeshProUGUI>();if(text!=null)text.color=selected?Blue:Color.white;
        }
        public static OwnedSlider Slider(Transform parent,string name,float x,float y,float width,float min,float max,float value,Action<float> changed,bool thumb=false)
        {
            var rect=Box(parent,name,x,y,width,62,new Color(.10f,.105f,.11f,1),true,16);
            var slider=rect.gameObject.AddComponent<OwnedSlider>();rect.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var area=UiFactory.Rect(rect,"Fill Area",0,0,width,62);var fill=UiFactory.Box(area,"Fill",0,0,width,62,new Color(.64f,.66f,.70f,1));
            fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;slider.fillRect=fill;
            slider.targetGraphic=rect.GetComponent<Graphic>();
            if(thumb){
                fill.gameObject.SetActive(false);slider.fillRect=null;
                var handleArea=UiFactory.Rect(rect,"Handle area",0,0,width,62);
                var handle=Box(handleArea,"Handle",0,0,76,62,new Color(.29f,.30f,.32f,1),false,12);handle.pivot=new Vector2(.5f,.5f);handle.anchorMin=handle.anchorMax=new Vector2(0,.5f);handle.anchoredPosition=Vector2.zero;
                slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Graphic>();
            }
            slider.minValue=min;slider.maxValue=max;slider.SetValueWithoutNotify(value);slider.onValueChanged.AddListener(v=>changed?.Invoke(v));return slider;
        }
    }
}
