using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class UIRedesignTests
    {
        private GameObject root,head,events;
        private PlayerApp app;private PlayerUI ui;private AndroidMediaBackend backend;private string path;
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void Call(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
        private bool BuildCard()=> (bool)typeof(EmbyBrowserUI).GetMethod("BuildNextCard",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Field<EmbyBrowserUI>(ui,"embyBrowser"),null);
        private void FinishCards(){for(int i=0;i<100&&BuildCard();i++){} }
        private void Set(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
        [SetUp] public void Setup()
        {
            path=Path.Combine(Path.GetTempPath(),"VRPlayer-ui-"+Guid.NewGuid()+".json");
            root=new GameObject("UI redesign tests");root.SetActive(false);app=root.AddComponent<PlayerApp>();
            var store=new PlayerStore(path);Set("Store",store);backend=new AndroidMediaBackend();Set("Backend",backend);
            Set("Presenter",root.AddComponent<VideoPresenter>());Set("RenderQuality",root.AddComponent<RenderQualityController>());
            Set("Generation",1);backend.Snapshot.generation=1;backend.Snapshot.duration=120000;backend.Snapshot.position=30000;
            Set("Current",store.Touch(new FileEntry{name="Fixture.mp4",uri="file:///fixture.mp4"},""));
            head=new GameObject("Test camera",typeof(Camera));ui=root.AddComponent<PlayerUI>();Set("UI",ui);ui.Initialize(app,head.GetComponent<Camera>());
            events=new GameObject("Test events",typeof(EventSystem));Canvas.ForceUpdateCanvases();
        }
        [TearDown] public void Cleanup()
        {
            var quick=ui.Quick.Canvas;typeof(QuickControlsUI).GetProperty("Canvas").SetValue(ui.Quick,null);UnityEngine.Object.DestroyImmediate(quick.gameObject);
            var full=ui.Canvas;typeof(PlayerUI).GetProperty("Canvas").SetValue(ui,null);UnityEngine.Object.DestroyImmediate(full.gameObject);
            UnityEngine.Object.DestroyImmediate(Field<RectTransform>(ui,"transportRoot").gameObject);
            Set("Presenter",null);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head);UnityEngine.Object.DestroyImmediate(events);
            if(File.Exists(path))File.Delete(path);
        }
        private PointerEventData Pointer(RectTransform rect,float x,float y)
        {
            var world=rect.TransformPoint(new Vector3(rect.rect.xMin+x,rect.rect.yMax-y,0));
            var ray=new RaycastResult{gameObject=rect.gameObject,module=ui.Quick.Canvas.GetComponent<GraphicRaycaster>()};
            return new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-10,position=head.GetComponent<Camera>().WorldToScreenPoint(world),pointerCurrentRaycast=ray,pointerPressRaycast=ray};
        }
        [TestCase(70,true)][TestCase(170,false)]
        public void OnlyUpperQuickPanelReceivesSeekHits(float y,bool seekable)
        {
            ui.Hide();ui.ShowQuick();var camera=head.GetComponent<Camera>();var target=new RenderTexture(1280,960,24);camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();var bar=Field<RectTransform>(ui.Quick,"bar");
            var hits=new List<RaycastResult>();ui.Quick.Canvas.GetComponent<GraphicRaycaster>().Raycast(Pointer(bar,400,y),hits);
            Assert.That(hits.Any(hit=>hit.gameObject.name=="Seek surface"),Is.EqualTo(seekable));camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);
        }
        [Test] public void DragPreviewDoesNotReplaceActualPlaybackProgress()
        {
            ui.Hide();ui.ShowQuick();var seek=ui.Quick.Canvas.GetComponentInChildren<QuickSeekSurface>();
            var pointer=Pointer((RectTransform)seek.transform,1080,55);seek.OnPointerDown(pointer);seek.OnDrag(pointer);Call(ui.Quick,"LateUpdate");
            Assert.That(Field<RectTransform>(ui.Quick,"progress").rect.width,Is.EqualTo(360).Within(.1));seek.Cancel();
        }
        [Test] public void OpeningQuickPopupCancelsSeekDragAndItsPreview()
        {
            ui.Hide();ui.ShowQuick();var surface=ui.Quick.Canvas.GetComponentInChildren<QuickSeekSurface>();
            surface.OnPointerDown(Pointer((RectTransform)surface.transform,700,55));Assert.That(surface.Dragging,Is.True);
            ui.Quick.TogglePopup("mode");Assert.That(surface.Dragging,Is.False);
            Assert.That(Field<RectTransform>(ui.Quick,"previewBadge").gameObject.activeSelf,Is.False);
        }
        [Test] public void BeamAndTimestampNeverInterceptControllerRays()
        {
            foreach(string name in new[]{"previewLine","previewBadge"}){
                var rect=Field<RectTransform>(ui.Quick,name);Assert.That(rect.GetComponentsInChildren<Graphic>(true).All(graphic=>!graphic.raycastTarget));
            }
            Assert.That(Field<RectTransform>(ui.Quick,"previewLine").GetComponent<SeekBeamGraphic>(),Is.Not.Null);
        }
        [Test] public void AllCastMembersRemainAvailableInABoundedHorizontalPool()
        {
            Set("Source",FileSource.Emby);app.EmbyState.page=EmbyPage.Details;app.EmbyState.selected=new EmbyItem{Id="movie",Name="Fixture",Type="Movie",People=Enumerable.Range(0,120).Select(i=>new EmbyPerson{Id="person"+i,Name="Actor "+i}).ToArray()};
            ui.SelectTab("文件");var browser=Field<EmbyBrowserUI>(ui,"embyBrowser");
            Assert.That(Field<EmbyPerson[]>(browser,"castPeople").Length,Is.EqualTo(120));
            var cast=Field<RectTransform>(browser,"castBody");var scroll=Field<ListScrollRect>(browser,"castScroll");
            Assert.That(cast.childCount,Is.LessThanOrEqualTo(Mathf.CeilToInt(scroll.viewport.rect.width/216)+2));
            cast.anchoredPosition=new Vector2(-(cast.rect.width-scroll.viewport.rect.width),0);Call(browser,"UpdateCast");
            Assert.That(cast.GetComponentsInChildren<RowBinding>().Any(binding=>binding.Item.key.EndsWith(":person119")),Is.True);
        }
        [TestCase(EmbyLibraryView.Poster)][TestCase(EmbyLibraryView.Banner)][TestCase(EmbyLibraryView.Disc)][TestCase(EmbyLibraryView.Logo)][TestCase(EmbyLibraryView.Thumb)][TestCase(EmbyLibraryView.List)][TestCase(EmbyLibraryView.Table)]
        public void LibraryCardsFitSourceSidebarWithoutStretchingArtwork(EmbyLibraryView view)
        {
            var baseline=EmbyLibraryViews.Layout(view);var compact=EmbyLibraryViews.Layout(view,1128);
            Assert.That(compact.Columns*compact.Width+(compact.Columns-1)*18,Is.LessThanOrEqualTo(1100.1));
            Assert.That(compact.ArtworkWidth/compact.ArtworkHeight,Is.EqualTo(baseline.ArtworkWidth/baseline.ArtworkHeight).Within(.001));
        }
        [Test] public void HiddenInfoPageDoesNotConsumeQuickSubtitleScrolling()
        {
            ui.SelectTab("信息");Call(ui,"Update");var info=Field<RectTransform>(ui,"infoBody");info.anchoredPosition=new Vector2(0,80);
            ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("subtitles");ui.ScrollList(-1);
            Assert.That(info.anchoredPosition.y,Is.EqualTo(80));
        }
        [Test] public void EmbyHomeFitsFiveLandscapeResumeCardsAcrossTheSourcePage()
        {
            Set("Source",FileSource.Emby);app.EmbyState.page=EmbyPage.Home;
            app.EmbyState.resume=Enumerable.Range(0,6).Select(i=>new EmbyItem{Id="resume"+i,Name="Fixture "+i,Type=i%2==0?"Movie":"Episode",SeriesName="Series",ImageTags=new EmbyImageTags{Primary="poster",Thumb="thumbnail"}}).ToArray();
            ui.SelectTab("文件");var browser=Field<EmbyBrowserUI>(ui,"embyBrowser");var body=Field<RectTransform>(browser,"body");
            FinishCards();
            var cards=body.GetComponentsInChildren<Button>().Where(button=>button.name.StartsWith("Emby resume ")).Select(button=>(RectTransform)button.transform).ToArray();
            Assert.That(cards.Length,Is.EqualTo(6));Assert.That(cards.Take(5).All(card=>Mathf.Abs(card.anchoredPosition.y-cards[0].anchoredPosition.y)<.1f),Is.True);
            Assert.That(cards[4].anchoredPosition.x+cards[4].rect.width,Is.EqualTo(body.rect.width-14).Within(.1));
            foreach(var card in cards){var art=(RectTransform)card.Find("Artwork");Assert.That(art.rect.width/art.rect.height,Is.EqualTo(16f/9).Within(.001));}
            Assert.That(cards[5].anchoredPosition.y,Is.LessThan(cards[0].anchoredPosition.y));
        }
        [TestCase(false)][TestCase(true)] public void MovieActionsShareTouchSizeAndKeepCdResumeSeparate(bool multipart)
        {
            Set("Source",FileSource.Emby);Set("ActiveEmbyAccount",new EmbyAccount{id="fixture"});
            app.EmbyState.page=EmbyPage.Details;app.EmbyState.selected=new EmbyItem{Id="main",Name="Fixture",Type="Movie",UserData=new EmbyUserData{PlaybackPositionTicks=100000000,LastPlayedDate="2026-10-16T01:00:00Z"}};
            if(multipart)app.EmbyState.parts=new[]{app.EmbyState.selected,new EmbyItem{Id="second",Type="Video",ExtraType="AdditionalPart",UserData=new EmbyUserData{PlaybackPositionTicks=200000000,LastPlayedDate="2026-10-16T02:00:00Z"}}};
            ui.SelectTab("文件");var body=Field<RectTransform>(Field<EmbyBrowserUI>(ui,"embyBrowser"),"body");
            var buttons=body.GetComponentsInChildren<Button>().Where(button=>button.name.StartsWith("Emby action ")).ToArray();
            Assert.That(buttons.Length,Is.EqualTo(multipart?4:3));
            var size=((RectTransform)buttons[0].transform).rect.size;
            Assert.That(size.y,Is.EqualTo(64));Assert.That(size.x,Is.GreaterThanOrEqualTo(230));
            Assert.That(buttons.All(button=>((RectTransform)button.transform).rect.size==size),Is.True);
            Assert.That(buttons.Any(button=>button.name.Contains("标记为已播放")),Is.True);
            Assert.That(buttons.Any(button=>button.name.Contains("仅继续 CD1")),Is.EqualTo(multipart));
            foreach(var button in buttons){var rect=(RectTransform)button.transform;Assert.That(rect.anchoredPosition.x+rect.rect.width,Is.LessThanOrEqualTo(body.rect.width-14+.1f));}
        }
        [Test] public void ResumeCardsExposeTheirOwnWatchedActionWithoutOpeningDetails()
        {
            Set("Source",FileSource.Emby);app.EmbyState.page=EmbyPage.Home;app.EmbyState.resume=new[]{new EmbyItem{Id="main",Type="Movie"}};
            ui.SelectTab("文件");var body=Field<RectTransform>(Field<EmbyBrowserUI>(ui,"embyBrowser"),"body");
            FinishCards();
            var card=body.GetComponentsInChildren<Button>().Single(button=>button.name=="Emby resume main");
            var mark=card.GetComponentsInChildren<Button>().Single(button=>button.name=="Emby played main");
            Assert.That(((RectTransform)mark.transform).rect.size,Is.EqualTo(new Vector2(44,44)));Assert.That(mark.GetComponent<UiIconTooltip>(),Is.Not.Null);
            Assert.That(mark.GetComponent<CanvasGroup>().alpha,Is.Zero);Assert.That(mark.GetComponent<CanvasGroup>().blocksRaycasts,Is.False);
            card.GetComponent<UiHoverAction>().OnPointerEnter(new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-10});
            Assert.That(mark.GetComponent<CanvasGroup>().alpha,Is.EqualTo(1));Assert.That(mark.GetComponent<CanvasGroup>().blocksRaycasts,Is.True);
            mark.onClick.Invoke();Assert.That(app.EmbyState.page,Is.EqualTo(EmbyPage.Home));
        }
        [TestCase(EmbyLibraryView.Poster)][TestCase(EmbyLibraryView.Thumb)][TestCase(EmbyLibraryView.Banner)]
        [TestCase(EmbyLibraryView.List)][TestCase(EmbyLibraryView.Table)][TestCase(EmbyLibraryView.Disc)][TestCase(EmbyLibraryView.Logo)]
        public void ArtworkCoversItsFrameWithoutStretchingWhileTransparentArtStaysComplete(EmbyLibraryView view)
        {
            Set("Source",FileSource.Emby);app.EmbyState.page=EmbyPage.Library;app.EmbyState.parent=new EmbyItem{Id="library"};
            app.EmbyState.items=new[]{new EmbyItem{Id="image",Name="Fixture",Type="Movie",ImageTags=new EmbyImageTags{Primary="p",Thumb="t",Banner="b",Disc="d",Logo="l"}}};
            app.Store.Settings.embyLibraryViews.Add(new EmbyLibraryViewPreference{libraryId="library",view=view});
            ui.SelectTab("文件");FinishCards();var image=Field<RectTransform>(ui,"content").GetComponentsInChildren<RawImage>().Single();var rect=(RectTransform)image.transform;var holder=(RectTransform)rect.parent;
            var texture=new Texture2D(424,225);
            try{
                typeof(EmbyBrowserUI).GetMethod("Assign",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{image,texture});Canvas.ForceUpdateCanvases();
                Assert.That(rect.rect.width/rect.rect.height,Is.EqualTo(424f/225).Within(.001));
                bool transparent=view==EmbyLibraryView.Disc||view==EmbyLibraryView.Logo;
                if(transparent){Assert.That(rect.rect.width,Is.LessThanOrEqualTo(holder.rect.width+.1));Assert.That(rect.rect.height,Is.LessThanOrEqualTo(holder.rect.height+.1));}
                else{Assert.That(rect.rect.width,Is.GreaterThanOrEqualTo(holder.rect.width-.1));Assert.That(rect.rect.height,Is.GreaterThanOrEqualTo(holder.rect.height-.1));}
                Assert.That(holder.Find("Text").gameObject.activeSelf,Is.False);
                typeof(EmbyBrowserUI).GetMethod("Detach",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{image});
                Assert.That(image.texture,Is.Null);Assert.That(holder.Find("Text").gameObject.activeSelf,Is.True);
            }finally{UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test] public void LargeLibraryCreatesOneCardPerStepInsteadOfBlockingItsInitialRender()
        {
            Set("Source",FileSource.Emby);app.EmbyState.page=EmbyPage.Library;
            app.EmbyState.items=Enumerable.Range(0,1005).Select(i=>new EmbyItem{Id="film"+i,Name="Film "+i,Type="Movie"}).ToArray();
            ui.SelectTab("文件");var browser=Field<EmbyBrowserUI>(ui,"embyBrowser");var body=Field<RectTransform>(browser,"body");
            Assert.That(body.GetComponentsInChildren<RowBinding>().Length,Is.Zero);
            Assert.That(BuildCard(),Is.True);Assert.That(body.GetComponentsInChildren<RowBinding>().Length,Is.EqualTo(1));
            Assert.That(BuildCard(),Is.True);Assert.That(body.GetComponentsInChildren<RowBinding>().Length,Is.EqualTo(2));
            FinishCards();Assert.That(body.GetComponentsInChildren<RowBinding>().Length,Is.LessThan(100));
            Assert.That(body.GetComponentsInChildren<RowBinding>().Select(row=>row.Item.key).Distinct().Count(),Is.EqualTo(body.GetComponentsInChildren<RowBinding>().Length));
        }
        [Test] public void ReplacingAPageDiscardsItsPendingCards()
        {
            Set("Source",FileSource.Emby);app.EmbyState.page=EmbyPage.Home;
            app.EmbyState.resume=Enumerable.Range(0,12).Select(i=>new EmbyItem{Id="old"+i,Name="Old"}).ToArray();
            ui.SelectTab("文件");Assert.That(BuildCard(),Is.True);
            Set("EmbyState",new EmbyBrowserState{page=EmbyPage.Library,items=new[]{new EmbyItem{Id="new",Name="New",Type="Movie"}}});
            var browser=Field<EmbyBrowserUI>(ui,"embyBrowser");browser.Render(true);FinishCards();
            var body=Field<RectTransform>(browser,"body");
            Assert.That(body.GetComponentsInChildren<RowBinding>().Single().Item.key,Does.EndWith(":new"));
            Assert.That(body.GetComponentsInChildren<Button>().Any(button=>button.name.StartsWith("Emby resume old")),Is.False);
        }
        [Test] public void ActiveEmbyAccountUsesTheSharedHighlightWithoutALeftStripe()
        {
            var accounts=new EmbyAccountStore(path+"-accounts",()=>"{}",_=>true);
            var active=new EmbyAccount{id="active",userId="fixture",name="Active fixture",serverUrl="https://example.invalid"};
            accounts.Accounts.Add(new EmbyAccount{id="other",userId="fixture",name="Other fixture",serverUrl="https://example.invalid"});accounts.Accounts.Add(active);
            typeof(PlayerApp).GetField("embyAccounts",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,accounts);Set("ActiveEmbyAccount",active);
            Set("Source",FileSource.Emby);app.EmbyState.page=EmbyPage.Accounts;ui.SelectTab("文件");
            var cards=Field<RectTransform>(ui,"content").GetComponentsInChildren<RoundedPanelGraphic>().Where(panel=>panel.name=="Server account").ToArray();
            Assert.That(cards.Length,Is.EqualTo(2));Assert.That(cards.Count(card=>card.color==UiTheme.SelectedMuted),Is.EqualTo(1));
            Assert.That(cards.All(card=>card.transform.Find("Active")==null),Is.True);
            var input=Field<RectTransform>(ui,"content").GetComponentsInChildren<Image>().Single(image=>image.name=="Emby input surface");Assert.That(input.color.a,Is.Zero);
        }
        [Test] public void SubtitleNavigationAndItsUnderlineSurvivePageRebuilds()
        {
            ui.SelectTab("字幕");var navigation=Field<RectTransform>(ui,"subtitleNavigation");var marker=navigation.Find("Selected underline");
            navigation.GetComponentsInChildren<Button>().Single(button=>button.name=="位置").onClick.Invoke();
            navigation.GetComponentsInChildren<Button>().Single(button=>button.name=="样式").onClick.Invoke();
            Assert.That(Field<RectTransform>(ui,"subtitleNavigation"),Is.SameAs(navigation));Assert.That(navigation.Find("Selected underline"),Is.SameAs(marker));
            Assert.That(navigation.parent,Is.SameAs(ui.Canvas.transform));
            ui.SelectTab("文件");Assert.That(navigation.gameObject.activeSelf,Is.False);
            ui.SelectTab("字幕");Assert.That(navigation.gameObject.activeSelf,Is.True);
            Assert.That(navigation.GetComponentsInChildren<Button>().All(button=>((RectTransform)button.transform).rect.size==new Vector2(202,60)),Is.True);
        }
        [Test] public void TrackDialogHidesSubtitleNavigationAndRestoresItOnReturn()
        {
            ui.SelectTab("字幕");var navigation=Field<RectTransform>(ui,"subtitleNavigation");
            Call(ui,"DrawTracks");Assert.That(navigation.gameObject.activeSelf,Is.False);
            var back=Field<RectTransform>(ui,"content").GetComponentsInChildren<Button>().Single(button=>button.name=="返回");back.onClick.Invoke();
            Assert.That(navigation.gameObject.activeSelf,Is.True);Assert.That(Field<RectTransform>(ui,"subtitleNavigation"),Is.SameAs(navigation));
        }
        [Test] public void LeavingEmbyAccountModalRestoresTransportOnOtherTabs()
        {
            Set("Source",FileSource.Emby);ui.SelectTab("文件");var browser=Field<EmbyBrowserUI>(ui,"embyBrowser");
            typeof(EmbyBrowserUI).GetField("login",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(browser,true);
            Assert.That(ui.BlocksTransport,Is.True);ui.SelectTab("投影");Assert.That(ui.BlocksTransport,Is.False);
        }
        [Test] public void DiagnosticsRemainOnOneScrollablePage()
        {
            ui.SelectTab("信息");Call(ui,"Update");
            Assert.That(Field<ListScrollRect>(ui,"infoScroll"),Is.Not.Null);
            string text=string.Join(" ",Field<RectTransform>(ui,"infoBody").GetComponentsInChildren<TMPro.TextMeshProUGUI>().Select(label=>label.text));
            Assert.That(text,Does.Contain("缓冲内存").And.Contain("意外缓冲").And.Contain("GPU").And.Contain("图形 API"));
        }
        [Test] public void ReplacementPlaybackClearsRetiredDiagnosticValues()
        {
            ui.SelectTab("信息");var cards=Field<PlaybackInfoUI>(ui,"infoCards");backend.Snapshot.decoder="Retired decoder";backend.Snapshot.width=3840;backend.Snapshot.height=2160;cards.Update(app,backend.Snapshot);
            var values=Field<Dictionary<string,TMPro.TextMeshProUGUI>>(cards,"values");Assert.That(values["decoder"].text,Is.EqualTo("Retired decoder"));
            Set("Generation",2);cards.Update(app,backend.Snapshot);
            Assert.That(values["decoder"].text,Is.EqualTo("—"));Assert.That(values["resolution"].text,Is.EqualTo("—"));Assert.That(values["playing"].text,Is.EqualTo("正在打开影片"));
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(true,true)]
        public void SliderEdgesReachTheFullRangeAndKeepTheThumbVisible(bool quick,bool thumb)
        {
            var slider=quick?QuickUiFactory.Slider(ui.Canvas.transform,"Fixture slider",160,320,320,-1,2,.5f,_=>{},thumb):UiFactory.Slider(ui.Canvas.transform,160,320,320,-1,2,.5f,_=>{});
            var rect=(RectTransform)slider.transform;Canvas.ForceUpdateCanvases();
            foreach(float end in new[]{0f,1f}){
                slider.SetValueWithoutNotify(.5f);var data=Pointer(rect,end*rect.rect.width,rect.rect.height/2);
                slider.OnPointerDown(data);Assert.That(slider.value,Is.EqualTo(end==0?-1:2).Within(.001));
                if(slider.handleRect!=null){
                    var corners=new Vector3[4];slider.handleRect.GetWorldCorners(corners);
                    foreach(var corner in corners){Vector3 local=rect.InverseTransformPoint(corner);Assert.That(local.x,Is.InRange(rect.rect.xMin-.01f,rect.rect.xMax+.01f));}
                }
                slider.OnPointerUp(data);
            }
            Assert.That(slider.GetComponent<Graphic>().raycastTarget,Is.True);
        }
        [Test] public void SettingsSwitchesApplyWithoutReplacingTheirControls()
        {
            ui.SelectTab("设置");var controls=Field<RectTransform>(ui,"content").GetComponentsInChildren<UiSwitch>();
            Assert.That(controls.Length,Is.EqualTo(4));
            foreach(var control in controls){
                bool before=control.Value;control.onClick.Invoke();
                Assert.That(control.Value,Is.EqualTo(!before));
                Assert.That(Field<RectTransform>(ui,"content").GetComponentsInChildren<UiSwitch>(),Does.Contain(control));
                Assert.That(control.targetGraphic.color.a,Is.Zero);
            }
            Assert.That(app.Store.Settings.loop,Is.EqualTo(controls.Single(c=>c.name=="单片循环").Value));
            Assert.That(app.Store.Settings.autoLoadSubtitles,Is.EqualTo(controls.Single(c=>c.name=="自动加载同名字幕").Value));
        }
        [Test] public void SubtitleEffectsAndPresetSelectionUpdateWithoutRebuildingTheStylePage()
        {
            ui.SelectTab("字幕");Field<RectTransform>(ui,"subtitleNavigation").GetComponentsInChildren<Button>().Single(b=>b.name=="样式").onClick.Invoke();
            var content=Field<RectTransform>(ui,"content");var outline=content.GetComponentsInChildren<UiSwitch>().Single(c=>c.name=="描边开关");
            var shadow=content.GetComponentsInChildren<UiSwitch>().Single(c=>c.name=="阴影开关");var style=app.Store.Settings.subtitleStyle;
            bool before=style.outline;outline.onClick.Invoke();shadow.onClick.Invoke();Call(ui,"Update");
            Assert.That(style.outline,Is.EqualTo(!before));Assert.That(style.shadow,Is.True);
            Assert.That(content.GetComponentsInChildren<UiSwitch>(),Does.Contain(outline));Assert.That(content.GetComponentsInChildren<UiSwitch>(),Does.Contain(shadow));
            var presets=Field<List<Button>>(ui,"stylePresets");Assert.That(presets.All(b=>b.targetGraphic.color==UiTheme.Elevated),Is.True);
        }
        [Test] public void QuickSubtitleOffAndTrackRefreshKeepTheMasterSwitchAlive()
        {
            app.Current.subtitle="track:fixture";app.Current.subtitleSelection=SubtitleSelection.Manual;
            backend.Snapshot.tracks=new[]{new MediaTrack{id="fixture",name="Fixture",type=3,supported=true}};
            ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("subtitles");
            var control=ui.Quick.Canvas.GetComponentInChildren<UiSwitch>();control.onClick.Invoke();Call(ui.Quick,"LateUpdate");
            Assert.That(app.Current.subtitleSelection,Is.EqualTo(SubtitleSelection.Off));
            Assert.That(ui.Quick.Canvas.GetComponentInChildren<UiSwitch>(),Is.SameAs(control));
            control.onClick.Invoke();Call(ui.Quick,"LateUpdate");
            Assert.That(app.Current.subtitleSelection,Is.EqualTo(SubtitleSelection.Manual));Assert.That(app.Current.subtitle,Is.EqualTo("track:fixture"));
            Assert.That(ui.Quick.Canvas.GetComponentInChildren<UiSwitch>(),Is.SameAs(control));
        }
        [Test] public void SmbGroupTitlesAreNoninteractiveAndSavedSharesExposeAnEditIcon()
        {
            Set("Source",FileSource.Smb);Set("Files",new List<FileEntry>{new FileEntry{name="Fixture share",uri="smb://fixture/",directory=true}});
            app.Discovery.hosts=new[]{new DiscoveredSmbHost{name="Fixture host",host="192.0.2.10"}};Call(ui,"Draw");
            var list=Field<VirtualFileList>(ui,"fileList");var bindings=list.GetComponentsInChildren<RowBinding>();
            foreach(string key in new[]{"saved","discovered"}){
                var heading=bindings.Single(binding=>binding.Item.key==key);
                Assert.That(heading.Item.sectionHeader,Is.True);
                Assert.That(heading.GetComponentsInChildren<Selectable>(),Is.Empty);
                Assert.That(heading.GetComponentsInChildren<Graphic>().All(graphic=>!graphic.raycastTarget),Is.True);
            }
            var share=bindings.Single(binding=>binding.Item.key=="smb://fixture/");
            Assert.That(share.Item.actionIcon,Is.EqualTo(QuickIcon.Edit));Assert.That(share.GetComponentsInChildren<Button>().Single(button=>button.name=="编辑").GetComponent<UiIconTooltip>(),Is.Not.Null);
            Assert.That(bindings.Single(binding=>binding.Item.key.StartsWith("host:")).GetComponentInChildren<Button>().IsInteractable(),Is.True);
        }
        [Test] public void RecycledIconActionsAndSectionHeadersRestoreTheCorrectRowInteraction()
        {
            var list=UiFactory.Rect(ui.Canvas.transform,"Recycled action fixture",0,0,600,160).gameObject.AddComponent<VirtualFileList>();list.Initialize(600,160,directoryStyle:true);
            int edits=0,opens=0,locations=0;
            list.SetRows(new[]{new BrowserRow{key="share",label="Share",secondary="编辑",actionIcon=QuickIcon.Edit,open=()=>opens++,action=()=>edits++}});
            var binding=list.GetComponentInChildren<RowBinding>();var action=binding.GetComponentsInChildren<Button>().Single(button=>button.name=="编辑");
            binding.Press();action.onClick.Invoke();Assert.That(edits,Is.EqualTo(1));Assert.That(opens,Is.Zero);
            list.SetRows(new[]{new BrowserRow{key="section",label="Section",sectionHeader=true}});
            Assert.That(list.GetComponentInChildren<RowBinding>(),Is.SameAs(binding));Assert.That(binding.GetComponentsInChildren<Selectable>(),Is.Empty);
            list.SetRows(new[]{new BrowserRow{key="recent",label="Recent",secondary="所在目录",open=()=>opens++,action=()=>locations++}});
            Assert.That(action.gameObject.activeInHierarchy,Is.True);Assert.That(action.GetComponentInChildren<TMPro.TextMeshProUGUI>().text,Is.EqualTo("所在目录"));
            Assert.That(action.GetComponentInChildren<QuickIconGraphic>(),Is.Null);
            action.onClick.Invoke();Assert.That(locations,Is.Zero,"A press on the retired item must not act on the recycled item");
            binding.Press();action.onClick.Invoke();Assert.That(locations,Is.EqualTo(1));binding.Open();Assert.That(opens,Is.EqualTo(1));
        }
        [Test] public void SurfaceGradientAndRoundedStencilRenderWithoutExpandingMaskIntoShadow()
        {
            var fixture=new GameObject("Surface rendering",typeof(RectTransform),typeof(Canvas));var canvas=fixture.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.GetComponent<RectTransform>().sizeDelta=new Vector2(256,256);fixture.transform.position=new Vector3(0,0,2);
            var cameraObject=new GameObject("Surface camera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=128;camera.nearClipPlane=.1f;camera.farClipPlane=3;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<31;
            var target=new RenderTexture(256,256,24);var pixels=new Texture2D(256,256,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{
                camera.targetTexture=target;var panel=UiFactory.RoundedBox(fixture.transform,"Surface",68,68,120,120,UiTheme.Surface,false,24);var graphic=panel.GetComponent<RoundedPanelGraphic>();
                foreach(var child in fixture.GetComponentsInChildren<Transform>())child.gameObject.layer=31;
                Assert.That(graphic.defaultMaterial.shader.name,Is.EqualTo("VRPlayer/UI Surface"));Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
                Assert.That(pixels.GetPixel(128,175).b,Is.GreaterThan(pixels.GetPixel(128,80).b+.01f));
                panel.gameObject.AddComponent<Mask>().showMaskGraphic=true;UiFactory.Box(panel,"Masked child",0,0,120,120,Color.green).gameObject.layer=31;Canvas.ForceUpdateCanvases();camera.Render();pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
                Assert.That(pixels.GetPixel(128,128).g,Is.GreaterThan(.9f));Assert.That(pixels.GetPixel(69,69).g,Is.LessThan(.2f));Assert.That(pixels.GetPixel(66,128).g,Is.LessThan(.2f));
            }finally{RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(fixture);}
        }
    }
}
