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
        public Action open,action;
        public bool selected;
    }
    // Fixed-height recycled rows: object count depends on the viewport, never directory size.
    public sealed class VirtualFileList : MonoBehaviour
    {
        public const float RowPitch=64;
        private ListScrollRect scroll;
        private RectTransform body;
        private readonly List<Row> pool=new List<Row>();
        private IReadOnlyList<BrowserRow> items=Array.Empty<BrowserRow>();
        private int first=-1;
        private bool dirty;
        private bool quickStyle;
        private sealed class Row {public RectTransform root;public Button open,action;public TextMeshProUGUI label,detail,actionLabel,subtitle;public RowBinding binding;public float fontSize,labelHeight;}
        public float Offset=>body==null?0:body.anchoredPosition.y;
        public int PoolCount=>pool.Count;
        public int Count=>items.Count;
        public void Initialize(float width,float height,bool quickStyle=false)
        {
            this.quickStyle=quickStyle;
            scroll=gameObject.AddComponent<ListScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;scroll.decelerationRate=.12f;
            var viewport=UiFactory.Box(transform,"Viewport",0,0,width-32,height,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();scroll.viewport=viewport;
            body=UiFactory.Rect(viewport,"Rows",0,0,width-32,height);scroll.content=body;
            var barRect=UiFactory.Box(transform,"Scrollbar",width-24,0,24,height,quickStyle?new Color(.16f,.17f,.18f,1):UiFactory.Panel,true);
            var bar=barRect.gameObject.AddComponent<Scrollbar>();var handle=UiFactory.Box(barRect,"Handle",0,0,24,height,quickStyle?new Color(.38f,.40f,.42f,1):UiFactory.Accent,true);
            handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=bar;
            for(int i=0;i<Mathf.CeilToInt(height/RowPitch)+2;i++){
                var row=new Row();row.root=UiFactory.Rect(body,"Row",0,0,width-32,56);
                row.binding=row.root.gameObject.AddComponent<RowBinding>();
                row.open=quickStyle?QuickUiFactory.Button(row.root,"",0,0,width-230,56,()=>row.binding.Open()):UiFactory.Button(row.root,"",0,0,width-230,56,()=>row.binding.Open());row.label=row.open.GetComponentInChildren<TextMeshProUGUI>();row.label.alignment=TextAlignmentOptions.Left;
                row.open.gameObject.AddComponent<RowPress>().Bind(row.binding);
                row.fontSize=row.label.fontSize;row.labelHeight=row.label.rectTransform.sizeDelta.y;
                row.subtitle=UiFactory.Label(row.open.transform,"",12,34,width-254,26,17);row.subtitle.color=UiFactory.Muted;row.subtitle.gameObject.SetActive(false);
                row.action=UiFactory.Button(row.root,"",width-216,0,180,56,()=>row.binding.Action());row.actionLabel=row.action.GetComponentInChildren<TextMeshProUGUI>();
                row.action.gameObject.AddComponent<RowPress>().Bind(row.binding);
                row.detail=UiFactory.Label(row.root,"",width-216,0,180,56,22,TextAlignmentOptions.Right);pool.Add(row);
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
                row.label.text=item.label;row.open.interactable=item.open!=null;
                bool hasSubtitle=!string.IsNullOrEmpty(item.subtitle);row.subtitle.gameObject.SetActive(hasSubtitle);row.subtitle.text=item.subtitle;
                row.label.fontSize=hasSubtitle?Mathf.Min(23,row.fontSize):row.fontSize;row.label.rectTransform.sizeDelta=new Vector2(row.label.rectTransform.sizeDelta.x,hasSubtitle?34:row.labelHeight);
                foreach(var rect in new[]{row.root,(RectTransform)row.open.transform,(RectTransform)row.action.transform})rect.sizeDelta=new Vector2(rect.sizeDelta.x,hasSubtitle?60:56);
                if(quickStyle){QuickUiFactory.Select(row.open,item.selected);row.detail.color=item.selected?QuickUiFactory.Blue:UiFactory.Muted;}
                row.action.gameObject.SetActive(item.action!=null);row.actionLabel.text=item.secondary;
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
