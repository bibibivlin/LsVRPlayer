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
        private readonly UnityEngine.XR.InputDevice[] controllers=new UnityEngine.XR.InputDevice[2];
        private readonly Vector3[] controllerPositions=new Vector3[2];
        private readonly Quaternion[] controllerRotations=new Quaternion[2];
        private readonly bool[] controllerValid=new bool[2],backPressed=new bool[2];
        private readonly LineRenderer[] rays=new LineRenderer[2];
        private readonly Transform[] dots=new Transform[2];
        private Material rayMaterial;
        private readonly StickTransportGesture transport=new StickTransportGesture();
        private readonly Vector2[] stickAxes=new Vector2[2];
        private readonly bool[] stickValid=new bool[2];
        private readonly bool[] gripHeld=new bool[2];
        private readonly TriggerPressGate triggerGate=new TriggerPressGate();
        private readonly TriggerPressGate backGate=new TriggerPressGate();
        private int surfaceRevision=-1;
        private Canvas activeCanvas;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [System.Serializable] public sealed class InputSnapshot
        {
            public int processFrame,processCount,leftSamples,rightSamples,leftChanges,rightChanges;
            public bool diagnostic,leftValid,rightValid;
            public bool leftGrip,rightGrip;public Vector2 leftStick,rightStick;
            public float maxGapMs;
        }
        public InputSnapshot Diagnostics{get;private set;}=new InputSnapshot();
        private long diagnosticTick;
        private readonly Vector3[] previousPositions=new Vector3[2];
        private readonly Quaternion[] previousRotations=new Quaternion[2];
        public void ResetDiagnostics(){Diagnostics=new InputSnapshot();diagnosticTick=0;}
        private void RecordPose(int hand,bool valid)
        {
            bool changed=valid&&((controllerPositions[hand]-previousPositions[hand]).sqrMagnitude>1e-8f||Quaternion.Angle(controllerRotations[hand],previousRotations[hand])>.01f);
            if(hand==0){Diagnostics.leftValid=valid;if(valid)Diagnostics.leftSamples++;if(changed)Diagnostics.leftChanges++;}
            else{Diagnostics.rightValid=valid;if(valid)Diagnostics.rightSamples++;if(changed)Diagnostics.rightChanges++;}
            previousPositions[hand]=controllerPositions[hand];previousRotations[hand]=controllerRotations[hand];
        }
        private bool diagnosticInput;
        private readonly string[] diagnosticTargets=new string[3];
        private readonly float[] diagnosticValues={.5f,.5f,.5f};
        private readonly bool[] diagnosticPressed=new bool[3];
        private readonly bool[] diagnosticBackPressed=new bool[2],diagnosticBackValid=new bool[2];
        public void SetDiagnosticPointer(int index,string target,float fraction,bool pressed)
        {
            if(index<0||index>=3)return;diagnosticInput=true;diagnosticTargets[index]=target;diagnosticValues[index]=Mathf.Clamp01(fraction);diagnosticPressed[index]=pressed;
        }
        public void SetDiagnosticStick(int index,Vector2 axis){if(index<0||index>=2)return;diagnosticInput=true;stickAxes[index]=Vector2.ClampMagnitude(axis,1);stickValid[index]=true;}
        public void SetDiagnosticGrip(int index,bool pressed){if(index<0||index>=2)return;diagnosticInput=true;gripHeld[index]=pressed;}
        public void SetDiagnosticBack(int index,bool pressed,bool valid){if(index<0||index>2)return;diagnosticInput=true;for(int hand=0;hand<2;hand++)if(index==2||hand==index){diagnosticBackPressed[hand]=pressed;diagnosticBackValid[hand]=valid;}}
        public void ResetDiagnosticInput(){diagnosticInput=false;for(int i=0;i<3;i++){diagnosticTargets[i]=null;diagnosticPressed[i]=false;}for(int i=0;i<2;i++){stickAxes[i]=Vector2.zero;stickValid[i]=gripHeld[i]=false;diagnosticBackPressed[i]=diagnosticBackValid[i]=false;}backGate.Suppress();CancelAllPointers();}
        private void ProcessDiagnosticInput()
        {
            if(HandleControllerBack(diagnosticBackPressed[0],diagnosticBackValid[0],diagnosticBackPressed[1],diagnosticBackValid[1]))return;
            foreach(var line in rays)line.enabled=false;foreach(var dot in dots)dot.gameObject.SetActive(false);
            for(int index=0;index<3;index++){
                Vector2 point=new Vector2(-500,-500);Canvas targetCanvas=app.UI.ActiveCanvas;
                if(targetCanvas!=null&&!string.IsNullOrEmpty(diagnosticTargets[index]))foreach(var rect in targetCanvas.GetComponentsInChildren<RectTransform>()){
                    if(rect.name!=diagnosticTargets[index]&&rect.GetComponent<RowBinding>()?.Item?.key!=diagnosticTargets[index])continue;
                    var local=new Vector3(Mathf.Lerp(rect.rect.xMin,rect.rect.xMax,diagnosticValues[index]),rect.rect.center.y,0);point=head.WorldToScreenPoint(rect.TransformPoint(local));break;
                }
                Dispatch(index,point,diagnosticPressed[index],Vector2.zero,targetCanvas!=null);
            }
            ProcessStickControls(IsTyping(eventSystem.currentSelectedGameObject),Time.unscaledTime,Time.unscaledDeltaTime);
        }
