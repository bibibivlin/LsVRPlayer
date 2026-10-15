using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed class BrowserRow
    {
        public string key,label,detail,secondary,subtitle;
        public QuickIcon? icon,actionIcon;
        public Action open,action;
        public bool selected,sectionHeader;
    }
    // Fixed-height recycled rows: object count depends on the viewport, never directory size.
    public sealed class VirtualFileList : MonoBehaviour
    {
        public const float RowPitch=80;
        private ListScrollRect scroll;
        private RectTransform body;
        private readonly List<Row> pool=new List<Row>();
        private IReadOnlyList<BrowserRow> items=Array.Empty<BrowserRow>();
        private int first=-1;
        private bool dirty;
        private bool quickStyle,directoryStyle,fullWidthRows;
        private sealed class Row {public RectTransform root,divider,sectionRule;public Button open,action;public TextMeshProUGUI label,detail,actionLabel,subtitle,heading;public RowBinding binding;public QuickIconGraphic icon,actionIcon;public UiIconTooltip actionHint;public RoundedPanelGraphic surface;public float fontSize,labelHeight;}
        public float Offset=>body==null?0:body.anchoredPosition.y;
        public int PoolCount=>pool.Count;
        public int Count=>items.Count;
        public void Initialize(float width,float height,bool quickStyle=false,bool directoryStyle=false,bool fullWidthRows=false)
        {
            this.quickStyle=quickStyle;this.directoryStyle=directoryStyle;this.fullWidthRows=fullWidthRows;
            scroll=gameObject.AddComponent<ListScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;scroll.decelerationRate=.12f;
            var viewport=UiFactory.Box(transform,"Viewport",0,0,width-32,height,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();scroll.viewport=viewport;
            body=UiFactory.Rect(viewport,"Rows",0,0,width-32,height);scroll.content=body;
            var barRect=UiFactory.Box(transform,"Scrollbar",width-16,0,10,height,new Color(.09f,.17f,.22f,.35f),true);
            var bar=barRect.gameObject.AddComponent<Scrollbar>();var handle=UiFactory.RoundedBox(barRect,"Handle",0,0,10,height,new Color(.40f,.53f,.62f,.78f),true,5);handle.GetComponent<RoundedPanelGraphic>().shadow=0;handle.GetComponent<RoundedPanelGraphic>().border=0;
            handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Graphic>();bar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=bar;
            if(fullWidthRows)scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            for(int i=0;i<Mathf.CeilToInt(height/RowPitch)+2;i++){
                var row=new Row();row.root=UiFactory.Rect(body,"Row",0,0,width-32,72);
                row.binding=row.root.gameObject.AddComponent<RowBinding>();
                if(directoryStyle)row.divider=UiFactory.Box(row.root,"Row divider",12,78,width-52,1,new Color(.20f,.31f,.37f,.22f));
                row.heading=UiFactory.Label(row.root,"",16,22,width-64,42,24);row.heading.color=UiTheme.Muted;
                row.sectionRule=UiFactory.Box(row.root,"Section rule",0,43,0,1,new Color(.20f,.31f,.37f,.38f));
                row.heading.gameObject.SetActive(false);row.sectionRule.gameObject.SetActive(false);
                row.open=quickStyle?QuickUiFactory.Button(row.root,"",0,0,width-230,72,()=>row.binding.Open()):UiFactory.Button(row.root,"",0,0,width-230,72,()=>row.binding.Open());row.label=row.open.GetComponentInChildren<TextMeshProUGUI>();row.label.alignment=TextAlignmentOptions.MidlineLeft;
                row.open.gameObject.AddComponent<RowPress>().Bind(row.binding);
                if(directoryStyle){row.surface=row.open.targetGraphic as RoundedPanelGraphic;row.surface.shadow=0;row.surface.border=1;row.label.rectTransform.anchoredPosition=new Vector2(74,0);row.label.rectTransform.sizeDelta=new Vector2(width-312,72);row.icon=UiFactory.Icon(row.open.transform,QuickIcon.Film,22,20,32,UiTheme.Text);}
                row.fontSize=row.label.fontSize;row.labelHeight=row.label.rectTransform.sizeDelta.y;
                row.subtitle=UiFactory.Label(row.open.transform,"",directoryStyle?74:12,40,width-(directoryStyle?312:254),34,22);row.subtitle.color=UiFactory.Muted;row.subtitle.gameObject.SetActive(false);
                row.action=UiFactory.Button(row.root,"",width-216,0,180,72,()=>row.binding.Action());row.actionLabel=row.action.GetComponentInChildren<TextMeshProUGUI>();
                row.actionIcon=UiFactory.Icon(row.action.transform,QuickIcon.Edit,17,17,30,UiTheme.Text);
                row.actionIcon.gameObject.SetActive(false);
                row.actionHint=row.action.gameObject.AddComponent<UiIconTooltip>();row.actionHint.Initialize(row.action,row.actionLabel);row.actionHint.enabled=false;
                row.action.gameObject.AddComponent<RowPress>().Bind(row.binding);
                row.detail=UiFactory.Label(row.root,"",width-216,0,180,72,22,TextAlignmentOptions.Right);pool.Add(row);
            }
            scroll.onValueChanged.AddListener(_=>Rebind());
        }
        public void SetRows(IReadOnlyList<BrowserRow> rows,bool reset=false)
        {
            items=rows??Array.Empty<BrowserRow>();float offset=reset?0:Offset;
            body.sizeDelta=new Vector2(body.sizeDelta.x,Mathf.Max(scroll.viewport.rect.height,items.Count*RowPitch));
            SetOffset(offset);dirty=true;Rebind();
        }
        public void SetOffset(float value){scroll.StopMovement();body.anchoredPosition=new Vector2(0,Mathf.Clamp(value,0,Mathf.Max(0,body.rect.height-scroll.viewport.rect.height)));Rebind();}
        public void Scroll(float axis){if(scroll.enabled&&!scroll.HasDragOwner&&Mathf.Abs(axis)>.2f)SetOffset(Offset-Mathf.Sign(axis)*(Mathf.Abs(axis)-.2f)/.8f*650*Time.unscaledDeltaTime);}
        public void Block(bool blocked){if(scroll!=null){scroll.enabled=!blocked;if(blocked)scroll.StopMovement();}}
        private void Rebind()
        {
            int start=Mathf.Max(0,Mathf.FloorToInt(Offset/RowPitch));if(!dirty&&first==start)return;first=start;dirty=false;
            for(int i=0;i<pool.Count;i++){
                var row=pool[i];int index=start+i;row.root.gameObject.SetActive(index<items.Count);if(index>=items.Count)continue;
                var item=items[index];row.root.anchoredPosition=new Vector2(0,-index*RowPitch);row.binding.Item=item;
                row.open.gameObject.SetActive(!item.sectionHeader);row.heading.gameObject.SetActive(item.sectionHeader);row.sectionRule.gameObject.SetActive(item.sectionHeader);
                if(row.divider!=null)row.divider.gameObject.SetActive(!item.sectionHeader);
                if(item.sectionHeader){
                    row.heading.text=item.label;float ruleX=16+Mathf.Ceil(row.heading.preferredWidth)+20;
                    row.sectionRule.anchoredPosition=new Vector2(ruleX,-43);row.sectionRule.sizeDelta=new Vector2(Mathf.Max(0,row.root.rect.width-ruleX-16),1);
                    row.action.gameObject.SetActive(false);row.detail.gameObject.SetActive(false);continue;
                }
                row.label.text=item.label;row.open.interactable=item.open!=null;
                bool iconAction=item.action!=null&&item.actionIcon.HasValue;float actionWidth=iconAction?UiTheme.IconButtonSize:180;
                var actionRect=(RectTransform)row.action.transform;actionRect.anchoredPosition=new Vector2(row.root.rect.width-actionWidth-4,iconAction?-4:0);actionRect.sizeDelta=new Vector2(actionWidth,iconAction?UiTheme.IconButtonSize:72);
                row.actionHint.enabled=false;row.actionLabel.text=item.secondary;row.actionLabel.gameObject.SetActive(!iconAction);row.actionIcon.gameObject.SetActive(iconAction);
                row.action.name=item.secondary??"Row action";
                if(iconAction){row.actionIcon.Icon=item.actionIcon.Value;row.actionHint.enabled=true;}
                if(fullWidthRows){
                    float width=row.root.rect.width-(item.action!=null?actionWidth+18:0);
                    ((RectTransform)row.open.transform).sizeDelta=new Vector2(width,72);
                    row.label.rectTransform.anchoredPosition=new Vector2(16,0);
                    row.label.rectTransform.sizeDelta=new Vector2(width-32-(item.action==null&&!string.IsNullOrEmpty(item.detail)?198:0),row.label.rectTransform.sizeDelta.y);
                    row.detail.rectTransform.anchoredPosition=new Vector2(row.root.rect.width-196,0);
                }
                if(directoryStyle){
                    float width=row.root.rect.width-(iconAction?UiTheme.IconButtonSize+20:198);
                    ((RectTransform)row.open.transform).sizeDelta=new Vector2(width,72);
                    row.label.rectTransform.sizeDelta=new Vector2(width-86,row.label.rectTransform.sizeDelta.y);row.subtitle.rectTransform.sizeDelta=new Vector2(width-86,34);
                }
                bool hasSubtitle=!string.IsNullOrEmpty(item.subtitle);row.subtitle.gameObject.SetActive(hasSubtitle);row.subtitle.text=item.subtitle;
                row.label.fontSize=hasSubtitle?Mathf.Min(26,row.fontSize):row.fontSize;row.label.rectTransform.sizeDelta=new Vector2(row.label.rectTransform.sizeDelta.x,hasSubtitle?42:row.labelHeight);
                foreach(var rect in new[]{row.root,(RectTransform)row.open.transform})rect.sizeDelta=new Vector2(rect.sizeDelta.x,72);
                if(directoryStyle){row.surface.color=Color.white;row.surface.Selected=item.selected;var colors=row.open.colors;colors.normalColor=item.selected?UiTheme.SelectedMuted:Color.clear;colors.highlightedColor=UiTheme.Elevated;colors.selectedColor=colors.normalColor;colors.pressedColor=UiTheme.SelectedMuted;colors.disabledColor=Color.clear;UiFactory.SetColorsImmediate(row.open,colors);row.label.color=UiTheme.Text;row.icon.gameObject.SetActive(item.icon.HasValue);if(item.icon.HasValue)row.icon.Icon=item.icon.Value;row.icon.color=item.selected?UiTheme.Accent:UiTheme.Text;}
                else if(quickStyle)QuickUiFactory.Select(row.open,item.selected);else UiFactory.SetAccent(row.open,item.selected);row.detail.color=item.selected?(fullWidthRows?UiTheme.Ink:UiTheme.Accent):UiFactory.Muted;
                row.action.gameObject.SetActive(item.action!=null);
                row.detail.gameObject.SetActive(item.action==null);row.detail.text=item.detail;
            }
        }
    }
    public sealed class ListScrollRect : ScrollRect
    {
        private int? owner;
        public bool HasDragOwner=>owner.HasValue;
        public override void OnInitializePotentialDrag(PointerEventData data){if(!HasDragOwner)base.OnInitializePotentialDrag(data);}
        public override void OnBeginDrag(PointerEventData data){if(!IsActive()||HasDragOwner||data.button!=PointerEventData.InputButton.Left)return;owner=data.pointerId;base.OnBeginDrag(data);}
        public override void OnDrag(PointerEventData data){if(owner==data.pointerId)base.OnDrag(data);}
        public override void OnEndDrag(PointerEventData data){if(owner!=data.pointerId)return;base.OnEndDrag(data);owner=null;}
        protected override void SetNormalizedPosition(float value,int axis)
        {
            // Layout updates mirror the content into the scrollbar. They must not feed
            // stale normalized values back into a newly restored directory position.
            if(CanvasUpdateRegistry.IsRebuildingLayout())return;
            base.SetNormalizedPosition(value,axis);
        }
        public override void OnScroll(PointerEventData data){if(!HasDragOwner)base.OnScroll(data);}
        protected override void OnDisable(){owner=null;base.OnDisable();}
    }
    public sealed class RowBinding : MonoBehaviour
    {
        public BrowserRow Item;
        private string pressedKey;
        public void Press()=>pressedKey=Item?.key;
        public void Open(){if(Item?.key==pressedKey)Item?.open?.Invoke();}
        public void Action(){if(Item?.key==pressedKey)Item?.action?.Invoke();}
    }
    public sealed class RowPress : MonoBehaviour,IPointerDownHandler
    {
        private RowBinding binding;
        public void Bind(RowBinding value)=>binding=value;
        public void OnPointerDown(PointerEventData data)=>binding?.Press();
    }
}
