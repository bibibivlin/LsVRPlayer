using System.Collections.Generic;
using UnityEngine;

namespace VRPlayer
{
    public sealed class UiTabGroup : MonoBehaviour
    {
        private readonly List<UiTabButton> buttons=new List<UiTabButton>();
        private RectTransform indicator;
        private UiTabButton selected;
        private Vector2 velocity;
        private bool initialized,moving;
        private int enabledFrame;

        internal void Register(UiTabButton button)
        {
            buttons.Add(button);
            if(indicator==null){
                indicator=UiFactory.RoundedBox(transform,"Selected underline",0,0,76,4,UiTheme.Accent,false,2);
                indicator.anchorMin=indicator.anchorMax=new Vector2(.5f,1);indicator.pivot=new Vector2(.5f,.5f);
                var graphic=indicator.GetComponent<RoundedPanelGraphic>();graphic.shadow=0;graphic.border=0;
                indicator.gameObject.SetActive(false);
            }
            indicator.SetAsLastSibling();
        }

        internal void Select(UiTabButton button)
        {
            if(selected==button)return;
            selected=button;foreach(var item in buttons)item.SetSelectedState(item==selected);
            indicator.gameObject.SetActive(true);
            if(!Application.isPlaying||!isActiveAndEnabled||!initialized||Time.frameCount==enabledFrame)Snap();else moving=true;
        }

        private Vector2 Destination()
        {
            var target=(RectTransform)selected.transform;var rect=(RectTransform)transform;
            Vector3 local=transform.InverseTransformPoint(target.TransformPoint(new Vector3(target.rect.center.x,target.rect.yMin+3,0)));
            return new Vector2(local.x-rect.rect.center.x,local.y-rect.rect.yMax);
        }

        private void Snap()
        {
            moving=false;velocity=Vector2.zero;
            if(indicator!=null&&selected!=null){indicator.anchoredPosition=Destination();initialized=true;}
        }

        private void LateUpdate()
        {
            if(!moving||indicator==null||selected==null)return;
            Vector2 target=Destination();
            // Critically damped movement retains velocity when a new tab is chosen.
            indicator.anchoredPosition=Vector2.SmoothDamp(indicator.anchoredPosition,target,ref velocity,.09f,Mathf.Infinity,Mathf.Min(Time.unscaledDeltaTime,.05f));
            if((indicator.anchoredPosition-target).sqrMagnitude<.0001f&&velocity.sqrMagnitude<1)Snap();
        }

        private void OnEnable(){enabledFrame=Time.frameCount;Snap();}
        private void OnDisable()=>Snap();
        private void OnApplicationFocus(bool focused){if(!focused)Snap();}
        private void OnApplicationPause(bool paused){if(paused)Snap();}
    }
}