#endif
        public void Initialize(PlayerApp owner,Camera camera,Canvas target)
        {
            app=owner;head=camera;canvas=target;
            for(int i=0;i<3;i++)pointers[i]=new PointerEventData(eventSystem){pointerId=-100-i,button=PointerEventData.InputButton.Left};
            rayMaterial=CreatePointerMaterial();
            for(int i=0;i<2;i++){
                var ray=new GameObject(i==0?"Left UI Ray":"Right UI Ray");rays[i]=ray.AddComponent<LineRenderer>();rays[i].sharedMaterial=rayMaterial;rays[i].positionCount=2;rays[i].startWidth=.004f;rays[i].endWidth=.002f;
                var dot=GameObject.CreatePrimitive(PrimitiveType.Sphere);dot.name="UI Pointer";Destroy(dot.GetComponent<Collider>());dot.GetComponent<Renderer>().sharedMaterial=rayMaterial;dot.transform.localScale=Vector3.one*.012f;dots[i]=dot.transform;
            }
        }
        public override bool IsModuleSupported()=>true;
        public static Material CreatePointerMaterial()=>new Material(Shader.Find("Unlit/Color")){color=UiFactory.Accent,renderQueue=4000};
        public override bool ShouldActivateModule()=>enabled&&gameObject.activeInHierarchy;
        // Unity may stop Process immediately on focus loss; suppress before its next input tick.
        private void OnApplicationFocus(bool focused){if(!focused){backGate.Suppress();CancelAllPointers();}}
        private void OnApplicationPause(bool paused){if(paused){backGate.Suppress();CancelAllPointers();}}
        public override void Process()
        {
            if(app==null)return;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            long tick=System.Diagnostics.Stopwatch.GetTimestamp();
            if(diagnosticTick!=0)Diagnostics.maxGapMs=Mathf.Max(Diagnostics.maxGapMs,(float)((tick-diagnosticTick)*1000d/System.Diagnostics.Stopwatch.Frequency));
            diagnosticTick=tick;Diagnostics.processFrame=Time.frameCount;Diagnostics.processCount++;Diagnostics.diagnostic=diagnosticInput;
#endif
            if(surfaceRevision!=app.UI.SurfaceRevision||activeCanvas!=app.UI.ActiveCanvas){CancelAllPointers();surfaceRevision=app.UI.SurfaceRevision;activeCanvas=app.UI.ActiveCanvas;}
            if(!app.HasFocus){backGate.Suppress();CancelAllPointers();foreach(var line in rays)line.enabled=false;foreach(var dot in dots)dot.gameObject.SetActive(false);return;}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(diagnosticInput){if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)HandleKeyboardShortcut(Key.Escape,false);else ProcessDiagnosticInput();return;}
