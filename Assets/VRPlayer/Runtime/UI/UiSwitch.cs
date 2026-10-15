using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    // The setting changes immediately; only its presentation is animated.
    public sealed class UiSwitch : Button
    {
        private readonly HashSet<int> hovering=new HashSet<int>();
        private Func<bool> read;
        private TextMeshProUGUI label;
        private RoundedPanelGraphic track,knob;
        private float position,velocity,emphasis,emphasisVelocity;
        private bool initialized;
        public bool Value{get;private set;}

        internal void Initialize(TextMeshProUGUI text,RoundedPanelGraphic rail,RoundedPanelGraphic thumb,bool value,Func<bool> getValue,Action<bool> change)
        {
            transition=Transition.None;label=text;track=rail;knob=thumb;read=getValue;Value=value;initialized=true;
            onClick.AddListener(()=>{
                bool next=!(read?.Invoke()??Value);change?.Invoke(next);
                SetValue(read?.Invoke()??next);
            });
            Snap();
        }

        public void SetValue(bool value)
        {
            if(Value==value)return;Value=value;
            if(!Application.isPlaying||!isActiveAndEnabled)Snap();
        }

        private void LateUpdate()
        {
            if(!initialized)return;
            if(read!=null)SetValue(read());
            float target=Value?1:0,highlight=IsInteractable()&&(hovering.Count>0||currentSelectionState==SelectionState.Pressed)?1:0;
            if(position==target&&velocity==0&&emphasis==highlight&&emphasisVelocity==0)return;
            // Critical damping keeps velocity on reversal, without overshoot or a restart jump.
            float delta=Mathf.Min(Time.unscaledDeltaTime,.05f);
            position=Mathf.SmoothDamp(position,target,ref velocity,.065f,Mathf.Infinity,delta);
            emphasis=Mathf.SmoothDamp(emphasis,highlight,ref emphasisVelocity,.055f,Mathf.Infinity,delta);
            if(Mathf.Abs(position-target)<.0005f&&Mathf.Abs(velocity)<.005f){position=target;velocity=0;}
            if(Mathf.Abs(emphasis-highlight)<.0005f&&Mathf.Abs(emphasisVelocity)<.005f){emphasis=highlight;emphasisVelocity=0;}
            Render();
        }

        private void Snap()
        {
            position=Value?1:0;velocity=emphasisVelocity=0;emphasis=0;Render();
        }

        private void Render()
        {
            if(!initialized)return;
            float blend=Mathf.SmoothStep(0,1,position);
            Color rail=Color.Lerp(UiTheme.Rail,UiTheme.Selected,blend);
            rail=Color.Lerp(rail,Color.Lerp(rail,UiTheme.Text,.13f),emphasis);
            track.color=rail;knob.color=Color.Lerp(UiTheme.Text,Color.white,blend*.65f);
            label.color=Color.Lerp(UiTheme.Text,UiTheme.Accent,emphasis*.7f);
            if(!IsInteractable()){
                track.color=Color.Lerp(UiTheme.Surface,track.color,.45f);
                knob.color=UiTheme.Muted;label.color=UiTheme.Muted;
            }
            var rect=knob.rectTransform;
            float inset=(track.rectTransform.rect.height-rect.rect.height)*.5f;
            rect.anchoredPosition=new Vector2(Mathf.Lerp(inset,track.rectTransform.rect.width-rect.rect.width-inset,position),-inset);
        }

        protected override void DoStateTransition(SelectionState state,bool instant)
        {
            // The transparent row is only a generous hit target, never a button plate.
            Render();
        }
        public override void OnPointerEnter(PointerEventData data){hovering.Add(data.pointerId);base.OnPointerEnter(data);}
        public override void OnPointerExit(PointerEventData data){hovering.Remove(data.pointerId);if(hovering.Count==0)base.OnPointerExit(data);}
        protected override void OnEnable(){base.OnEnable();if(read!=null)Value=read();Snap();}
        protected override void OnDisable(){hovering.Clear();Snap();base.OnDisable();}
        private void OnApplicationFocus(bool focus){if(!focus){hovering.Clear();Snap();}}
        private void OnApplicationPause(bool pause){if(pause){hovering.Clear();Snap();}}
    }
}
