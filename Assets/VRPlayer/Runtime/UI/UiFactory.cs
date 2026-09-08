using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    public static class UiFactory
    {
        public static readonly Color Background=new Color(.035f,.055f,.085f,.98f),Panel=new Color(.07f,.105f,.155f,1),Accent=new Color(.14f,.78f,.72f,1),TextColor=new Color(.89f,.94f,.98f,1),Muted=new Color(.54f,.65f,.74f,1);
        private static TMP_FontAsset font;
        public static TMP_FontAsset Font
        {
            get{if(font==null){font=Resources.Load<TMP_FontAsset>("Fonts/UIChinese");if(font==null){var source=Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");font=TMP_FontAsset.CreateFontAsset(source);font.isMultiAtlasTexturesEnabled=true;}}return font;}
        }
        public static RectTransform Rect(Transform parent,string name,float x,float y,float width,float height)
        {
            var obj=new GameObject(name,typeof(RectTransform));obj.transform.SetParent(parent,false);var rect=(RectTransform)obj.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);return rect;
        }
        public static RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color,bool raycast=false){var rect=Rect(parent,name,x,y,w,h);var image=rect.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=raycast;return rect;}
        public static TextMeshProUGUI Label(Transform parent,string text,float x,float y,float w,float h,float size=26,TextAlignmentOptions alignment=TextAlignmentOptions.Left)
        {
            var label=Rect(parent,"Text",x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();label.font=Font;label.text=text;label.fontSize=size;label.color=TextColor;label.alignment=alignment;label.raycastTarget=false;label.richText=false;label.overflowMode=TextOverflowModes.Ellipsis;label.textWrappingMode=TextWrappingModes.NoWrap;return label;
        }
        public static Button Button(Transform parent,string text,float x,float y,float w,float h,Action action,bool accent=false)
        {
            var rect=Box(parent,text,x,y,w,h,accent?new Color(.08f,.36f,.36f,1):Panel,true);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.25f,1.3f,1.3f,1);colors.pressedColor=Accent;colors.selectedColor=Color.white;button.colors=colors;
            Label(rect,text,8,0,w-16,h,24,TextAlignmentOptions.Center);button.onClick.AddListener(()=>action?.Invoke());return button;
        }
        public static void SetAccent(Button button,bool selected)
        {
            button.targetGraphic.color=selected?new Color(.08f,.36f,.36f,1):Panel;
            button.GetComponentInChildren<TextMeshProUGUI>().color=selected?Accent:TextColor;
        }
        public static TMP_InputField Input(Transform parent,string value,string placeholder,float x,float y,float w,float h,Action<TMP_InputField> selected=null,bool password=false)
        {
            var rect=Box(parent,"Input",x,y,w,h,new Color(.045f,.075f,.115f,1),true);var input=rect.gameObject.AddComponent<TMP_InputField>();
            var viewport=Rect(rect,"Viewport",12,5,w-24,h-10);viewport.gameObject.AddComponent<RectMask2D>();
            var text=Label(viewport,"",0,0,w-24,h-10,25);text.overflowMode=TextOverflowModes.Masking;
            var hint=Label(viewport,placeholder,0,0,w-24,h-10,24);hint.color=Muted;
            input.textViewport=viewport;input.textComponent=text;input.placeholder=hint;input.fontAsset=Font;input.text=value;input.caretColor=Accent;input.customCaretColor=true;input.selectionColor=new Color(.1f,.7f,.6f,.4f);input.characterLimit=2048;input.shouldHideSoftKeyboard=true;
            if(password)input.contentType=TMP_InputField.ContentType.Password;
            input.onSelect.AddListener(_=>selected?.Invoke(input));return input;
        }
        public static Slider Slider(Transform parent,float x,float y,float w,float min,float max,float value,Action<float> changed)
        {
            var rect=Rect(parent,"Slider",x,y,w,42);var slider=rect.gameObject.AddComponent<Slider>();
            var hitArea=rect.gameObject.AddComponent<Image>();hitArea.color=Color.clear;hitArea.raycastTarget=true;
            Box(rect,"Rail",0,17,w,8,new Color(.12f,.2f,.25f,1));
            var fillArea=Rect(rect,"Fill Area",0,17,w,8);var fill=Box(fillArea,"Fill",0,0,0,0,Accent);fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;slider.fillRect=fill;
            var handleArea=Rect(rect,"Handle Area",10,6,w-20,30);var handle=Box(handleArea,"Handle",0,0,22,0,new Color(.8f,.98f,.96f,1),true);handle.pivot=new Vector2(.5f,.5f);handle.anchorMin=Vector2.zero;handle.anchorMax=new Vector2(0,1);handle.anchoredPosition=Vector2.zero;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();slider.minValue=min;slider.maxValue=max;slider.SetValueWithoutNotify(value);slider.onValueChanged.AddListener(v=>changed?.Invoke(v));return slider;
        }
        public static void Clear(Transform parent){for(int i=parent.childCount-1;i>=0;i--){var child=parent.GetChild(i).gameObject;child.SetActive(false);UnityEngine.Object.Destroy(child);}}
    }
    public sealed class SeekGesture : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IBeginDragHandler,IEndDragHandler
    {
        public bool Dragging{get;private set;}
        public Action<float> Commit;
        public void OnPointerDown(PointerEventData data)=>Dragging=true;
        public void OnBeginDrag(PointerEventData data)=>Dragging=true;
        public void OnPointerUp(PointerEventData data){Dragging=false;Commit?.Invoke(GetComponent<Slider>().value);}
        public void OnEndDrag(PointerEventData data){Dragging=false;}
    }
}