#endif
            bool hasController=false,togglePanel=false;
            for(int hand=0;hand<2;hand++){
                var device=controllers[hand]=InputDevices.GetDeviceAtXRNode(hand==0?XRNode.LeftHand:XRNode.RightHand);
                bool valid=device.isValid&&device.TryGetFeatureValue(CommonUsages.devicePosition,out controllerPositions[hand])&&device.TryGetFeatureValue(CommonUsages.deviceRotation,out controllerRotations[hand]);
                if(device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)&&!tracked)valid=false;
                controllerValid[hand]=valid;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                RecordPose(hand,valid);
#endif
                stickValid[hand]=valid&&device.TryGetFeatureValue(CommonUsages.primary2DAxis,out stickAxes[hand]);
                device.TryGetFeatureValue(CommonUsages.gripButton,out bool gripButton);device.TryGetFeatureValue(CommonUsages.grip,out float grip);
                gripHeld[hand]=valid&&(gripButton||grip>.6f);
                device.TryGetFeatureValue(CommonUsages.secondaryButton,out backPressed[hand]);
                device.TryGetFeatureValue(CommonUsages.primaryButton,out bool primary);device.TryGetFeatureValue(CommonUsages.menuButton,out bool menu);bool toggle=primary||menu;
                if(valid&&toggle&&!menuHeld[hand])togglePanel=true;menuHeld[hand]=toggle;
            }
            // Sample both hands before changing surfaces so simultaneous Y/B presses return once.
            if(HandleControllerBack(backPressed[0],controllerValid[0],backPressed[1],controllerValid[1]))return;
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){HandleKeyboardShortcut(Key.Escape,false);return;}
            if(togglePanel){app.UI.Toggle();CancelAllPointers();surfaceRevision=app.UI.SurfaceRevision;activeCanvas=app.UI.ActiveCanvas;}
            for(int hand=0;hand<2;hand++){
                var device=controllers[hand];bool valid=controllerValid[hand];
                if(!valid){rays[hand].enabled=false;dots[hand].gameObject.SetActive(false);CancelPointer(pointers[hand]);held[hand]=false;triggerGate.Suppress(hand);continue;}
                hasController=true;
                Transform origin=head.transform.parent;Vector3 position=controllerPositions[hand];Quaternion rotation=controllerRotations[hand];
                Vector3 world=origin!=null?origin.TransformPoint(position):position;Quaternion worldRotation=origin!=null?origin.rotation*rotation:rotation;
                Canvas targetCanvas=app.UI.ActiveCanvas;
                Ray ray=new Ray(world,worldRotation*Vector3.forward);float distance=2;bool intersects=targetCanvas!=null&&new Plane(targetCanvas.transform.forward,targetCanvas.transform.position).Raycast(ray,out distance);
                // The UI lies on one plane. Project the controller intersection for uGUI's GraphicRaycaster.
                float length=intersects?Mathf.Clamp(distance,0,10):2;
                Vector3 point=ray.GetPoint(length);Vector3 screen=head.WorldToScreenPoint(point);bool usable=intersects&&distance>0&&distance<10&&screen.z>0;
                device.TryGetFeatureValue(CommonUsages.trigger,out float trigger);device.TryGetFeatureValue(CommonUsages.triggerButton,out bool triggerButton);
                device.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 stick);
                bool pressed=trigger>.6f||triggerButton;bool wasHeld=held[hand];Dispatch(hand,screen,pressed,Vector2.zero,usable);
                rays[hand].enabled=app.UI.AnyVisible;dots[hand].gameObject.SetActive(usable&&pointers[hand].pointerCurrentRaycast.gameObject!=null);
                rays[hand].SetPosition(0,world);rays[hand].SetPosition(1,point);dots[hand].position=point-ray.direction*.005f;
                if(pressed&&!wasHeld&&pointers[hand].pointerCurrentRaycast.gameObject!=null)device.SendHapticImpulse(0,.18f,.025f);
            }
            if(Mouse.current!=null&&(!hasController||Application.isEditor))Dispatch(2,Mouse.current.position.ReadValue(),Mouse.current.leftButton.isPressed,Mouse.current.scroll.ReadValue()/120,app.UI.AnyVisible);
            bool typing=IsTyping(eventSystem.currentSelectedGameObject);
            ProcessStickControls(typing,Time.unscaledTime,Time.unscaledDeltaTime);
            if(eventSystem.currentSelectedGameObject!=null)ExecuteEvents.Execute(eventSystem.currentSelectedGameObject,GetBaseEventData(),ExecuteEvents.updateSelectedHandler);
            ProcessKeyboard(typing);
        }
        private bool HandleControllerBack(bool leftPressed,bool leftValid,bool rightPressed,bool rightValid)
        {
            bool left=BackPressed(0,leftPressed,leftValid),right=BackPressed(1,rightPressed,rightValid);
            if(!left&&!right)return false;
            HandleBack();return true;
        }
        private bool BackPressed(int hand,bool pressed,bool valid){if(!valid){backGate.Suppress(hand);return false;}return backGate.Update(hand,pressed);}
        private void HandleBack()
        {
            if(!app.HasFocus){backGate.Suppress();return;}
            CancelAllPointers();eventSystem?.SetSelectedGameObject(null);app.UI.Back();
            surfaceRevision=app.UI.SurfaceRevision;activeCanvas=app.UI.ActiveCanvas;
            foreach(var line in rays)if(line!=null)line.enabled=false;foreach(var dot in dots)if(dot!=null)dot.gameObject.SetActive(false);
        }
        private bool castRouting;
        private int adjustmentRouting;
        private void ProcessStickControls(bool typing,float now,float deltaTime)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Diagnostics.leftGrip=gripHeld[0];Diagnostics.rightGrip=gripHeld[1];Diagnostics.leftStick=stickAxes[0];Diagnostics.rightStick=stickAxes[1];
