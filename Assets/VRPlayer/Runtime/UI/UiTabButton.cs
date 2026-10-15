using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed class UiTabButton : Button
    {
        private readonly HashSet<int> hovering=new HashSet<int>();
        private TextMeshProUGUI label;
        private UiTabGroup group;
        private bool selected;

        internal void Initialize(TextMeshProUGUI text,UiTabGroup owner)
        {
            label=text;group=owner;group.Register(this);RefreshText(currentSelectionState);
        }

        internal void SetSelected(bool value)
        {
            if(value)group.Select(this);else SetSelectedState(false);
        }

        internal void SetSelectedState(bool value){selected=value;RefreshText(currentSelectionState);}

        protected override void DoStateTransition(SelectionState state,bool instant)
        {
            base.DoStateTransition(state,instant);RefreshText(state);
        }

        private void RefreshText(SelectionState state)
        {
            if(label!=null)label.color=state==SelectionState.Disabled?UiTheme.Muted:selected||hovering.Count>0||state==SelectionState.Pressed||state==SelectionState.Selected?UiTheme.Accent:UiTheme.Text;
        }

        public override void OnPointerEnter(PointerEventData data){hovering.Add(data.pointerId);base.OnPointerEnter(data);RefreshText(currentSelectionState);}
        public override void OnPointerExit(PointerEventData data){hovering.Remove(data.pointerId);if(hovering.Count==0)base.OnPointerExit(data);RefreshText(currentSelectionState);}
        protected override void OnDisable(){hovering.Clear();base.OnDisable();RefreshText(currentSelectionState);}
    }
}
