using System;
using System.IO;
using UnityEngine;

namespace VRPlayer
{
    // Development builds only. Commands are read from the app-private sandbox via adb run-as.
    public sealed class PlayerDiagnostics : MonoBehaviour
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable] private sealed class Command
        {
            public string action,uri,name,subtitle,host,share,username,password;
            public long position;public int projection,stereo,port=1445;public bool flag;
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
                    case "recenter":app.Recenter(command.flag);break;
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
                    case "smb-read-ahead":
#if UNITY_ANDROID && !UNITY_EDITOR
                        AndroidBridge.Api.CallStatic("setSmbReadAhead",command.flag);
#endif
                        break;
                    case "tab":app.UI.SelectTab(command.name);break;
                    case "source":if(command.name=="smb")app.ShowServers();else if(command.name=="favorites")app.ShowFavorites();else if(command.name=="recent")app.ShowRecent();else app.Home();break;
                    case "quality":app.RenderQuality.Select((RenderQuality)Mathf.Clamp(command.preset,0,2));break;
                    case "caption-settings":app.Store.Settings.subtitleDistance=Mathf.Clamp(command.distance,.1f,6);app.Store.Settings.subtitleSize=command.size;app.Store.Settings.subtitleOffset=command.offset;app.Store.Settings.subtitleStyle=SubtitleStyle.Preset(command.preset);break;
                    case "scan":app.ShowServers();break;
                    case "stop-scan":app.StopDiscovery();break;
                    case "shares":_ = InspectShares(new SmbServer{host=command.host,port=command.port,username=command.username??"",password=command.password??""});break;
                    case "menu":if(command.flag)app.UI.Show();else app.UI.Hide();break;
                    case "capture":ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath,"capture.png"));break;
                    case "smb":app.SaveServer(new SmbServer{name=command.name??"Test fixture",host=command.host,share=command.share,port=command.port,username=command.username??"",password=command.password??"",remember=false});break;
                }
                File.WriteAllText(Path.Combine(commandDirectory,"command-result.json"),JsonUtility.ToJson(new Result{accepted=true,action=command.action,files=app.Files.ToArray(),subtitles=app.SubtitleCandidates.ToArray(),tab=app.UI.SelectedTab,source=app.Source.ToString(),discovery=app.Discovery}));
            }catch(Exception ex){File.WriteAllText(Path.Combine(commandDirectory,"command-result.json"),JsonUtility.ToJson(new Failure{error=ex.ToString()}));}
        }
        [Serializable] private sealed class Failure {public string error;}
        private async System.Threading.Tasks.Task InspectShares(SmbServer server){var result=await app.Smb.Shares(server,System.Threading.CancellationToken.None);File.WriteAllText(Path.Combine(commandDirectory,"smb-shares-result.json"),JsonUtility.ToJson(result));}
        [Serializable] private sealed class Result {public bool accepted;public string action,tab,source;public FileEntry[] files,subtitles;public SmbDiscoverySnapshot discovery;}
#endif
    }
}
