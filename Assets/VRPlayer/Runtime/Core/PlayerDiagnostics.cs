using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VRPlayer
{
    // Development builds only. Commands are read from the app-private sandbox via adb run-as.
    public sealed class PlayerDiagnostics : MonoBehaviour
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable] private sealed class Command
        {
            public string action,uri,name,subtitle,host,share,username,password,collectionType;
            public long position;public int projection,stereo,port=1445;public bool flag;
            public bool valid=true;
            public float value,distance=3,size=42,offset=-.75f;public int preset;
        }
        private PlayerApp app;private float nextPoll;private string commandDirectory;
        private void Start(){app=GetComponent<PlayerApp>();commandDirectory=Application.persistentDataPath;
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer");using var activity=unity.GetStatic<AndroidJavaObject>("currentActivity");using var files=activity.Call<AndroidJavaObject>("getFilesDir");commandDirectory=files.Call<string>("getAbsolutePath");
#endif
        }
        private void Update()
        {
            if(app==null||app.Store==null||Time.unscaledTime<nextPoll)return;nextPoll=Time.unscaledTime+.5f;
            string path=Path.Combine(commandDirectory,"command.json");if(!File.Exists(path))return;
            try{
                string json=File.ReadAllText(path);File.Delete(path);var command=JsonUtility.FromJson<Command>(json);
                switch(command.action){
                    case "open":app.Open(new FileEntry{uri=command.uri,name=command.name??Path.GetFileName(command.uri)},FileLibrary.Parent(command.uri));break;
                    case "browse":_ = app.Browse(command.uri);break;
                    case "seek":app.Seek(command.position);break;
                    case "pause":app.Pause();break;
                    case "stop":app.Stop();break;
                    case "repeat":app.Store.Settings.loop=command.flag;app.Backend?.SetRepeat(command.flag);break;
                    case "sequential":app.Store.Settings.sequential=command.flag;break;
                    case "play":app.Backend?.Play();break;
                    case "subtitle":_ = app.LoadSubtitle(command.uri);break;
                    case "projection":app.ChangeFormat(view=>{view.projection=(Projection)command.projection;view.stereo=(StereoLayout)command.stereo;});break;
                    case "eyes":app.ChangeFormat(view=>view.swapEyes=command.flag);break;
                    case "track":if(command.flag)app.EmbeddedSubtitle(command.uri);else app.Backend.SelectTrack(command.uri,1);break;
                    case "subtitle-auto":app.SetAutoLoadSubtitles(command.flag);break;
                    case "subtitle-off":app.DisableSubtitles();break;
                    case "subtitle-reset":app.RestoreAutomaticSubtitles();break;
                    case "skip":app.Skip((int)command.value);break;
                    case "next":app.Next(1);break;
                    case "previous":app.Next(-1);break;
                    case "locate":app.OpenContainingDirectory(command.uri);break;
                    case "detect":app.RedetectFormat();break;
                    case "detection-mode":app.SetFormatDetectionMode((FormatDetectionMode)command.preset);break;
                    case "detect-undo":app.UndoFormatDetection();break;
                    case "detect-apply":app.ApplyFormatSuggestion();break;
                    case "directory-vr180":app.SetDirectoryVr180Preference(command.flag);break;
                    case "inspect":break;
                    case "localization-inspect":break;
                    case "smb-read-ahead":
#if UNITY_ANDROID && !UNITY_EDITOR
                        AndroidBridge.Api.CallStatic("setSmbReadAhead",command.flag);
#endif
                        break;
                    case "tab":app.UI.SelectTab(command.name);break;
                    case "source":if(command.name=="emby")app.ShowEmby();else if(command.name=="smb")app.ShowServers();else if(command.name=="favorites")app.ShowFavorites();else if(command.name=="recent")app.ShowRecent();else app.Home();break;
                    case "emby-login":_ = app.LoginEmby(command.uri,command.username,command.password,command.flag);break;
                    case "emby-account":app.SelectEmbyAccount(command.name);break;
                    case "emby-accounts":app.ShowEmbyAccounts();break;
                    case "emby-remove":app.RemoveEmbyAccount(command.name);break;
                    case "emby-home":app.EmbyHome();break;
                    case "emby-back":app.EmbyBack();break;
                    case "emby-library":app.OpenEmbyLibrary(new EmbyItem{Id=command.uri,Name=command.name??"媒体库",Type=string.IsNullOrEmpty(command.collectionType)?null:"CollectionFolder",CollectionType=command.collectionType},(int)command.position,command.subtitle??"");break;
                    case "emby-item":app.OpenEmbyItem(new EmbyItem{Id=command.uri,Name=command.name??"影片"});break;
                    case "emby-play":app.PlayEmby(new EmbyItem{Id=command.uri},command.flag);break;
                    case "emby-part":app.PlayEmbyPart(new EmbyItem{Id=command.uri});break;
                    case "emby-extra":app.PlayEmbyExtra(new EmbyItem{Id=command.uri});break;
                    case "emby-person":app.OpenEmbyPerson(new EmbyPerson{Id=command.uri,Name=command.name??"演员"});break;
                    case "emby-filter":app.OpenEmbyCategory(command.name,command.uri,command.subtitle??"");break;
                    case "emby-search":app.SearchEmbyLibrary(command.subtitle??"");break;
                    case "quality":app.RenderQuality.Select((RenderQuality)Mathf.Clamp(command.preset,0,2));break;
                    case "caption-settings":app.Store.Settings.subtitleDistance=Mathf.Clamp(command.distance,.1f,6);app.Store.Settings.subtitleSize=command.size;app.Store.Settings.subtitleOffset=command.offset;app.Store.Settings.subtitleStyle=SubtitleStyle.Preset(command.preset);break;
                    case "scan":app.ShowServers();break;
                    case "stop-scan":app.StopDiscovery();break;
                    case "shares":_ = InspectShares(new SmbServer{host=command.host,port=command.port,username=command.username??"",password=command.password??""});break;
                    case "menu":if(command.flag)app.UI.Show();else app.UI.Hide();break;
                    case "quick-menu":if(command.flag){app.UI.Hide();app.UI.ShowQuick();}else app.UI.HideQuick();break;
                    case "quick-popup":app.UI.Quick.TogglePopup(command.name);break;
                    case "quick-browse":_ = app.UI.Quick.Browse(command.uri);break;
                    case "ui-pointer":app.GetComponent<VrUiInput>()?.SetDiagnosticPointer(command.preset,command.name,command.value,command.flag);break;
                    case "ui-stick":app.GetComponent<VrUiInput>()?.SetDiagnosticStick(command.preset,new Vector2(command.value,command.offset));break;
                    case "ui-grip":app.GetComponent<VrUiInput>()?.SetDiagnosticGrip(command.preset,command.flag);break;
                    case "ui-back":app.GetComponent<VrUiInput>()?.SetDiagnosticBack(command.preset,command.flag,command.valid);break;
                    case "ui-input-reset":app.GetComponent<VrUiInput>()?.ResetDiagnosticInput();break;
                    case "ui-input-stats-reset":app.GetComponent<VrUiInput>()?.ResetDiagnostics();break;
                    case "volume":app.SetVolume(command.value);break;
                    case "mute":app.ToggleMute();break;
                    case "speed":app.SetSpeed(command.value);break;
                    case "auto-hide":app.Store.Settings.autoHide=command.flag;break;
                    case "subtitle-enabled":app.SetSubtitlesEnabled(command.flag);break;
                    case "subtitle-delay":app.SetSubtitleDelay(command.value);break;
                    case "panorama-zoom":app.SetPanoramaZoom(command.value);break;
                    case "capture":ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath,"capture.png"));break;
                    case "smb":app.SaveServer(new SmbServer{name=command.name??"Test fixture",host=command.host,share=command.share,port=command.port,username=command.username??"",password=command.password??"",remember=false});break;
                }
                bool presenceKnown=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence,out bool userPresent);
                File.WriteAllText(Path.Combine(commandDirectory,"command-result.json"),JsonUtility.ToJson(new Result{languagePreference=Localization.Preference.ToString(),language=Localization.Language.ToString(),systemLanguage=Application.systemLanguage.ToString(),labels=command.action=="localization-inspect"?InspectLabels():null,input=app.GetComponent<VrUiInput>()?.Diagnostics,accepted=true,mainPanelDistance=app.Store.Settings.mainPanelDistance,panelPosition=app.UI.Canvas.transform.position,panelForward=app.UI.Canvas.transform.forward,panelScale=app.UI.Canvas.transform.localScale,wall=app.UI.EmbyWall,action=command.action,timestampUtc=DateTime.UtcNow.ToString("O"),frame=Time.frameCount,files=app.Files.ToArray(),subtitles=app.SubtitleCandidates.ToArray(),tab=app.UI.SelectedTab,source=app.Source.ToString(),directory=app.DirectoryUri,discovery=app.Discovery,emby=EmbyDiagnostic.From(app),status=app.Status,loading=app.Loading,uiSurface=app.UI.Visible?"full":app.UI.Quick.Visible?"quick":"hidden",fullVisible=app.UI.Visible,quickVisible=app.UI.Quick.Visible,fullOffset=app.UI.FullOffset,fullCount=app.UI.FullCount,fullModal=app.UI.FullModal,fullQuery=app.UI.FullQuery,keyboardVisible=app.UI.KeyboardVisible,quickOffset=app.UI.Quick.BrowserOffset,quickPopup=app.UI.Quick.Popup,quickBrowserUri=app.UI.Quick.BrowserUri,quickBrowserCount=app.UI.Quick.BrowserCount,quickBrowserLoading=app.UI.Quick.BrowserLoading,hasFocus=app.HasFocus,presenceKnown=presenceKnown,userPresent=userPresent}));
            }catch(Exception ex){File.WriteAllText(Path.Combine(commandDirectory,"command-result.json"),JsonUtility.ToJson(new Failure{error=ex.ToString()}));}
        }
        [Serializable] private sealed class UiLabel {public string text,parent;public bool truncated;public int missingGlyphs;}
        private UiLabel[] InspectLabels()
        {
            var canvas=app.UI.ActiveCanvas;
            if(canvas==null)return Array.Empty<UiLabel>();
            return canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>()
                .Where(label=>label.GetComponentInParent<TMPro.TMP_InputField>()==null)
                .Select(label=>{
                    label.ForceMeshUpdate();
                    string text=label.text??string.Empty;
                    return new UiLabel{text=text,parent=label.transform.parent.name,truncated=label.isTextTruncated,
                        missingGlyphs=text.Count(character=>!char.IsControl(character)&&!char.IsWhiteSpace(character)&&(label.font==null||!label.font.HasCharacter(character,true)))};
                }).ToArray();
        }
        [Serializable] private sealed class Failure {public string error;}
        private async System.Threading.Tasks.Task InspectShares(SmbServer server){var result=await app.Smb.Shares(server,System.Threading.CancellationToken.None);File.WriteAllText(Path.Combine(commandDirectory,"smb-shares-result.json"),JsonUtility.ToJson(result));}
        [Serializable] private sealed class EmbyDiagnostic
        {
            public string page,accountId,selectedId,currentId,error,filterKind,filterValue,playbackQueueKind,playbackMainId;public bool loading,canNext,canPrevious,personOverviewPresent;public int accountCount,viewCount,resumeCount,latestCount,totalCount,startIndex,itemCount,castCount,pendingReportCount,partCount,extraCount,partPlaylistIndex,playbackQueueIndex;public string[] viewIds,itemIds,partIds,extraIds,partPlaylistIds,playbackQueueIds;public long resumePosition;
            public static EmbyDiagnostic From(PlayerApp app)
            {
                EmbyIdentity.TryParse(app.Current?.uri,out _,out string currentId);
                return new EmbyDiagnostic{page=app.EmbyState.page.ToString(),accountId=app.ActiveEmbyAccount?.id??"",selectedId=app.EmbyState.selected?.Id??"",currentId=currentId??"",error=app.EmbyState.error,loading=app.EmbyState.loading,accountCount=app.EmbyAccounts.Count,viewCount=app.EmbyState.views.Length,resumeCount=app.EmbyState.resume.Length,latestCount=app.EmbyState.latest.Length,totalCount=app.EmbyState.totalCount,startIndex=app.EmbyState.startIndex,itemCount=app.EmbyState.items.Length,castCount=app.EmbyState.selected?.People?.Length??0,pendingReportCount=app.EmbyPendingReports,resumePosition=(app.EmbyState.selected?.UserData?.PlaybackPositionTicks??0)/10000,viewIds=Array.ConvertAll(app.EmbyState.views,x=>x.Id),itemIds=Array.ConvertAll(app.EmbyState.items,x=>x.Id),partCount=app.EmbyState.parts.Length,extraCount=app.EmbyState.extras.Length,partIds=Array.ConvertAll(app.EmbyState.parts,x=>x.Id),extraIds=Array.ConvertAll(app.EmbyState.extras,x=>x.Id),partPlaylistIds=app.EmbyPlaybackPartIds,partPlaylistIndex=app.EmbyPlaybackPartIndex,canNext=app.CanNavigateAdjacent(1),canPrevious=app.CanNavigateAdjacent(-1),filterKind=app.EmbyState.filterKind,filterValue=app.EmbyState.filterValue,personOverviewPresent=app.EmbyState.page==EmbyPage.Person&&!string.IsNullOrWhiteSpace(app.EmbyState.selected?.Overview),playbackQueueKind=app.EmbyPlaybackQueueKind,playbackQueueIds=app.EmbyPlaybackQueueIds,playbackQueueIndex=app.EmbyPlaybackQueueIndex,playbackMainId=app.EmbyPlaybackMainId};
            }
        }
        [Serializable] private sealed class Result {public string languagePreference,language,systemLanguage;public UiLabel[] labels;public VrUiInput.InputSnapshot input;public bool accepted;public float mainPanelDistance;public Vector3 panelPosition,panelForward,panelScale;public EmbyBrowserUI.WallSnapshot wall;public string action,tab,source,directory,uiSurface,fullModal,fullQuery,quickPopup,quickBrowserUri,status,timestampUtc;public int fullCount,quickBrowserCount,frame;public float fullOffset,quickOffset;public bool keyboardVisible,quickBrowserLoading,hasFocus,presenceKnown,userPresent,loading,fullVisible,quickVisible;public FileEntry[] files,subtitles;public SmbDiscoverySnapshot discovery;public EmbyDiagnostic emby;}
#endif
    }
}
