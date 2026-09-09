using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace VRPlayer
{
    // World-space UI ray input, independent of scene sample prefabs and locomotion bindings.
    public sealed class VrUiInput : BaseInputModule
    {
        private PlayerApp app;private Camera head;private Canvas canvas;
        private readonly List<RaycastResult> hits=new List<RaycastResult>();
        private readonly PointerEventData[] pointers=new PointerEventData[3];
        private readonly bool[] held=new bool[3],menuHeld=new bool[2];
        private readonly LineRenderer[] rays=new LineRenderer[2];
        private readonly Transform[] dots=new Transform[2];
        private Material rayMaterial;
        private readonly StickTransportGesture transport=new StickTransportGesture();
        private readonly Vector2[] stickAxes=new Vector2[2];
        private readonly bool[] stickValid=new bool[2];
        public void Initialize(PlayerApp owner,Camera camera,Canvas target)
        {
            app=owner;head=camera;canvas=target;
            for(int i=0;i<3;i++)pointers[i]=new PointerEventData(eventSystem){pointerId=-100-i,button=PointerEventData.InputButton.Left};
            rayMaterial=new Material(Shader.Find("Unlit/Color"));rayMaterial.color=UiFactory.Accent;
            for(int i=0;i<2;i++){
                var ray=new GameObject(i==0?"Left UI Ray":"Right UI Ray");rays[i]=ray.AddComponent<LineRenderer>();rays[i].sharedMaterial=rayMaterial;rays[i].positionCount=2;rays[i].startWidth=.004f;rays[i].endWidth=.002f;
                var dot=GameObject.CreatePrimitive(PrimitiveType.Sphere);dot.name="UI Pointer";Destroy(dot.GetComponent<Collider>());dot.GetComponent<Renderer>().sharedMaterial=rayMaterial;dot.transform.localScale=Vector3.one*.012f;dots[i]=dot.transform;
            }
        }
        public override bool IsModuleSupported()=>true;
        public override bool ShouldActivateModule()=>enabled&&gameObject.activeInHierarchy;
        // Unity may stop Process immediately on focus loss; suppress before its next input tick.
        private void OnApplicationFocus(bool focused){if(!focused)transport.Suppress();}
        private void OnApplicationPause(bool paused){if(paused)transport.Suppress();}
        public override void Process()
        {
            if(app==null)return;
            bool hasController=false;
            for(int hand=0;hand<2;hand++){
                UnityEngine.XR.InputDevice device=InputDevices.GetDeviceAtXRNode(hand==0?XRNode.LeftHand:XRNode.RightHand);
                Vector3 position=Vector3.zero;Quaternion rotation=Quaternion.identity;
                bool valid=device.isValid&&device.TryGetFeatureValue(CommonUsages.devicePosition,out position)&&device.TryGetFeatureValue(CommonUsages.deviceRotation,out rotation);
                stickValid[hand]=valid&&device.TryGetFeatureValue(CommonUsages.primary2DAxis,out stickAxes[hand]);
                if(!valid){rays[hand].enabled=false;dots[hand].gameObject.SetActive(false);if(held[hand])Dispatch(hand,Vector2.zero,false,Vector2.zero,false);continue;}
                hasController=true;
                device.TryGetFeatureValue(CommonUsages.primaryButton,out bool primary);device.TryGetFeatureValue(CommonUsages.menuButton,out bool menu);bool toggle=primary||menu;
                if(toggle&&!menuHeld[hand])app.UI.Toggle();menuHeld[hand]=toggle;
                Transform origin=head.transform.parent;Vector3 world=origin!=null?origin.TransformPoint(position):position;Quaternion worldRotation=origin!=null?origin.rotation*rotation:rotation;
                Ray ray=new Ray(world,worldRotation*Vector3.forward);float distance=2;bool intersects=app.UI.Visible&&new Plane(canvas.transform.forward,canvas.transform.position).Raycast(ray,out distance);
                // The UI lies on one plane. Project the controller intersection for uGUI's GraphicRaycaster.
                float length=intersects?Mathf.Clamp(distance,0,10):2;
                Vector3 point=ray.GetPoint(length);Vector3 screen=head.WorldToScreenPoint(point);bool usable=intersects&&distance>0&&distance<10&&screen.z>0;
                device.TryGetFeatureValue(CommonUsages.trigger,out float trigger);device.TryGetFeatureValue(CommonUsages.triggerButton,out bool triggerButton);
                device.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 stick);
                bool pressed=trigger>.6f||triggerButton;bool wasHeld=held[hand];Dispatch(hand,screen,pressed,Vector2.zero,usable);
                rays[hand].enabled=app.UI.Visible;dots[hand].gameObject.SetActive(usable&&pointers[hand].pointerCurrentRaycast.gameObject!=null);
                rays[hand].SetPosition(0,world);rays[hand].SetPosition(1,point);dots[hand].position=point-ray.direction*.005f;
                if(pressed&&!wasHeld&&pointers[hand].pointerCurrentRaycast.gameObject!=null)device.SendHapticImpulse(0,.18f,.025f);
            }
            if(Mouse.current!=null&&(!hasController||Application.isEditor))Dispatch(2,Mouse.current.position.ReadValue(),Mouse.current.leftButton.isPressed,Mouse.current.scroll.ReadValue()/120,app.UI.Visible);
            bool typing=IsTyping(eventSystem.currentSelectedGameObject);
            bool blocked=!app.HasFocus||app.UI.BlocksTransport||typing||held[0]||held[1]||held[2]||pointers[0].dragging||pointers[1].dragging||pointers[2].dragging;
            int skip=transport.Update(stickAxes[0],stickValid[0],stickAxes[1],stickValid[1],blocked,Time.unscaledTime);
            if(skip!=0){app.Skip(skip);app.UI.LastInteraction=Time.unscaledTime;}
            if(!blocked&&app.UI.Visible&&Mathf.Abs(transport.Scroll)>.2f){app.UI.ScrollList(transport.Scroll);app.UI.LastInteraction=Time.unscaledTime;}
            if(eventSystem.currentSelectedGameObject!=null)ExecuteEvents.Execute(eventSystem.currentSelectedGameObject,GetBaseEventData(),ExecuteEvents.updateSelectedHandler);
            if(Keyboard.current!=null&&!typing&&app.HasFocus&&!app.UI.BlocksTransport){
                if(Keyboard.current.tabKey.wasPressedThisFrame)app.UI.Toggle();if(Keyboard.current.spaceKey.wasPressedThisFrame)app.TogglePlay();if(Keyboard.current.leftArrowKey.wasPressedThisFrame)app.Skip(-10);if(Keyboard.current.rightArrowKey.wasPressedThisFrame)app.Skip(10);if(Keyboard.current.rKey.wasPressedThisFrame)app.Recenter(false);
            }
        }
        public static bool IsTyping(GameObject selected){var field=selected!=null?selected.GetComponent<TMP_InputField>():null;return field!=null&&field.isActiveAndEnabled&&field.isFocused;}
        private void Dispatch(int index,Vector2 position,bool pressed,Vector2 scroll,bool usable)
        {
            PointerEventData data=pointers[index];Vector2 previous=data.position;data.position=position;data.delta=position-previous;data.scrollDelta=scroll;
            if(!usable&&held[index]){CancelPointer(data);held[index]=pressed;return;}
            hits.Clear();if(usable)eventSystem.RaycastAll(data,hits);data.pointerCurrentRaycast=FindFirstRaycast(hits);GameObject target=data.pointerCurrentRaycast.gameObject;
            HandlePointerExitAndEnter(data,target);
            if(target!=null&&(data.delta.sqrMagnitude>1||pressed||scroll.sqrMagnitude>.001f))app.UI.LastInteraction=Time.unscaledTime;
            bool down=pressed&&!held[index],up=!pressed&&held[index];held[index]=pressed;
            if(down){
                data.pressPosition=position;data.pointerPressRaycast=data.pointerCurrentRaycast;data.eligibleForClick=true;data.dragging=false;data.useDragThreshold=true;
                GameObject selected=ExecuteEvents.GetEventHandler<ISelectHandler>(target);eventSystem.SetSelectedGameObject(selected,data);
                data.pointerPress=ExecuteEvents.ExecuteHierarchy(target,data,ExecuteEvents.pointerDownHandler)??ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);data.rawPointerPress=target;data.clickCount=1;data.clickTime=Time.unscaledTime;
                data.pointerDrag=ExecuteEvents.GetEventHandler<IDragHandler>(target);if(data.pointerDrag!=null)ExecuteEvents.Execute(data.pointerDrag,data,ExecuteEvents.initializePotentialDrag);
            }
            if(pressed&&data.pointerDrag!=null&&data.delta.sqrMagnitude>0){
                if(!data.dragging&&(!data.useDragThreshold||(data.position-data.pressPosition).sqrMagnitude>Mathf.Pow(Mathf.Max(10,eventSystem.pixelDragThreshold),2))){ExecuteEvents.Execute(data.pointerDrag,data,ExecuteEvents.beginDragHandler);data.dragging=true;data.eligibleForClick=false;}
                if(data.dragging){if(data.pointerPress!=data.pointerDrag){ExecuteEvents.Execute(data.pointerPress,data,ExecuteEvents.pointerUpHandler);data.eligibleForClick=false;data.pointerPress=null;data.rawPointerPress=null;}ExecuteEvents.Execute(data.pointerDrag,data,ExecuteEvents.dragHandler);}
            }
            if(up){
                ExecuteEvents.Execute(data.pointerPress,data,ExecuteEvents.pointerUpHandler);
                if(data.pointerPress==ExecuteEvents.GetEventHandler<IPointerClickHandler>(target)&&data.eligibleForClick)ExecuteEvents.Execute(data.pointerPress,data,ExecuteEvents.pointerClickHandler);
                else if(data.dragging)ExecuteEvents.ExecuteHierarchy(target,data,ExecuteEvents.dropHandler);
                if(data.dragging&&data.pointerDrag!=null)ExecuteEvents.Execute(data.pointerDrag,data,ExecuteEvents.endDragHandler);
                data.eligibleForClick=false;data.dragging=false;data.pointerPress=null;data.rawPointerPress=null;data.pointerDrag=null;
            }
            if(scroll.sqrMagnitude>.00001f)ExecuteEvents.ExecuteHierarchy(target,data,ExecuteEvents.scrollHandler);
        }
        private void CancelPointer(PointerEventData data)
        {
            ExecuteEvents.Execute(data.pointerPress,data,ExecuteEvents.pointerUpHandler);
            if(data.dragging)ExecuteEvents.Execute(data.pointerDrag,data,ExecuteEvents.endDragHandler);
            data.eligibleForClick=false;data.dragging=false;data.pointerPress=null;data.rawPointerPress=null;data.pointerDrag=null;HandlePointerExitAndEnter(data,null);
        }
        protected override void OnDestroy(){base.OnDestroy();foreach(var ray in rays)if(ray!=null)Destroy(ray.gameObject);foreach(var dot in dots)if(dot!=null)Destroy(dot.gameObject);if(rayMaterial!=null)Destroy(rayMaterial);}
    }
}
