using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VRPlayer
{
    // Hover belongs to the card, including its child action, so crossing onto the button keeps it visible.
    public sealed class UiHoverAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private readonly HashSet<int> pointers = new HashSet<int>();
        private CanvasGroup action;

        public void Initialize(CanvasGroup target)
        {
            action = target;
            Clear();
        }

        public void OnPointerEnter(PointerEventData data)
        {
            pointers.Add(data.pointerId);
            Refresh();
        }

        public void OnPointerExit(PointerEventData data)
        {
            pointers.Remove(data.pointerId);
            Refresh();
        }

        private void Clear() { pointers.Clear(); Refresh(); }
        private void OnDisable() => Clear();
        private void Refresh()
        {
            if (action == null) return;
            bool visible = pointers.Count > 0;
            action.alpha = visible ? 1 : 0;
            action.interactable = action.blocksRaycasts = visible;
        }
    }
}
