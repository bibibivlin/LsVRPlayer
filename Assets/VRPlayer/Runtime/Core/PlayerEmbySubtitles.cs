using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VRPlayer
{
    public static class EmbySubtitleIdentity
    {
        public static string Create(string accountId,string itemId,string sourceId,int index,string format="srt")=>
            "emby-subtitle://"+accountId+"/"+Uri.EscapeDataString(itemId)+"/"+Uri.EscapeDataString(sourceId)+"/"+index+"."+(format=="ass"||format=="ssa"?format:"srt");
        public static bool TryParse(string value,out string accountId,out string itemId,out string sourceId,out int index)
            =>TryParse(value,out accountId,out itemId,out sourceId,out index,out _);
        public static bool TryParse(string value,out string accountId,out string itemId,out string sourceId,out int index,out string format)
        {
            accountId=itemId=sourceId="";format="";index=-1;
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="emby-subtitle"||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))return false;
            var parts=uri.AbsolutePath.Trim('/').Split('/');
            if(parts.Length!=3||!SubtitleFormat.IsSupported(parts[2])||!int.TryParse(parts[2].Substring(0,parts[2].Length-4),out index)||index<0)return false;
            format=SubtitleFormat.Extension(parts[2]).Substring(1);
            accountId=uri.Host;itemId=Uri.UnescapeDataString(parts[0]);sourceId=Uri.UnescapeDataString(parts[1]);
            return accountId.Length>0&&itemId.Length>0&&sourceId.Length>0;
        }
    }

    public sealed partial class PlayerApp
    {
        private async Task DiscoverEmbySubtitles(PlaybackRecord record,int generation,int revision,bool explicitEnable,CancellationToken token)
        {
            EmbyIdentity.TryParse(record.uri,out string accountId,out string itemId);
            var account=EmbyAccounts.FirstOrDefault(a=>a.id==accountId);if(account==null)return;
            var item=embyPlayback?.item??await EmbyApi.GetItem(account,itemId,token);
            var source=embyPlayback?.source??item.MediaSources?.FirstOrDefault();
            if(token.IsCancellationRequested||generation!=Generation||record!=Current)return;
            SubtitleCandidates=(source?.MediaStreams??Array.Empty<EmbyMediaStream>())
                .Where(s=>s.Type=="Subtitle"&&s.IsExternal&&(s.Codec=="srt"||s.Codec=="subrip"||s.Codec=="ass"||s.Codec=="ssa"))
                .Select(s=>{string format=s.Codec=="ass"||s.Codec=="ssa"?s.Codec:"srt";return new FileEntry{name=(s.DisplayTitle??s.Language??"外部字幕")+"."+format,uri=EmbySubtitleIdentity.Create(accountId,itemId,source.Id,s.Index,format)};})
                .ToList();
            Notify();if(revision!=subtitleRevision)return;
            if(record.subtitleSelection==SubtitleSelection.Manual&&!string.IsNullOrEmpty(record.subtitle)&&!record.subtitle.StartsWith("track:"))await LoadSubtitle(record.subtitle);
            else if(record.subtitleSelection==SubtitleSelection.Auto&&(Store.Settings.autoLoadSubtitles||explicitEnable))await LoadSubtitleCandidates(SubtitleCandidates.Select(s=>s.uri).ToArray(),false);
        }

        private async Task<FileResult> ReadPlayerSubtitle(string uri,string encoding,CancellationToken token)
        {
            if(!EmbySubtitleIdentity.TryParse(uri,out string accountId,out string itemId,out string sourceId,out int index,out string format))return await library.Text(uri,encoding,token);
            var account=EmbyAccounts.FirstOrDefault(a=>a.id==accountId);
            if(account==null||string.IsNullOrEmpty(account.accessToken))return new FileResult{error="此 Emby 字幕需要重新登录对应账户"};
            string text=await EmbyApi.GetSubtitle(account,itemId,sourceId,index,token,format);
            return new FileResult{ok=true,text=text};
        }
    }
}
