using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRPlayer.Editor
{
    // Synthetic metadata only. Batch mode keeps fixture creation out of a live editor scene.
    public static class UIRedesignPreview
    {
        private static void Set(PlayerApp app,string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(target);
        private static void Call(object target,string name)=>target.GetType().GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(target,null);
        private static void FixtureArtwork(Transform root,Dictionary<string,Texture2D> textures)
        {
            foreach(var holder in root.GetComponentsInChildren<RectTransform>().Where(rect=>rect.name=="Artwork").ToArray()){
                string kind=holder.parent.name.StartsWith("Emby library ")?"LIBRARY":holder.parent.name.StartsWith("Emby resume ")?"THUMB":holder.rect.width<holder.rect.height?"POSTER":"THUMB";
                if(!textures.TryGetValue(kind,out var texture)){
                    int width=kind=="POSTER"?200:kind=="LIBRARY"?512:424,height=kind=="POSTER"?300:kind=="LIBRARY"?288:225;
                    texture=new Texture2D(width,height,TextureFormat.RGB24,false);var pixels=new Color[width*height];
                    Color low=kind=="LIBRARY"?new Color(.08f,.27f,.33f):kind=="THUMB"?new Color(.19f,.16f,.32f):new Color(.15f,.25f,.34f);
                    for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                        bool edge=x<4||y<4||x>=width-4||y>=height-4;float blend=.12f+.45f*x/width+.25f*y/height;
                        pixels[y*width+x]=edge?new Color(.30f,.68f,.64f):Color.Lerp(low,new Color(.24f,.60f,.61f),blend);
                    }
                    texture.SetPixels(pixels);texture.Apply();textures.Add(kind,texture);
                }
                var image=holder.GetComponentInChildren<UnityEngine.UI.RawImage>();
                if(image==null){var rect=UiFactory.Rect(holder,"Image",0,0,holder.rect.width,holder.rect.height);rect.pivot=new Vector2(.5f,.5f);image=rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();image.raycastTarget=false;rect.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>().aspectMode=UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;}
                if(image.texture==null){
                    typeof(EmbyBrowserUI).GetMethod("Assign",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{image,texture});
                    UiFactory.Label(holder,kind,8,0,holder.rect.width-16,holder.rect.height,22,TMPro.TextAlignmentOptions.Center).name="Fixture artwork label";
                }
            }
        }
        [Serializable] private sealed class LabelAlignment {public string text,parent;public float fontSize,width,height,inkOffset,fontMiddleInkOffset;public int lines;}
        [Serializable] private sealed class AlignmentCapture {public LabelAlignment[] labels;}
        private static LabelAlignment MeasureAlignment(TMPro.TextMeshProUGUI text)
        {
            float bottom=float.PositiveInfinity,top=float.NegativeInfinity;
            for(int i=0;i<text.textInfo.characterCount;i++){var character=text.textInfo.characterInfo[i];if(!character.isVisible)continue;bottom=Mathf.Min(bottom,character.bottomLeft.y);top=Mathf.Max(top,character.topRight.y);}
            float center=(bottom+top)*.5f;
            return new LabelAlignment{text=text.text,parent=text.transform.parent.name,fontSize=text.fontSize,width=text.rectTransform.rect.width,height=text.rectTransform.rect.height,inkOffset=center-text.rectTransform.rect.center.y,fontMiddleInkOffset=center-text.textBounds.center.y,lines=text.textInfo.lineCount};
        }
        public static void ExportBatch()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Export UI fixtures from a separate batch-mode project snapshot.");
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Docs/Captures/UIRedesign"));
            string[] args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="-uiPreviewOutput")folder=Path.GetFullPath(args[i+1]);
            Run(folder);
        }
        private static void Run(string folder)
        {
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,"preview-settings.json");
            var owner=new GameObject("UI validation fixture"){hideFlags=HideFlags.HideAndDontSave};owner.SetActive(false);var app=owner.AddComponent<PlayerApp>();
            PlayerUI ui=null;GameObject cameraObject=null;RenderTexture rt=null;var fixtureTextures=new Dictionary<string,Texture2D>();bool artworkFixtures=Environment.GetCommandLineArgs().Contains("-uiArtworkFixtures");
            try{
                var store=new PlayerStore(path);Set(app,"Store",store);var backend=new AndroidMediaBackend();Set(app,"Backend",backend);Set(app,"Generation",1);
                backend.Snapshot.generation=1;backend.Snapshot.duration=7200000;backend.Snapshot.position=2310000;backend.Snapshot.playing=true;backend.Snapshot.width=3840;backend.Snapshot.height=2160;backend.Snapshot.decoder="Editor fixture";backend.Snapshot.codec="HEVC";backend.Snapshot.fps=24;
                Set(app,"Current",store.Touch(new FileEntry{name="远方的风景 · 演示影片.mp4",uri="file:///ui-fixture.mp4"},""));
                Set(app,"Presenter",owner.AddComponent<VideoPresenter>());Set(app,"RenderQuality",owner.AddComponent<RenderQualityController>());
                cameraObject=new GameObject("UI capture camera",typeof(Camera)){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.012f,.02f,.028f,1);camera.orthographic=true;camera.orthographicSize=.86f;camera.nearClipPlane=.01f;camera.farClipPlane=10;
                app.headCamera=camera;ui=owner.AddComponent<PlayerUI>();Set(app,"UI",ui);ui.Initialize(app,camera);
                rt=new RenderTexture(1800,1320,24){antiAliasing=4};rt.Create();camera.targetTexture=rt;
                void Save(string name)
                {
                    if(artworkFixtures)FixtureArtwork(ui.ActiveCanvas.transform,fixtureTextures);
                    Canvas.ForceUpdateCanvases();foreach(var text in ui.ActiveCanvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>())text.ForceMeshUpdate();Canvas.ForceUpdateCanvases();camera.Render();
                    File.WriteAllText(Path.Combine(folder,name+"-alignment.json"),JsonUtility.ToJson(new AlignmentCapture{labels=ui.ActiveCanvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Where(text=>!string.IsNullOrWhiteSpace(text.text)&&text.GetComponentInParent<CaptionPresenter>()==null).Select(MeasureAlignment).ToArray()},true));
                    var old=RenderTexture.active;RenderTexture.active=rt;var png=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);png.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);RenderTexture.active=old;
                }
                Set(app,"Source",FileSource.Directory);Set(app,"DirectoryUri","file:///storage/Movies");Set(app,"Files",Enumerable.Range(0,28).Select(i=>new FileEntry{name=i==0?"旅行记录":i==1?"远方的风景.mp4":"影片 "+i.ToString("D2")+".mp4",uri="file:///fixture/"+i,directory=i==0,size=2538000000}).ToList());ui.SelectTab("文件");Call(ui,"Update");Save("01-Files");
                var iconHint=Field<RectTransform>(ui,"content").GetComponentsInChildren<UiIconTooltip>().Single(hint=>hint.name=="搜索");var pointer=new UnityEngine.EventSystems.PointerEventData(null){pointerId=-101};iconHint.OnPointerEnter(pointer);System.Threading.Thread.Sleep(360);Call(ui.Canvas.GetComponent<UiIconTooltipLayer>(),"LateUpdate");Save("16-IconTooltip");iconHint.OnPointerExit(pointer);
                typeof(PlayerUI).GetMethod("ShowKeyboard",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(ui,new object[]{Field<TMPro.TMP_InputField>(ui,"address")});Save("23-Keyboard");Call(ui,"CloseKeyboard");
                Set(app,"Source",FileSource.Smb);Set(app,"DirectoryUri","");Set(app,"Files",new List<FileEntry>{new FileEntry{name="客厅媒体库",uri="smb://preview-share/",directory=true},new FileEntry{name="纪录片与旅行",uri="smb://preview-share-two/",directory=true}});
                app.Discovery.complete=true;app.Discovery.hosts=new[]{new DiscoveredSmbHost{name="演示工作站",host="192.0.2.10"},new DiscoveredSmbHost{name="演示媒体服务器",host="192.0.2.20"}};Call(ui,"Draw");Save("33-SmbGroups");
                var editHint=Field<VirtualFileList>(ui,"fileList").GetComponentsInChildren<UiIconTooltip>().First(hint=>hint.name=="编辑");editHint.OnPointerEnter(pointer);System.Threading.Thread.Sleep(360);Call(ui.Canvas.GetComponent<UiIconTooltipLayer>(),"LateUpdate");Save("34-SmbEditHint");editHint.OnPointerExit(pointer);
                Set(app,"Source",FileSource.Favorites);Set(app,"Files",new List<FileEntry>{new FileEntry{name="旅行记录",uri="file:///fixture/favorites/",directory=true},new FileEntry{name="远方的风景.mp4",uri="file:///fixture/landscape.mp4"}});ui.SelectTab("文件");Save("35-FavoriteActions");
                ui.SelectTab("投影");Save("02-Projection");
                app.ChangeFormat(view=>view.projection=Projection.Fisheye);ui.SelectTab("投影");Save("31-FisheyeGeometry");
                app.ChangeFormat(view=>view.projection=Projection.Equirect360);ui.SelectTab("投影");Save("32-PanoramaGeometry");
                app.ChangeFormat(view=>view.projection=Projection.Flat);ui.SelectTab("字幕");Save("03-SubtitleSelection");
                app.SubtitleCandidates.AddRange(Enumerable.Range(0,12).Select(i=>new FileEntry{name="远方的风景."+(i%2==0?"zh-CN":"en")+"."+(i+1).ToString("D2")+".srt",uri="file:///subtitle-fixture/"+i+".srt"}));
                app.Current.subtitle=app.SubtitleCandidates[0].uri;ui.SelectTab("字幕");Save("03b-SubtitleMatches");
                Call(ui,"DrawTracks");Save("22-Tracks");ui.SelectTab("字幕");
                Field<RectTransform>(ui,"subtitleNavigation").GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="位置").onClick.Invoke();store.Settings.subtitleSize=80.54f;store.Settings.subtitleOffset=-1.42f;store.Settings.subtitleDistance=.57f;ui.SelectTab("字幕");Save("17-SubtitlePosition");
                var tabPointer=new UnityEngine.EventSystems.PointerEventData(null){pointerId=-102};var subtitleHover=Field<RectTransform>(ui,"subtitleNavigation").GetComponentsInChildren<UiTabButton>().Single(button=>button.name=="选择");subtitleHover.OnPointerEnter(tabPointer);Save("19-SubtitleTabHover");subtitleHover.OnPointerExit(tabPointer);
                var primaryHover=ui.Canvas.GetComponentsInChildren<UiTabButton>().Single(button=>button.name=="文件");primaryHover.OnPointerEnter(tabPointer);Save("20-PrimaryTabHover");primaryHover.OnPointerExit(tabPointer);
                Field<RectTransform>(ui,"subtitleNavigation").GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="样式").onClick.Invoke();Call(ui,"Update");Save("04-SubtitleStyle");
                ui.SelectTab("设置");Save("05-Settings");ui.SelectTab("信息");Call(ui,"Update");Save("06-Info");Field<RectTransform>(ui,"infoBody").anchoredPosition=new Vector2(0,PlaybackInfoUI.Height-Field<ListScrollRect>(ui,"infoScroll").viewport.rect.height);Save("14-InfoDiagnostics");
                Set(app,"Source",FileSource.Emby);app.EmbyState.title="演示媒体库";app.EmbyState.page=EmbyPage.Home;
                app.EmbyState.views=Enumerable.Range(0,3).Select(i=>new EmbyItem{Id="library"+i,Name=new[]{"音乐","电影","番剧"}[i],Type="CollectionFolder",IsFolder=true}).ToArray();
                app.EmbyState.resume=Enumerable.Range(0,8).Select(i=>new EmbyItem{Id="resume"+i,Name="演示影片 "+(i+1),SeriesName=i>1?"演示剧集 "+(i-1):null,Type=i>1?"Episode":"Movie",ParentIndexNumber=1,IndexNumber=i,ProductionYear=2024,RunTimeTicks=TimeSpan.FromMinutes(24).Ticks,UserData=new EmbyUserData{PlaybackPositionTicks=TimeSpan.FromMinutes(3+i).Ticks}}).ToArray();
                app.EmbyState.latest=app.EmbyState.resume.Take(5).ToArray();ui.SelectTab("文件");Save("24-EmbyHome");
                Field<RectTransform>(Field<EmbyBrowserUI>(ui,"embyBrowser"),"body").anchoredPosition=new Vector2(0,340);Save("25-EmbyResume");
                var previewAccounts=new EmbyAccountStore(Path.Combine(folder,"unused-preview-accounts.json"),()=>"{}",_=>true);previewAccounts.Accounts.AddRange(Enumerable.Range(0,2).Select(i=>new EmbyAccount{id="preview-account"+i,userId="fixture",name="演示账户 "+(i+1),username="preview",serverUrl="https://example.invalid"}));typeof(PlayerApp).GetField("embyAccounts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(app,previewAccounts);Set(app,"ActiveEmbyAccount",previewAccounts.Accounts[1]);app.EmbyState.page=EmbyPage.Accounts;ui.SelectTab("文件");Save("26-EmbyAccounts");Set(app,"ActiveEmbyAccount",null);
                app.EmbyState.page=EmbyPage.Library;app.EmbyState.parent=new EmbyItem{Id="library",Name="影片"};app.EmbyState.items=Enumerable.Range(0,80).Select(i=>new EmbyItem{Id="film"+i,Name="演示影片 "+i.ToString("D2"),Type="Movie",ProductionYear=2024,RunTimeTicks=TimeSpan.FromMinutes(120).Ticks}).ToArray();ui.SelectTab("文件");Save("07-EmbyWall");
                app.EmbyState.page=EmbyPage.Details;app.EmbyState.selected=new EmbyItem{Id="main",Name="远方的风景",Type="Movie",ProductionYear=2024,RunTimeTicks=TimeSpan.FromMinutes(120).Ticks,Overview="一个用于检查界面层级与可读性的演示影片。所有名称均为测试数据。长片介绍按实际内容向下排版，演员和主创在片段之后完整横向展示。",People=Enumerable.Range(0,80).Select(i=>new EmbyPerson{Id="person"+i,Name="演示演员 "+(i+1),Role=i==0?"导演":"演员"}).ToArray()};app.EmbyState.parts=new[]{new EmbyItem{Id="main",Name="上半部",Type="Movie",RunTimeTicks=TimeSpan.FromMinutes(60).Ticks},new EmbyItem{Id="second",Name="下半部",Type="Video",RunTimeTicks=TimeSpan.FromMinutes(60).Ticks}};app.EmbyState.extras=new[]{new EmbyItem{Id="extra",Name="制作花絮",Type="Video",RunTimeTicks=TimeSpan.FromMinutes(8).Ticks}};
                ui.SelectTab("文件");Save("08-EmbyDetails");var browser=Field<EmbyBrowserUI>(ui,"embyBrowser");Field<RectTransform>(browser,"body").anchoredPosition=new Vector2(0,510);Save("09-Cast");
                app.EmbyState.parts=Array.Empty<EmbyItem>();app.EmbyState.extras=Array.Empty<EmbyItem>();ui.SelectTab("文件");Field<RectTransform>(browser,"body").anchoredPosition=Vector2.zero;Save("27-EmbyPlayButton");
                ui.Hide();ui.ShowQuick();Call(ui.Quick,"LateUpdate");camera.orthographicSize=.82f;cameraObject.transform.position=new Vector3(0,-.43f,0);Save("10-Quick");
                Field<RectTransform>(ui.Quick,"volumeExpansion").gameObject.SetActive(true);Save("28-QuickVolume");Field<RectTransform>(ui.Quick,"volumeExpansion").gameObject.SetActive(false);
                typeof(QuickControlsUI).GetMethod("Preview",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(ui.Quick,new object[]{true,.64f});Save("11-QuickBeam");
                cameraObject.transform.position=Vector3.zero;camera.orthographicSize=.94f;ui.Quick.TogglePopup("subtitles");Call(ui.Quick,"LateUpdate");Save("12-QuickSubtitles");ui.Quick.TogglePopup("mode");Call(ui.Quick,"LateUpdate");Save("13-QuickMode");
                ui.Quick.TogglePopup("advanced");Call(ui.Quick,"LateUpdate");Save("18-QuickSpeed");
                app.SetSpeed(.25f);Call(ui.Quick,"LateUpdate");Save("29-QuickSpeedMinimum");app.SetSpeed(3);Call(ui.Quick,"LateUpdate");Save("30-QuickSpeedMaximum");app.SetSpeed(1);
                ui.Quick.TogglePopup("files");Call(ui.Quick,"LateUpdate");Save("21-QuickSubtitleBrowser");
                    File.WriteAllText(Path.Combine(folder,"capture-result.txt"),"35 native uGUI fixture captures using synthetic metadata only");
            }finally{
                if(ui!=null){
                    if(ui.Quick!=null&&ui.Quick.Canvas!=null){var quick=ui.Quick.Canvas;typeof(QuickControlsUI).GetProperty("Canvas").SetValue(ui.Quick,null);UnityEngine.Object.DestroyImmediate(quick.gameObject);}
                    if(ui.Canvas!=null){var canvas=ui.Canvas;typeof(PlayerUI).GetProperty("Canvas").SetValue(ui,null);UnityEngine.Object.DestroyImmediate(canvas.gameObject);}
                    var hud=Field<RectTransform>(ui,"transportRoot");if(hud!=null)UnityEngine.Object.DestroyImmediate(hud.gameObject);
                }
                Set(app,"Presenter",null);Set(app,"Current",null);UnityEngine.Object.DestroyImmediate(owner);
                if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);
                if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
                foreach(var texture in fixtureTextures.Values)UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
