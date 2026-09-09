using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRPlayer
{
    public sealed class FileLibrary
    {
        public async Task<FileResult> List(string uri,CancellationToken token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int id=AndroidBridge.Api.CallStatic<int>("beginList",uri);var result=await AwaitResult(id,token);
            if(result.ok&&result.entries!=null)foreach(var entry in result.entries)entry.metadataVerified=true;
            return result;
#else
            return await Task.Run(()=>{
                try{var path=uri.StartsWith("file:")?new Uri(uri).LocalPath:uri;var files=new DirectoryInfo(path).EnumerateFileSystemInfos().Where(x=>!x.Name.StartsWith(".")).Select(x=>{token.ThrowIfCancellationRequested();return new FileEntry{name=x.Name,uri=new Uri(x.FullName).AbsoluteUri,directory=x is DirectoryInfo,size=x is FileInfo f?f.Length:0,modified=new DateTimeOffset(x.LastWriteTimeUtc).ToUnixTimeMilliseconds(),metadataVerified=true};}).ToArray();return new FileResult{ok=true,entries=files};}
                catch(OperationCanceledException){throw;}catch(Exception ex){return new FileResult{error=ex.Message};}
            },token);
#endif
        }
        public async Task<FileResult> Text(string uri,string encoding,CancellationToken token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int id=AndroidBridge.Api.CallStatic<int>("beginText",uri,encoding);return await AwaitResult(id,token);
#else
            return await Task.Run(()=>{try{string path=uri.StartsWith("file:")?new Uri(uri).LocalPath:uri;if(new FileInfo(path).Length>4*1024*1024)return new FileResult{error="字幕超过 4 MB 限制"};token.ThrowIfCancellationRequested();return new FileResult{ok=true,text=File.ReadAllText(path,encoding=="Auto"?System.Text.Encoding.UTF8:System.Text.Encoding.GetEncoding(encoding))};}catch(Exception ex){return new FileResult{error=ex.Message};}},token);
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<FileResult> AwaitResult(int id,CancellationToken token)
        {
            float deadline=Time.realtimeSinceStartup+60;
            try{while(true){token.ThrowIfCancellationRequested();string json=AndroidBridge.Api.CallStatic<string>("takeResult",id);if(!string.IsNullOrEmpty(json))return JsonUtility.FromJson<FileResult>(json);if(Time.realtimeSinceStartup>deadline)return new FileResult{error="读取超时，请检查网络后重试"};await Task.Delay(60,token);}}
            finally{AndroidBridge.Api.CallStatic("cancelRequest",id);}
        }
#endif
        public static string Parent(string location)
        {
            if(string.IsNullOrEmpty(location))return null;
            if(location.StartsWith("content:",StringComparison.OrdinalIgnoreCase))return null; // SAF parents are navigated via the explicit directory stack.
            if(location.StartsWith("smb:",StringComparison.OrdinalIgnoreCase)){if(!Uri.TryCreate(location,UriKind.Absolute,out var uri))return null;string path=uri.AbsolutePath.TrimEnd('/');int slash=path.LastIndexOf('/');return slash>=0&&!string.IsNullOrEmpty(path)?uri.GetLeftPart(UriPartial.Authority)+path.Substring(0,slash)+"/":null;}
            try{string path=location.StartsWith("file:")?new Uri(location).LocalPath:location;string parent=Directory.GetParent(path)?.FullName;return parent==null?null:new Uri(parent).AbsoluteUri;}catch{return null;}
        }
    }
}
