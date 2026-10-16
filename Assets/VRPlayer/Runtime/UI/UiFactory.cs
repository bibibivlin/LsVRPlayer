using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    public static class UiFactory
    {
        public static readonly Color Background=UiTheme.Background,Panel=UiTheme.Surface,Accent=UiTheme.Accent,TextColor=UiTheme.Text,Muted=UiTheme.Muted;
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
        public static RectTransform RoundedBox(Transform parent,string name,float x,float y,float w,float h,Color color,bool raycast=false,float radius=18,SurfaceTreatment treatment=SurfaceTreatment.Panel)
        {
            var rect=Rect(parent,name,x,y,w,h);var graphic=rect.gameObject.AddComponent<RoundedPanelGraphic>();graphic.color=color;graphic.raycastTarget=raycast;graphic.radius=radius;graphic.Treatment=treatment;graphic.shadow=treatment==SurfaceTreatment.Frame?.42f:treatment==SurfaceTreatment.Control?.14f:.20f;return rect;
        }
        public static TextMeshProUGUI Label(Transform parent,string text,float x,float y,float w,float h,float size=26,TextAlignmentOptions alignment=TextAlignmentOptions.Left,bool opticalCenter=true)
        {
            float height=Mathf.Max(h,size*1.6f);
            if(opticalCenter){
                if(alignment==TextAlignmentOptions.Left)alignment=TextAlignmentOptions.MidlineLeft;
                else if(alignment==TextAlignmentOptions.Center)alignment=TextAlignmentOptions.Midline;
                else if(alignment==TextAlignmentOptions.Right)alignment=TextAlignmentOptions.MidlineRight;
                // Extra line room must remain symmetric around the requested layout slot.
                if(((int)alignment&0xff00)==(int)VerticalAlignmentOptions.Geometry)y-=(height-h)*.5f;
            }
            var label=Rect(parent,"Text",x,y,w,height).gameObject.AddComponent<TextMeshProUGUI>();label.font=Font;label.text=text;label.fontSize=size;label.color=TextColor;label.alignment=alignment;label.raycastTarget=false;label.richText=false;label.overflowMode=TextOverflowModes.Ellipsis;label.textWrappingMode=TextWrappingModes.NoWrap;return label;
        }
        public static Button Button(Transform parent,string text,float x,float y,float w,float h,Action action,bool accent=false)
        {
            var rect=RoundedBox(parent,text,x,y,w,h,accent?UiTheme.Selected:UiTheme.Elevated,true,12,SurfaceTreatment.Control);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Graphic>();
            SetColorsImmediate(button,UiTheme.ButtonColors());
            Label(rect,text,12,0,w-24,h,26,TextAlignmentOptions.Center);button.onClick.AddListener(()=>action?.Invoke());SetAccent(button,accent);return button;
        }
        public static Button IconButton(Transform parent,string name,QuickIcon icon,float x,float y,Action action,bool accent=false,string tooltip=null,float size=UiTheme.IconButtonSize)
        {
            float width=size,height=size,glyphSize=Mathf.Min(30,size*.5f);
            var button=Button(parent,name,x,y,width,height,action,accent);var text=button.GetComponentInChildren<TextMeshProUGUI>();text.gameObject.SetActive(false);
            var graphic=Rect(button.transform,"Icon",(width-glyphSize)/2,(height-glyphSize)/2,glyphSize,glyphSize).gameObject.AddComponent<QuickIconGraphic>();graphic.Icon=icon;graphic.color=accent?UiTheme.Ink:TextColor;graphic.raycastTarget=false;
            button.gameObject.AddComponent<UiIconTooltip>().Initialize(button,text,tooltip);return button;
        }
        public static QuickIconGraphic Icon(Transform parent,QuickIcon icon,float x,float y,float size,Color color)
        {
            var graphic=Rect(parent,"Icon",x,y,size,size).gameObject.AddComponent<QuickIconGraphic>();graphic.Icon=icon;graphic.color=color;graphic.raycastTarget=false;return graphic;
        }
        public static Button TabButton(Transform parent,string name,float x,float y,float width,float height,Action action)
        {
            var rect=Box(parent,name,x,y,width,height,Color.clear,true);var button=rect.gameObject.AddComponent<UiTabButton>();button.targetGraphic=rect.GetComponent<Graphic>();
            var colors=UiTheme.ButtonColors();colors.normalColor=colors.highlightedColor=colors.pressedColor=colors.selectedColor=colors.disabledColor=Color.clear;SetColorsImmediate(button,colors);
            var group=parent.GetComponent<UiTabGroup>();if(group==null)group=parent.gameObject.AddComponent<UiTabGroup>();
            button.Initialize(Label(rect,name,12,0,width-24,height,26,TextAlignmentOptions.Center),group);button.onClick.AddListener(()=>action?.Invoke());return button;
        }
        public static Button SourceButton(Transform parent,string name,QuickIcon icon,float x,float y,float width,float height,Action action,bool selected)
        {
            var button=Button(parent,name,x,y,width,height,action);var nav=button.gameObject.AddComponent<UiNavState>();nav.sidebar=true;
            var text=button.GetComponentInChildren<TextMeshProUGUI>();text.alignment=TextAlignmentOptions.MidlineLeft;text.rectTransform.anchoredPosition=new Vector2(64,0);text.rectTransform.sizeDelta=new Vector2(width-72,height);
            Icon(button.transform,icon,16,(height-30)/2,30,TextColor);SetAccent(button,selected);return button;
        }
        public static Button OptionTile(Transform parent,string name,QuickIcon icon,float x,float y,float width,float height,Action action,bool selected=false)
        {
            var button=Button(parent,name,x,y,width,height,action,selected);var text=button.GetComponentInChildren<TextMeshProUGUI>();text.fontSize=23;text.rectTransform.anchoredPosition=new Vector2(12,-height+44);text.rectTransform.sizeDelta=new Vector2(width-24,38);
            Icon(button.transform,icon,(width-34)/2,12,34,selected?UiTheme.Ink:TextColor);
            Icon(button.transform,QuickIcon.Check,width-30,10,18,selected?UiTheme.Ink:Color.clear).name="Selection mark";return button;
        }
        public static Button ToggleRow(Transform parent,string name,string label,bool value,float x,float y,float width,Action action,float height=80)
        {
            return CreateSwitch(parent,name,label,value,null,_=>action?.Invoke(),x,y,width,height);
        }
        public static UiSwitch ToggleRow(Transform parent,string name,string label,Func<bool> value,float x,float y,float width,Action<bool> change,float height=64)
        {
            return CreateSwitch(parent,name,label,value(),value,change,x,y,width,height);
        }
        private static UiSwitch CreateSwitch(Transform parent,string name,string label,bool value,Func<bool> read,Action<bool> change,float x,float y,float width,float height)
        {
            var rect=Box(parent,name,x,y,width,height,Color.clear,true);var button=rect.gameObject.AddComponent<UiSwitch>();button.targetGraphic=rect.GetComponent<Graphic>();
            var text=Label(rect,label,0,0,width-70,height,26);
            var rail=RoundedBox(rect,"Switch",width-54,(height-28)/2,54,28,value?Accent:UiTheme.Rail,false,14,SurfaceTreatment.Control).GetComponent<RoundedPanelGraphic>();rail.shadow=0;
            var thumb=RoundedBox(rail.transform,"Knob",value?29:3,3,22,22,TextColor,false,11,SurfaceTreatment.Control).GetComponent<RoundedPanelGraphic>();thumb.shadow=.10f;thumb.border=.5f;
            button.Initialize(text,rail,thumb,value,read,change);return button;
        }
        public static Button ColorSwatch(Transform parent,string name,Color color,bool selected,float x,float y,Action action)
        {
            var button=Button(parent,name,x,y,60,60,action);Clear(button.transform);
            button.targetGraphic.color=Color.white;var colors=button.colors;colors.normalColor=Color.clear;colors.highlightedColor=UiTheme.Elevated;colors.selectedColor=Color.clear;SetColorsImmediate(button,colors);
            var ring=RoundedBox(button.transform,"Selection ring",6,6,48,48,selected?Accent:UiTheme.Rail,false,24);ring.GetComponent<RoundedPanelGraphic>().shadow=0;
            var well=RoundedBox(ring,"Swatch",5,5,38,38,color,false,19,SurfaceTreatment.Swatch);var graphic=well.GetComponent<RoundedPanelGraphic>();graphic.shadow=0;graphic.border=0;
            return button;
        }
        public static void ConfigureTransport(Button button,bool primary)
        {
            var panel=button.targetGraphic as RoundedPanelGraphic;panel.radius=Mathf.Min(panel.rectTransform.rect.width,panel.rectTransform.rect.height)/2;panel.shadow=primary?.20f:0;
            panel.color=Color.white;var colors=button.colors;colors.normalColor=primary?TextColor:Color.clear;colors.highlightedColor=primary?Color.white:UiTheme.Elevated;colors.selectedColor=colors.normalColor;colors.pressedColor=Accent;colors.disabledColor=primary?UiTheme.Muted:Color.clear;SetColorsImmediate(button,colors);
            button.GetComponentInChildren<QuickIconGraphic>().color=primary?UiTheme.Ink:TextColor;
        }
        public static void SetAccent(Button button,bool selected)
        {
            if(button is UiTabButton tabButton){tabButton.SetSelected(selected);return;}
            var nav=button.GetComponent<UiNavState>();
            var text=button.GetComponentInChildren<TextMeshProUGUI>();
            if(nav!=null){
                if(nav.sidebar){button.targetGraphic.color=Color.white;var colors=button.colors;colors.normalColor=selected?UiTheme.SelectedMuted:Color.clear;colors.highlightedColor=UiTheme.Elevated;colors.selectedColor=colors.normalColor;colors.pressedColor=UiTheme.SelectedMuted;SetColorsImmediate(button,colors);if(button.targetGraphic is RoundedPanelGraphic side){side.Selected=selected;side.shadow=0;}}
                else{button.targetGraphic.color=Color.white;var colors=button.colors;colors.normalColor=Color.clear;colors.highlightedColor=UiTheme.Elevated;colors.selectedColor=Color.clear;colors.pressedColor=UiTheme.SelectedMuted;SetColorsImmediate(button,colors);}
                text.color=selected?Accent:TextColor;
            }else{
                button.targetGraphic.color=selected?UiTheme.Selected:UiTheme.Elevated;
                if(button.targetGraphic is RoundedPanelGraphic panel)panel.Selected=selected;
                if(text!=null)text.color=selected?UiTheme.Ink:TextColor;
            }
            foreach(var icon in button.GetComponentsInChildren<QuickIconGraphic>())icon.color=icon.name=="Selection mark"?(selected?UiTheme.Ink:Color.clear):(selected?(nav!=null?Accent:UiTheme.Ink):TextColor);
        }
        public static void SetColorsImmediate(Selectable control,ColorBlock colors)
        {
            // A rebuilt or recycled control must never fade from the renderer's old
            // tint (often white). Resolve its current pressed/hover/disabled state
            // synchronously, then retain the fade for subsequent pointer changes.
            float duration=colors.fadeDuration;colors.fadeDuration=0;control.colors=colors;
            colors.fadeDuration=duration;control.colors=colors;
        }
        public static TMP_InputField Input(Transform parent,string value,string placeholder,float x,float y,float w,float h,Action<TMP_InputField> selected=null,bool password=false)
        {
            var rect=RoundedBox(parent,"Input",x,y,w,h,UiTheme.Elevated,true,12,SurfaceTreatment.Inset);var input=rect.gameObject.AddComponent<TMP_InputField>();input.targetGraphic=rect.GetComponent<Graphic>();input.colors=UiTheme.ButtonColors();
            var viewport=Rect(rect,"Viewport",12,5,w-24,h-10);viewport.gameObject.AddComponent<RectMask2D>();
            var text=Label(viewport,"",0,0,w-24,h-10,25);text.overflowMode=TextOverflowModes.Masking;
            var hint=Label(viewport,placeholder,0,0,w-24,h-10,24);hint.color=Muted;
            input.textViewport=viewport;input.textComponent=text;input.placeholder=hint;input.fontAsset=Font;input.text=value;input.caretColor=Accent;input.customCaretColor=true;input.selectionColor=new Color(.1f,.7f,.6f,.4f);input.characterLimit=2048;input.shouldHideSoftKeyboard=true;
            if(password)input.contentType=TMP_InputField.ContentType.Password;
            input.onSelect.AddListener(_=>selected?.Invoke(input));return input;
        }
        public static Slider Slider(Transform parent,float x,float y,float w,float min,float max,float value,Action<float> changed)
        {
            var rect=Rect(parent,"Slider",x,y,w,UiTheme.SliderHeight);var slider=rect.gameObject.AddComponent<OwnedSlider>();
            var hitArea=rect.gameObject.AddComponent<Image>();hitArea.color=Color.clear;hitArea.raycastTarget=true;
            RoundedBox(rect,"Rail",0,20,w,8,UiTheme.Rail,false,4);
            var fillArea=Rect(rect,"Fill Area",0,20,w,8);var fill=Box(fillArea,"Fill",0,0,0,0,Accent);fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;slider.fillRect=fill;
            var handleArea=Rect(rect,"Handle Area",14,10,w-28,28);var handle=RoundedBox(handleArea,"Handle",0,0,28,0,TextColor,true,14);handle.pivot=new Vector2(.5f,.5f);handle.anchorMin=Vector2.zero;handle.anchorMax=new Vector2(0,1);handle.anchoredPosition=Vector2.zero;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Graphic>();slider.colors=UiTheme.ButtonColors();slider.minValue=min;slider.maxValue=max;slider.SetValueWithoutNotify(value);slider.onValueChanged.AddListener(v=>changed?.Invoke(v));return slider;
        }
        public static Slider ValueSlider(Transform parent,string name,float x,float y,float width,float min,float max,float value,Action<float> changed,Func<float,string> format,float labelSize=26,float labelWidth=0,float valueWidth=104)
        {
            float gap=UiTheme.ControlGap;
            var row=Rect(parent,name+" setting",x,y,width,UiTheme.SliderHeight);
            var title=Label(row,name,0,0,width,UiTheme.SliderHeight,labelSize,labelWidth>0?TextAlignmentOptions.Right:TextAlignmentOptions.Left);
            if(labelWidth<=0)labelWidth=Mathf.Ceil(title.preferredWidth);title.rectTransform.sizeDelta=new Vector2(labelWidth,title.rectTransform.sizeDelta.y);
            var number=Label(row,format(value),width-valueWidth,0,valueWidth,UiTheme.SliderHeight,24);
            float start=labelWidth+gap;
            var slider=Slider(row,start,0,width-valueWidth-gap-start,min,max,value,v=>{changed?.Invoke(v);number.text=format(v);});slider.name=name+" slider";return slider;
        }
        public static void Clear(Transform parent){for(int i=parent.childCount-1;i>=0;i--){var child=parent.GetChild(i).gameObject;child.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(child);else UnityEngine.Object.DestroyImmediate(child);}}
    }
    public sealed class SeekGesture : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IBeginDragHandler,IEndDragHandler
    {
        public bool Dragging{get;private set;}
        private int? owner;
        public Action<float> Commit;
        public void OnPointerDown(PointerEventData data){if(owner.HasValue)return;owner=data.pointerId;Dragging=true;}
        public void OnBeginDrag(PointerEventData data){if(owner==data.pointerId)Dragging=true;}
        public void OnPointerUp(PointerEventData data){if(owner!=data.pointerId)return;Cancel();Commit?.Invoke(GetComponent<Slider>().value);}
        public void OnEndDrag(PointerEventData data){if(owner==data.pointerId)Cancel();}
        public void Cancel(){owner=null;Dragging=false;}
        public void Cancel(int pointerId){if(owner==pointerId)Cancel();}
        private void OnDisable()=>Cancel();
    }
    public sealed class OwnedSlider : Slider
    {
        private int? owner;
        public bool HasOwner=>owner.HasValue;
        public override void OnPointerDown(PointerEventData data){if(!IsActive()||!IsInteractable()||data.button!=PointerEventData.InputButton.Left||owner.HasValue)return;owner=data.pointerId;base.OnPointerDown(data);}
        public override void OnDrag(PointerEventData data){if(owner==data.pointerId)base.OnDrag(data);}
        public override void OnPointerUp(PointerEventData data){if(owner!=data.pointerId)return;base.OnPointerUp(data);owner=null;}
        public override void OnInitializePotentialDrag(PointerEventData data){if(!owner.HasValue||owner==data.pointerId)base.OnInitializePotentialDrag(data);}
        protected override void OnDisable(){owner=null;base.OnDisable();}
    }
}