#endif
            bool blocked=!app.HasFocus||typing||held[0]||held[1]||held[2]||pointers[0].dragging||pointers[1].dragging||pointers[2].dragging;
            bool routeCast=app.UI.CastHovered;if(routeCast!=castRouting){transport.Suppress();castRouting=routeCast;}
            int grips=(gripHeld[0]&&stickValid[0]?1:0)|(gripHeld[1]&&stickValid[1]?2:0);
            bool panel=app.UI.Visible,video=!app.UI.AnyVisible&&app.Current!=null;
            bool adjusting=grips!=0&&(panel||video),scaling=adjusting&&(panel||app.Presenter.Settings.projection==Projection.Flat);
            int routing=adjusting?grips|(panel?4:scaling?8:16):0;
            if(routing!=adjustmentRouting){transport.SuppressActive();app.CommitViewDistance();adjustmentRouting=routing;}
            int skip=transport.Update(stickAxes[0],stickValid[0]&&(!adjusting||gripHeld[0]),stickAxes[1],stickValid[1]&&(!adjusting||gripHeld[1]),blocked,now,adjusting?!scaling:app.UI.BlocksTransport,scaling);
            if(skip!=0){if(routeCast)app.UI.ScrollCast(skip);else app.Skip(skip);app.UI.LastInteraction=now;}
            bool vertical=!blocked&&Mathf.Abs(transport.Scroll)>.25f;
            bool horizontal=!blocked&&Mathf.Abs(transport.Horizontal)>.25f;
            if(adjusting&&(vertical||horizontal)){
                if(panel){if(vertical)app.AdjustMainPanelDistance(transport.Scroll,deltaTime);else app.AdjustMainPanelScale(transport.Horizontal,deltaTime);app.UI.LastInteraction=now;}
                else if(vertical)app.AdjustViewDistance(transport.Scroll,deltaTime);
                else app.AdjustViewScale(transport.Horizontal,deltaTime);
            }else{
                if(vertical&&!adjusting&&app.UI.AnyVisible&&!app.UI.BlocksScrolling){app.UI.ScrollList(transport.Scroll);app.UI.LastInteraction=now;}
                app.CommitViewDistance();
            }
        }
        private void ProcessKeyboard(bool typing)
        {
            if(Keyboard.current==null)return;
            if(Keyboard.current.tabKey.wasPressedThisFrame)HandleKeyboardShortcut(Key.Tab,typing);
            if(Keyboard.current.spaceKey.wasPressedThisFrame)HandleKeyboardShortcut(Key.Space,typing);
            if(Keyboard.current.leftArrowKey.wasPressedThisFrame)HandleKeyboardShortcut(Key.LeftArrow,typing);
            if(Keyboard.current.rightArrowKey.wasPressedThisFrame)HandleKeyboardShortcut(Key.RightArrow,typing);
        }
        private void HandleKeyboardShortcut(Key key,bool typing)
        {
            if(key==Key.Escape){HandleBack();return;}
            if(typing||!app.HasFocus)return;
            if(key==Key.Tab){app.UI.Toggle();return;}
            if(app.UI.BlocksTransport)return;
            if(key==Key.Space)app.TogglePlay();else if(key==Key.LeftArrow)app.Skip(-10);else if(key==Key.RightArrow)app.Skip(10);
        }
        public static bool IsTyping(GameObject selected){var field=selected!=null?selected.GetComponent<TMP_InputField>():null;return field!=null&&field.isActiveAndEnabled&&field.isFocused;}
        private void Dispatch(int index,Vector2 position,bool pressed,Vector2 scroll,bool usable)
        {
            bool physicalDown=triggerGate.Update(index,pressed);
            PointerEventData data=pointers[index];Vector2 previous=data.position;data.position=position;data.delta=position-previous;data.scrollDelta=scroll;
            if(!usable&&held[index])CancelPointer(data);
            hits.Clear();if(usable)eventSystem.RaycastAll(data,hits);data.pointerCurrentRaycast=FindFirstRaycast(hits);GameObject target=data.pointerCurrentRaycast.gameObject;
            if(physicalDown&&!app.UI.Visible&&target==null){
                app.UI.ToggleQuick();CancelAllPointers();held[index]=pressed;surfaceRevision=app.UI.SurfaceRevision;activeCanvas=app.UI.ActiveCanvas;return;
            }
            if(triggerGate.Consumed(index)){CancelPointer(data);held[index]=pressed;return;}
            HandlePointerExitAndEnter(data,target);
            if(target!=null)ExecuteEvents.ExecuteHierarchy(target,data,ExecuteEvents.pointerMoveHandler);
            if(target!=null&&(data.delta.sqrMagnitude>1||pressed||scroll.sqrMagnitude>.001f))app.UI.LastInteraction=Time.unscaledTime;
            bool down=physicalDown,up=!pressed&&held[index];held[index]=pressed;
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
            if(data==null)return;
            if(data.pointerPress!=null){data.pointerPress.GetComponent<SeekGesture>()?.Cancel(data.pointerId);data.pointerPress.GetComponent<QuickSeekSurface>()?.Cancel(data.pointerId);}
            if(data.pointerDrag!=null){data.pointerDrag.GetComponent<SeekGesture>()?.Cancel(data.pointerId);data.pointerDrag.GetComponent<QuickSeekSurface>()?.Cancel(data.pointerId);}
            ExecuteEvents.Execute(data.pointerPress,data,ExecuteEvents.pointerUpHandler);
            if(data.dragging)ExecuteEvents.Execute(data.pointerDrag,data,ExecuteEvents.endDragHandler);
            data.eligibleForClick=false;data.dragging=false;data.pointerPress=null;data.rawPointerPress=null;data.pointerDrag=null;data.pointerCurrentRaycast=default;HandlePointerExitAndEnter(data,null);
        }
        private void CancelAllPointers(){for(int i=0;i<pointers.Length;i++){CancelPointer(pointers[i]);held[i]=false;}triggerGate.Suppress();transport.Suppress();app?.CommitViewDistance();}
        protected override void OnDestroy(){base.OnDestroy();foreach(var ray in rays)if(ray!=null)Destroy(ray.gameObject);foreach(var dot in dots)if(dot!=null)Destroy(dot.gameObject);if(rayMaterial!=null)Destroy(rayMaterial);}
    }
    // Physical edge state is independent of uGUI press state and survives surface changes.
    public sealed class TriggerPressGate
    {
        private readonly bool[] held=new bool[3],consumed=new bool[3];
        public bool Update(int index,bool pressed){bool down=pressed&&!held[index]&&!consumed[index];held[index]=pressed;if(!pressed)consumed[index]=false;return down;}
        public bool Consumed(int index)=>consumed[index];
        public void Suppress(){for(int i=0;i<3;i++)consumed[i]=true;}
        public void Suppress(int index)=>consumed[index]=true;
    }
}
