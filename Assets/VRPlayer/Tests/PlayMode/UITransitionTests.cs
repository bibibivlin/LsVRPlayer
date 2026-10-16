using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace VRPlayer.PlayModeTests
{
    public sealed class UITransitionTests
    {
        private GameObject root;
        private TMP_FontAsset priorFont,testFont;
        private static readonly FieldInfo FontCache=typeof(UiFactory).GetField("font",BindingFlags.Static|BindingFlags.NonPublic);

        [SetUp] public void Setup()
        {
            // Dynamic TMP atlases must remain test-owned, never dirty a font asset.
            priorFont=(TMP_FontAsset)FontCache.GetValue(null);
            testFont=TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular"));
            testFont.hideFlags=HideFlags.HideAndDontSave;FontCache.SetValue(null,testFont);
            root=new GameObject("UI transition fixture",typeof(RectTransform),typeof(Canvas));
            root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        }

        [TearDown] public void Cleanup()
        {
            root.SetActive(false);UnityEngine.Object.Destroy(root);FontCache.SetValue(null,priorFont);
            foreach(var texture in testFont.atlasTextures)if(texture!=null)UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(testFont.material);UnityEngine.Object.Destroy(testFont);
        }

        private static void AssertTint(Button button,Color expected)
        {
            Color actual=button.targetGraphic.canvasRenderer.GetColor();
            Assert.That(actual.r,Is.EqualTo(expected.r).Within(.001),button.name+" red tint");
            Assert.That(actual.g,Is.EqualTo(expected.g).Within(.001),button.name+" green tint");
            Assert.That(actual.b,Is.EqualTo(expected.b).Within(.001),button.name+" blue tint");
            Assert.That(actual.a,Is.EqualTo(expected.a).Within(.001),button.name+" alpha");
        }

        [UnityTest] public IEnumerator CardActionRemainsHoveredWithAnotherPointerAndResetsWhenThePanelCloses()
        {
            var card=UiFactory.Button(root.transform,"Card",0,0,240,220,()=>{});
            var button=UiFactory.IconButton(card.transform,"Watched",QuickIcon.Check,186,82,()=>{},size:44);
            var group=button.gameObject.AddComponent<CanvasGroup>();var hover=card.gameObject.AddComponent<UiHoverAction>();hover.Initialize(group);
            var first=new PointerEventData(EventSystem.current){pointerId=-10};var second=new PointerEventData(EventSystem.current){pointerId=-11};
            Assert.That(group.alpha,Is.Zero);Assert.That(button.IsInteractable(),Is.False);
            hover.OnPointerEnter(first);hover.OnPointerEnter(second);hover.OnPointerExit(first);
            yield return null;
            Assert.That(group.alpha,Is.EqualTo(1));Assert.That(button.IsInteractable(),Is.True);
            root.SetActive(false);root.SetActive(true);yield return null;
            Assert.That(group.alpha,Is.Zero);Assert.That(group.blocksRaycasts,Is.False);Assert.That(button.IsInteractable(),Is.False);
            hover.OnPointerEnter(first);hover.OnPointerExit(first);
            Assert.That(group.alpha,Is.Zero);Assert.That(group.blocksRaycasts,Is.False);
        }

        [UnityTest] public IEnumerator SourceButtonsUseTheirFinalPaletteBeforeTheFirstFrame()
        {
            var inactive=UiFactory.SourceButton(root.transform,"Local",QuickIcon.Local,0,0,180,64,()=>{},false);
            var active=UiFactory.SourceButton(root.transform,"Emby",QuickIcon.Play,0,72,180,64,()=>{},true);
            AssertTint(inactive,Color.clear);AssertTint(active,UiTheme.SelectedMuted);
            yield return null;
            AssertTint(inactive,Color.clear);AssertTint(active,UiTheme.SelectedMuted);
        }

        [UnityTest] public IEnumerator DirectoryRowsUseTheirBoundPaletteBeforeTheFirstFrameAndAfterReuse()
        {
            var container=UiFactory.Rect(root.transform,"Directory",0,0,900,240);
            var list=container.gameObject.AddComponent<VirtualFileList>();list.Initialize(900,240,directoryStyle:true);
            Action open=()=>{};
            list.SetRows(new[]{new BrowserRow{key="header",label="Saved shares"},new BrowserRow{key="folder",label="Folder",open=open}});
            void Check()
            {
                foreach(var binding in container.GetComponentsInChildren<RowBinding>()){
                    var button=binding.GetComponentInChildren<Button>();
                    AssertTint(button,binding.Item.selected?UiTheme.SelectedMuted:Color.clear);
                }
            }
            Check();yield return null;Check();
            list.SetRows(new[]{new BrowserRow{key="replacement",label="Selected",open=open,selected=true},new BrowserRow{key="other-header",label="Discovered"}});
            Check();yield return null;Check();
        }

        [UnityTest] public IEnumerator IconAndSwatchBackgroundsDoNotFlashOnCreation()
        {
            var quick=QuickUiFactory.IconButton(root.transform,"Quick",QuickIcon.Mode,0,0,()=>{});
            var transport=UiFactory.IconButton(root.transform,"Previous",QuickIcon.Previous,100,0,()=>{});UiFactory.ConfigureTransport(transport,false);
            var swatch=UiFactory.ColorSwatch(root.transform,"Swatch",Color.green,false,180,0,()=>{});
            foreach(var button in new[]{quick,transport,swatch})AssertTint(button,Color.clear);
            yield return null;
            foreach(var button in new[]{quick,transport,swatch})AssertTint(button,Color.clear);
        }

        [UnityTest] public IEnumerator RefreshingNavigationPalettePreservesPointerStateAndHoverFade()
        {
            var button=UiFactory.TabButton(root.transform,"Tab",0,0,120,64,()=>{});
            float duration=button.colors.fadeDuration;
            var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            button.OnPointerEnter(pointer);UiFactory.SetAccent(button,true);AssertTint(button,Color.clear);Assert.That(button.GetComponentInChildren<TextMeshProUGUI>().color,Is.EqualTo(UiTheme.Accent));
            button.OnPointerDown(pointer);UiFactory.SetAccent(button,false);AssertTint(button,Color.clear);
            Assert.That(button.colors.fadeDuration,Is.EqualTo(duration).And.GreaterThan(0));
            yield return null;AssertTint(button,Color.clear);
            button.OnPointerUp(pointer);button.OnPointerExit(pointer);
            yield return new WaitForSecondsRealtime(duration+.05f);
            AssertTint(button,Color.clear);
        }

        [UnityTest] public IEnumerator TabHoverChangesOnlyTextAndKeepsTheFullClickableRectangle()
        {
            var first=UiFactory.TabButton(root.transform,"First",0,0,118,64,()=>{});
            var second=UiFactory.TabButton(root.transform,"Second",126,0,118,64,()=>{});
            UiFactory.SetAccent(first,true);
            var a=new PointerEventData(EventSystem.current){pointerId=-10};var b=new PointerEventData(EventSystem.current){pointerId=-11};
            second.OnPointerEnter(a);second.OnPointerEnter(b);second.OnPointerExit(a);
            Assert.That(second.GetComponentInChildren<TextMeshProUGUI>().color,Is.EqualTo(UiTheme.Accent));
            AssertTint(second,Color.clear);Assert.That(second.targetGraphic.color.a,Is.Zero);
            second.OnPointerExit(b);Assert.That(second.GetComponentInChildren<TextMeshProUGUI>().color,Is.EqualTo(UiTheme.Text));
            var rect=(RectTransform)second.transform;Assert.That(rect.rect.size,Is.EqualTo(new Vector2(118,64)));
            var raycaster=root.AddComponent<GraphicRaycaster>();
            yield return null;Canvas.ForceUpdateCanvases();
            foreach(var point in new[]{new Vector2(2,-2),new Vector2(116,-62)}){
                var hits=new System.Collections.Generic.List<RaycastResult>();a.position=rect.TransformPoint(point);raycaster.Raycast(a,hits);
                Assert.That(hits.Exists(hit=>hit.gameObject==second.gameObject),Is.True,"Tab edges must remain clickable");
            }
            Assert.That(root.transform.Find("Selected underline").GetComponent<Graphic>().raycastTarget,Is.False);
        }

        [UnityTest] public IEnumerator SharedUnderlineMovesWhilePausedAndRetargetsWithoutJumping()
        {
            var group=UiFactory.Rect(root.transform,"Tabs",0,0,480,64);
            var first=UiFactory.TabButton(group,"First",0,0,118,64,()=>{});
            var second=UiFactory.TabButton(group,"Second",160,0,118,64,()=>{});
            var third=UiFactory.TabButton(group,"Third",320,0,118,64,()=>{});
            UiFactory.SetAccent(first,true);var marker=(RectTransform)group.Find("Selected underline");
            Assert.That(group.childCount,Is.EqualTo(4));Vector2 start=marker.anchoredPosition;
            yield return null;
            float scale=Time.timeScale;
            try{
                Time.timeScale=0;UiFactory.SetAccent(third,true);Assert.That(marker.anchoredPosition,Is.EqualTo(start));
                yield return new WaitForSecondsRealtime(.06f);Assert.That(marker.anchoredPosition.x,Is.GreaterThan(start.x).And.LessThan(start.x+320));
                Vector2 before=marker.anchoredPosition;UiFactory.SetAccent(second,true);Assert.That(marker.anchoredPosition,Is.EqualTo(before));
                yield return new WaitForSecondsRealtime(.65f);Assert.That(marker.anchoredPosition.x,Is.EqualTo(start.x+160).Within(.1));
                UiFactory.SetAccent(third,true);group.gameObject.SetActive(false);group.gameObject.SetActive(true);
                Assert.That(marker.anchoredPosition.x,Is.EqualTo(start.x+320).Within(.01));
            }finally{Time.timeScale=scale;}
        }

        [UnityTest] public IEnumerator SwitchAppliesImmediatelyButMovesAndBlendsWhilePaused()
        {
            bool value=false;var control=UiFactory.ToggleRow(root.transform,"Loop","Loop",()=>value,0,0,480,next=>value=next);
            var rail=control.transform.Find("Switch").GetComponent<RoundedPanelGraphic>();var knob=(RectTransform)rail.transform.Find("Knob");
            float start=knob.anchoredPosition.x;Color off=rail.color;
            yield return null;float scale=Time.timeScale;
            try{
                Time.timeScale=0;control.onClick.Invoke();Assert.That(value,Is.True);Assert.That(control.Value,Is.True);
                Assert.That(knob.anchoredPosition.x,Is.EqualTo(start));Assert.That(rail.color,Is.EqualTo(off));
                yield return new WaitForSecondsRealtime(.06f);
                Assert.That(knob.anchoredPosition.x,Is.GreaterThan(start).And.LessThan(29));
                Assert.That(rail.color.g,Is.GreaterThan(off.g).And.LessThan(UiTheme.Selected.g));
                yield return new WaitForSecondsRealtime(.55f);
                Assert.That(knob.anchoredPosition.x,Is.EqualTo(29).Within(.01));Assert.That(rail.color,Is.EqualTo(UiTheme.Selected));
            }finally{Time.timeScale=scale;}
        }

        [UnityTest] public IEnumerator SwitchRapidReversalKeepsItsPositionAndExternalChangesStayInSync()
        {
            bool value=false;var control=UiFactory.ToggleRow(root.transform,"Loop","Loop",()=>value,0,0,480,next=>value=next);
            var knob=(RectTransform)control.transform.Find("Switch/Knob");yield return null;
            control.onClick.Invoke();yield return new WaitForSecondsRealtime(.05f);
            float before=knob.anchoredPosition.x;control.onClick.Invoke();Assert.That(value,Is.False);Assert.That(knob.anchoredPosition.x,Is.EqualTo(before));
            float end=Time.realtimeSinceStartup+.5f;
            while(Time.realtimeSinceStartup<end){Assert.That(knob.anchoredPosition.x,Is.InRange(3,29));yield return null;}
            Assert.That(knob.anchoredPosition.x,Is.EqualTo(3).Within(.01));
            value=true;yield return new WaitForSecondsRealtime(.6f);Assert.That(control.Value,Is.True);Assert.That(knob.anchoredPosition.x,Is.EqualTo(29).Within(.01));
            control.onClick.Invoke();yield return null;root.SetActive(false);value=true;root.SetActive(true);
            Assert.That(control.Value,Is.True);Assert.That(knob.anchoredPosition.x,Is.EqualTo(29).Within(.01));
        }

        [UnityTest] public IEnumerator SwitchHoverAndDisabledStateNeverPaintTheHitRectangle()
        {
            bool value=false;var control=UiFactory.ToggleRow(root.transform,"Loop","Loop",()=>value,0,0,480,next=>value=next);
            var pointer=new PointerEventData(EventSystem.current){pointerId=-10};control.OnPointerEnter(pointer);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(control.targetGraphic.color,Is.EqualTo(Color.clear));
            control.interactable=false;control.OnPointerClick(pointer);Assert.That(value,Is.False);
            Assert.That(control.GetComponentInChildren<TextMeshProUGUI>().color,Is.EqualTo(UiTheme.Muted));
            Assert.That(control.targetGraphic.color,Is.EqualTo(Color.clear));
        }
    }
}
