using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed class UiIconTooltip : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        private Button button;
        private TextMeshProUGUI label;
        private string description;
        private UiIconTooltipLayer layer;
        internal bool Available=>button!=null&&button.isActiveAndEnabled&&button.IsInteractable();
        internal string Description=>description??label?.text??gameObject.name;
        public void Initialize(Button target,TextMeshProUGUI source,string text=null)
        {
            button=target;label=source;description=text;
            var canvas=GetComponentInParent<Canvas>();
            if(canvas!=null){layer=canvas.GetComponent<UiIconTooltipLayer>();if(layer==null)layer=canvas.gameObject.AddComponent<UiIconTooltipLayer>();}
        }
        public void OnPointerEnter(PointerEventData data){if(Available)layer?.Enter(this,data.pointerId);}
        public void OnPointerExit(PointerEventData data)=>layer?.Exit(this,data.pointerId);
        private void OnDisable()=>layer?.Remove(this);
    }

    // One shared, non-interactive hint per canvas, outside scrolling/stencil masks.
    public sealed class UiIconTooltipLayer : MonoBehaviour
    {
        private struct Hover {public UiIconTooltip source;public float enteredAt;public long order;}
        private readonly Dictionary<int,Hover> hovers=new Dictionary<int,Hover>();
        private readonly List<int> removal=new List<int>();
        private readonly Vector3[] corners=new Vector3[4];
        private RectTransform badge,root;
        private TextMeshProUGUI text;
        private long order;
        internal void Enter(UiIconTooltip source,int pointerId)=>hovers[pointerId]=new Hover{source=source,enteredAt=Time.realtimeSinceStartup,order=++order};
        internal void Exit(UiIconTooltip source,int pointerId){if(hovers.TryGetValue(pointerId,out var hover)&&hover.source==source)hovers.Remove(pointerId);if(hovers.Count==0)Hide();}
        internal void Remove(UiIconTooltip source)
        {
            removal.Clear();foreach(var pair in hovers)if(pair.Value.source==source)removal.Add(pair.Key);
            foreach(int pointerId in removal)hovers.Remove(pointerId);Hide();
        }
        private void LateUpdate()
        {
            Hover latest=default;foreach(var hover in hovers.Values)if(hover.source!=null&&hover.source.Available&&hover.order>latest.order)latest=hover;
            if(latest.source==null||Time.realtimeSinceStartup-latest.enteredAt<.32f){Hide();return;}
            if(badge==null){
                root=(RectTransform)transform;badge=UiFactory.RoundedBox(root,"Icon tooltip",0,0,200,54,UiTheme.Elevated,false,12);
                text=UiFactory.Label(badge,"",16,0,168,54,24,TextAlignmentOptions.Center);badge.gameObject.SetActive(false);
            }
            text.text=latest.source.Description;
            float width=Mathf.Clamp(text.GetPreferredValues(text.text).x+32,96,Mathf.Min(440,root.rect.width-24));
            ((RectTransform)latest.source.transform).GetWorldCorners(corners);
            Vector3 top=root.InverseTransformPoint(corners[1]),bottom=root.InverseTransformPoint(corners[3]);
            float x=Mathf.Clamp((top.x+bottom.x)*.5f-root.rect.xMin-width*.5f,12,root.rect.width-width-12);
            float y=root.rect.yMax-top.y-64;if(y<12)y=root.rect.yMax-bottom.y+10;
            badge.anchoredPosition=new Vector2(x,-Mathf.Clamp(y,12,root.rect.height-66));badge.sizeDelta=new Vector2(width,54);text.rectTransform.sizeDelta=new Vector2(width-32,54);
            badge.SetAsLastSibling();badge.gameObject.SetActive(true);
        }
        private void Hide(){if(badge!=null)badge.gameObject.SetActive(false);}
        private void Clear(){hovers.Clear();Hide();}
        private void OnDisable()=>Clear();
        private void OnApplicationFocus(bool focus){if(!focus)Clear();}
        private void OnApplicationPause(bool pause){if(pause)Clear();}
    }
}
