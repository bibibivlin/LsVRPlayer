using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VRPlayer
{
    public enum SubtitleSelection { Auto, Manual, Off }
    public enum FormatLockOrigin { Saved, Manual, Inherited }
    [Serializable] public sealed class PlaybackRecord
    {
        public string uri,name,subtitle="",audio="auto",directory="",formatName="";
        public long position,size,modified;public string lastPlayed;
        public float subtitleDelay;public ViewSettings view=new ViewSettings();
        public SubtitleSelection subtitleSelection;
        public string subtitleBeforeOff="";
        public SubtitleSelection subtitleSelectionBeforeOff;
        public bool formatLocked;
        public FormatLockOrigin formatLockOrigin;
        public FormatDetectionCache formatCache;
    }
    [Serializable] public sealed class Favorite {public string name,uri;}
    [Serializable] public sealed class UserSettings
    {
        public int version=3;public float volume=.8f,speed=1,subtitleSize=42,subtitleOffset=-.75f,subtitleDistance=3;
        public float lastAudibleVolume=.8f;
        public bool autoLoadSubtitles;
        public FormatDetectionMode formatDetection=FormatDetectionMode.Enhanced;
        public RenderQuality renderQuality=RenderQuality.High;
        public SubtitleStyle subtitleStyle=new SubtitleStyle();
        public bool subtitleFollow=true,sequential,loop,autoHide=true;
        public List<PlaybackRecord> history=new List<PlaybackRecord>();
        public List<Favorite> favorites=new List<Favorite>();
        public List<string> vr180Directories=new List<string>();
        public List<EmbyLibraryViewPreference> embyLibraryViews=new List<EmbyLibraryViewPreference>();
    }
    public sealed class PlayerStore
    {
        private readonly string path;
        public UserSettings Settings{get;private set;}=new UserSettings();
        public PlayerStore(string path)
        {
            this.path=path;
            try{if(File.Exists(path))Settings=ParseSettings(File.ReadAllText(path));}
            catch(Exception ex){Debug.LogWarning("VRPlayer settings restored to defaults: "+ex.GetType().Name);}
        }
        [Serializable] private sealed class VersionHeader {public int version;}
        public static UserSettings ParseSettings(string json)
        {
            var value=JsonUtility.FromJson<UserSettings>(json)??new UserSettings();
            int version=JsonUtility.FromJson<VersionHeader>(json)?.version??0;
            value.history??=new List<PlaybackRecord>();value.favorites??=new List<Favorite>();value.vr180Directories??=new List<string>();
            value.embyLibraryViews??=new List<EmbyLibraryViewPreference>();
            value.embyLibraryViews.RemoveAll(item=>item==null||string.IsNullOrEmpty(item.accountId)||string.IsNullOrEmpty(item.libraryId));
            foreach(var item in value.embyLibraryViews)item.view=EmbyLibraryViews.Normalize(item.view);
            float oldDistance=Finite(value.subtitleDistance,3);
            value.subtitleDistance=Mathf.Clamp(oldDistance,.1f,6);
            if(version<2){float ratio=3/Mathf.Max(.1f,oldDistance);value.subtitleSize*=ratio;value.subtitleOffset*=ratio;value.renderQuality=RenderQuality.High;value.subtitleStyle=new SubtitleStyle();}
            value.history.RemoveAll(item=>item==null||string.IsNullOrEmpty(item.uri));
            foreach(var record in value.history){
                record.subtitle??="";record.subtitleBeforeOff??="";record.view??=new ViewSettings();
                record.view.panoramaZoom=NormalizeZoom(record.view.panoramaZoom);
                if(record.subtitleSelectionBeforeOff!=SubtitleSelection.Manual)record.subtitleSelectionBeforeOff=SubtitleSelection.Auto;
                if(version<3){record.subtitleSelection=string.IsNullOrEmpty(record.subtitle)?SubtitleSelection.Auto:SubtitleSelection.Manual;record.formatLocked=true;}
                if(!Enum.IsDefined(typeof(SubtitleSelection),record.subtitleSelection))record.subtitleSelection=SubtitleSelection.Auto;
            }
            if(version<3){value.autoLoadSubtitles=false;value.formatDetection=FormatDetectionMode.Enhanced;}
            if(!Enum.IsDefined(typeof(FormatDetectionMode),value.formatDetection))value.formatDetection=FormatDetectionMode.Enhanced;
            value.version=3;value.subtitleStyle??=new SubtitleStyle();
            value.volume=Mathf.Clamp01(Finite(value.volume,.8f));
            float remembered=Finite(value.lastAudibleVolume,.8f);value.lastAudibleVolume=value.volume>0?value.volume:Mathf.Clamp(remembered>0?remembered:.8f,.01f,1);
            value.subtitleSize=Mathf.Clamp(Finite(value.subtitleSize,42),12,200);
            value.subtitleOffset=Mathf.Clamp(Finite(value.subtitleOffset,-.75f),-4,4);
            if(!Enum.IsDefined(typeof(RenderQuality),value.renderQuality))value.renderQuality=RenderQuality.High;
            value.subtitleStyle.Normalize();return value;
        }
        private static float Finite(float value,float fallback)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:value;
        public static float NormalizeZoom(float value)=>Mathf.Clamp(Finite(value,1)>0?Finite(value,1):1,.5f,2);
        public Favorite RemoveFavorite(string uri,out int index)
        {
            index=Settings.favorites.FindIndex(f=>f.uri==uri);if(index<0)return null;
            Favorite removed=Settings.favorites[index];Settings.favorites.RemoveAt(index);Save();return removed;
        }
        public bool RestoreFavorite(Favorite favorite,int index)
        {
            if(favorite==null||Settings.favorites.Any(f=>f.uri==favorite.uri))return false;
            Settings.favorites.Insert(Math.Clamp(index,0,Settings.favorites.Count),favorite);Save();return true;
        }
        public PlaybackRecord Find(string uri)=>Settings.history.FirstOrDefault(item=>item.uri==uri);
        public EmbyLibraryView GetEmbyLibraryView(string accountId,string libraryId)=>EmbyLibraryViews.Normalize(Settings.embyLibraryViews.LastOrDefault(item=>item.accountId==accountId&&item.libraryId==libraryId)?.view??EmbyLibraryView.Poster);
        public void SetEmbyLibraryView(string accountId,string libraryId,EmbyLibraryView view)
        {
            if(string.IsNullOrEmpty(accountId)||string.IsNullOrEmpty(libraryId))return;
            Settings.embyLibraryViews.RemoveAll(item=>item.accountId==accountId&&item.libraryId==libraryId);
            Settings.embyLibraryViews.Add(new EmbyLibraryViewPreference{accountId=accountId,libraryId=libraryId,view=EmbyLibraryViews.Normalize(view)});Save();
        }
        public PlaybackRecord Touch(FileEntry file,string directory)
        {
            var record=Find(file.uri);
            if(record==null){record=new PlaybackRecord{uri=file.uri,name=file.name,directory=directory};Settings.history.Insert(0,record);}
            if(record.size!=0&&file.size!=0&&(record.size!=file.size||(record.modified>0&&file.modified>0&&record.modified!=file.modified))){record.position=0;record.subtitle="";record.subtitleBeforeOff="";record.subtitleSelectionBeforeOff=SubtitleSelection.Auto;record.formatCache=null;if(record.subtitleSelection==SubtitleSelection.Manual)record.subtitleSelection=SubtitleSelection.Auto;}
            record.name=file.name;if(!string.IsNullOrEmpty(file.formatName))record.formatName=file.formatName;if(file.size>0)record.size=file.size;if(file.modified>0)record.modified=file.modified;
            if(!string.IsNullOrEmpty(directory))record.directory=directory;record.lastPlayed=DateTime.UtcNow.ToString("O");
            Settings.history.Remove(record);Settings.history.Insert(0,record);if(Settings.history.Count>200)Settings.history.RemoveRange(200,Settings.history.Count-200);return record;
        }
        public void Save()
        {
            try{Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(Settings,true));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            catch(Exception ex){Debug.LogWarning("VRPlayer settings save failed: "+ex.GetType().Name);}
        }
    }
}
