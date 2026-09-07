using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VRPlayer
{
    [Serializable] public sealed class PlaybackRecord
    {
        public string uri,name,subtitle="",audio="auto",directory="";
        public long position,size,modified;public string lastPlayed;
        public float subtitleDelay;public ViewSettings view=new ViewSettings();
    }
    [Serializable] public sealed class Favorite {public string name,uri;}
    [Serializable] public sealed class UserSettings
    {
        public int version=1;public float volume=.8f,speed=1,subtitleSize=42,subtitleOffset=-.75f,subtitleDistance=3;
        public bool subtitleFollow=true,sequential,loop,autoHide=true;
        public List<PlaybackRecord> history=new List<PlaybackRecord>();
        public List<Favorite> favorites=new List<Favorite>();
    }
    public sealed class PlayerStore
    {
        private readonly string path;
        public UserSettings Settings{get;private set;}=new UserSettings();
        public PlayerStore(string path){this.path=path;try{if(File.Exists(path))Settings=JsonUtility.FromJson<UserSettings>(File.ReadAllText(path))??new UserSettings();}catch(Exception ex){Debug.LogWarning("VRPlayer settings restored to defaults: "+ex.GetType().Name);}Settings.history??=new List<PlaybackRecord>();Settings.favorites??=new List<Favorite>();}
        public PlaybackRecord Find(string uri)=>Settings.history.FirstOrDefault(item=>item.uri==uri);
        public PlaybackRecord Touch(FileEntry file,string directory)
        {
            var record=Find(file.uri);
            if(record==null){record=new PlaybackRecord{uri=file.uri,name=file.name,directory=directory,view=ViewSettings.Detect(file.name)};Settings.history.Insert(0,record);}
            if(record.size!=0&&file.size!=0&&(record.size!=file.size||record.modified!=file.modified)){record.position=0;record.subtitle="";}
            record.size=file.size;record.modified=file.modified;record.directory=directory;record.lastPlayed=DateTime.UtcNow.ToString("O");
            Settings.history.Remove(record);Settings.history.Insert(0,record);if(Settings.history.Count>200)Settings.history.RemoveRange(200,Settings.history.Count-200);return record;
        }
        public void Save()
        {
            try{Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(Settings,true));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            catch(Exception ex){Debug.LogWarning("VRPlayer settings save failed: "+ex.GetType().Name);}
        }
    }
}
