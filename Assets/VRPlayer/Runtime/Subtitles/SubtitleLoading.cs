using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VRPlayer
{
    public sealed class SubtitleLoadResult
    {
        public string uri,error;
        public SrtDocument document;
    }
    public static class SubtitleLoader
    {
        public static async Task<SubtitleLoadResult> Load(IEnumerable<string> candidates,Func<string,CancellationToken,Task<FileResult>> read,CancellationToken token)
        {
            string error="没有可用的同名 SRT";
            foreach(string uri in candidates){
                token.ThrowIfCancellationRequested();
                try{
                    var text=await read(uri,token);token.ThrowIfCancellationRequested();
                    if(!text.ok){error=text.error;continue;}
                    var document=await Task.Run(()=>SrtDocument.Parse(text.text??""),token);token.ThrowIfCancellationRequested();
                    if(document.Cues.Count>0)return new SubtitleLoadResult{uri=uri,document=document};
                    error="未找到有效 SRT 时间轴，请检查编码或文件内容";
                }catch(OperationCanceledException){throw;}catch(Exception){error="字幕读取失败，请检查文件或网络后重新选择";}
            }
            return new SubtitleLoadResult{error=error};
        }
    }
    public sealed partial class PlayerApp
    {
        private void CancelSubtitleSelection(){subtitleRevision++;subtitles?.Cancel();subtitles?.Dispose();subtitles=null;}
        public void SetAutoLoadSubtitles(bool enabled)
        {
            Store.Settings.autoLoadSubtitles=enabled;Store.Save();
            if(Current?.subtitleSelection==SubtitleSelection.Auto){
                CancelSubtitleSelection();
                if(enabled)_ = DiscoverSubtitles(Current.directory,Generation);
            }
            Notify();
        }
        public void RestoreAutomaticSubtitles()
        {
            if(Current==null)return;CancelSubtitleSelection();Current.subtitleSelection=SubtitleSelection.Auto;Current.subtitle="";subtitleDocument=null;Backend?.SelectTrack("off",3);Store.Save();
            _ = DiscoverSubtitles(Current.directory,Generation);SetStatus(Store.Settings.autoLoadSubtitles?"正在自动选择字幕":"已恢复自动选择；可在设置中开启自动加载");Notify();
        }
        private async Task DiscoverSubtitles(string directory,int generation,bool explicitEnable=false)
        {
            subtitleListing?.Cancel();subtitleListing?.Dispose();subtitleListing=new CancellationTokenSource();var token=subtitleListing.Token;
            var record=Current;int revision=subtitleRevision;
            try{
                FileEntry[] entries=Array.Empty<FileEntry>();
                if(!string.IsNullOrEmpty(directory)){
                    var result=Source==FileSource.Directory&&directory==DirectoryUri&&!Loading?new FileResult{ok=true,entries=Files.ToArray()}:await library.List(directory,token);
                    if(result.ok)entries=result.entries??Array.Empty<FileEntry>();
                }
                if(generation!=Generation||token.IsCancellationRequested||record!=Current)return;
                SubtitleCandidates=SubtitleMatcher.Find(record.name,entries);
                if(playlist.Count==0)playlist=entries.Where(e=>e.IsVideo).OrderBy(e=>e.name,StringComparer.OrdinalIgnoreCase).ToList();
                Notify();if(revision!=subtitleRevision)return;
                if(record.subtitleSelection==SubtitleSelection.Manual&&!string.IsNullOrEmpty(record.subtitle)&&!record.subtitle.StartsWith("track:"))await LoadSubtitle(record.subtitle);
                else if(record.subtitleSelection==SubtitleSelection.Auto&&(Store.Settings.autoLoadSubtitles||explicitEnable))await LoadSubtitleCandidates(SubtitleCandidates.Select(x=>x.uri).ToArray(),false);
            }catch(OperationCanceledException){}catch(Exception){if(generation==Generation&&!token.IsCancellationRequested)SetStatus("字幕目录读取失败，可手动选择 SRT");}
        }
        public Task LoadSubtitle(string uri)=>LoadSubtitleCandidates(new[]{uri},true);
        private async Task LoadSubtitleCandidates(string[] uris,bool manual)
        {
            if(Current==null){SetStatus("请先播放视频，再选择字幕");return;}
            CancelSubtitleSelection();subtitles=new CancellationTokenSource();var token=subtitles.Token;int generation=Generation,revision=subtitleRevision;var record=Current;
            if(manual){subtitleListing?.Cancel();record.subtitleSelection=SubtitleSelection.Manual;record.subtitle=uris[0];subtitleDocument=null;Backend?.SelectTrack("off",3);Store.Save();}
            string encoding=SubtitleEncoding;if(uris.Length>0)SetStatus("正在读取字幕…");
            try{
                var result=await SubtitleLoader.Load(uris,(uri,ct)=>library.Text(uri,encoding,ct),token);
                if(token.IsCancellationRequested||generation!=Generation||revision!=subtitleRevision||record!=Current)return;
                if(result.document==null){SetStatus((manual?"请重新选择字幕：":"自动字幕：")+result.error);Notify();return;}
                subtitleDocument=result.document;record.subtitle=result.uri;Backend.SelectTrack("off",3);Store.Save();
                SetStatus($"字幕已加载：{result.document.Cues.Count} 条"+(result.document.InvalidBlocks>0?$"，跳过 {result.document.InvalidBlocks} 个损坏块":""));Notify();
            }catch(OperationCanceledException){}
        }
        public void SetSubtitlesEnabled(bool enabled)
        {
            if(Current==null)return;if(!enabled){DisableSubtitles();return;}
            if(Current.subtitleSelection!=SubtitleSelection.Off)return;
            CancelSubtitleSelection();Current.subtitleSelection=Current.subtitleSelectionBeforeOff;
            string previous=Current.subtitleBeforeOff;
            if(!string.IsNullOrEmpty(previous)){
                if(previous.StartsWith("track:")){
                    string id=previous.Substring(6);Current.subtitle=previous;subtitleDocument=null;
                    var state=Backend?.Snapshot;
                    if(state!=null&&state.generation==Generation&&(state.tracks??Array.Empty<MediaTrack>()).Any(t=>t.type==3&&t.id==id&&t.supported))EmbeddedSubtitle(id);
                    else SetStatus("上次选择的内嵌字幕暂不可用，请重新选择");
                }else _ = LoadSubtitleCandidates(new[]{previous},Current.subtitleSelection==SubtitleSelection.Manual);
            }else{Current.subtitleSelection=SubtitleSelection.Auto;Current.subtitle="";_ = DiscoverSubtitles(Current.directory,Generation,true);}
            Store.Save();Notify();
        }
        public void DisableSubtitles(){CancelSubtitleSelection();subtitleListing?.Cancel();subtitleDocument=null;if(Current!=null){if(Current.subtitleSelection!=SubtitleSelection.Off){Current.subtitleBeforeOff=Current.subtitle;Current.subtitleSelectionBeforeOff=Current.subtitleSelection;}Current.subtitle="";Current.subtitleSelection=SubtitleSelection.Off;}Backend?.SelectTrack("off",3);Store.Save();SetStatus("字幕已关闭 · 已记住该片设置");Notify();}
        public void EmbeddedSubtitle(string id){CancelSubtitleSelection();subtitleListing?.Cancel();subtitleDocument=null;if(Current!=null){Current.subtitle="track:"+id;Current.subtitleSelection=SubtitleSelection.Manual;}Backend?.SelectTrack(id,3);Store.Save();Notify();}
    }
}
