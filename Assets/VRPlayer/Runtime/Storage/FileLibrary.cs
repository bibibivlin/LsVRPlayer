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
        // Keep reserved escapes (notably %, /, ? and #) so display edits cannot change path boundaries.
        public static string DisplayAddress(string location)
        {
            if(!Uri.TryCreate(location,UriKind.Absolute,out var uri)||(uri.Scheme!="file"&&uri.Scheme!="smb"))return location??"";
            return uri.GetComponents(UriComponents.AbsoluteUri,UriFormat.SafeUnescaped);
        }
        public static string ParseAddress(string text,string original="")
        {
            if(text==DisplayAddress(original))return original;
            if(Uri.TryCreate(text,UriKind.Absolute,out var uri)&&(uri.Scheme=="file"||uri.Scheme=="smb"))return uri.AbsoluteUri;
            return text;
        }
        public static bool IsSharedStorage(string location)
        {
            string path=location??"";
            if(Uri.TryCreate(path,UriKind.Absolute,out var uri)){
                if(uri.Scheme!="file")return false;
                path=uri.LocalPath;
            }
            return path=="/sdcard"||path.StartsWith("/sdcard/",StringComparison.Ordinal)||path=="/storage"||path.StartsWith("/storage/",StringComparison.Ordinal);
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private static Task<bool> permissionPrompt;
        private static readonly System.Collections.Generic.HashSet<string> requestedPermissions=new System.Collections.Generic.HashSet<string>();
#endif
        public static async Task<bool> TryRequestReadPermission(string location,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!IsSharedStorage(location))return false;
            using var version=new AndroidJavaClass("android.os.Build$VERSION");
            string permission=version.GetStatic<int>("SDK_INT")>=33?"android.permission.READ_MEDIA_VIDEO":"android.permission.READ_EXTERNAL_STORAGE";
            if(UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))return false;
            if(permissionPrompt==null||permissionPrompt.IsCompleted){
                if(!requestedPermissions.Add(permission))return false;
                permissionPrompt=RequestReadPermission(permission);
            }
            var prompt=permissionPrompt;
            // Cancelling a browser/open retires only its waiter; the system dialog is shared.
            while(!prompt.IsCompleted)await Task.Delay(50,token);
            token.ThrowIfCancellationRequested();return await prompt;
#else
            await Task.CompletedTask;return false;
#endif
        }
        public static Func<string,Task<bool>> ReadPermissionRationale;
        public static async Task<FileResult> ExportLicenseSource(CancellationToken token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer");using var activity=unity.GetStatic<AndroidJavaObject>("currentActivity");
            int id=AndroidBridge.Api.CallStatic<int>("beginExportLicenseSource",activity);return await AwaitResult(id,token);
#else
            await Task.CompletedTask;return new FileResult{error="源码随 APK 打包；请在头显中导出 / Export is available on the headset"};
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<bool> RequestReadPermission(string permission)
        {
            if(ReadPermissionRationale!=null&&!await ReadPermissionRationale(permission)){requestedPermissions.Remove(permission);return false;}
            var result=new TaskCompletionSource<bool>();
            var callbacks=new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted+=_=>result.TrySetResult(true);
            callbacks.PermissionDenied+=_=>result.TrySetResult(false);
            callbacks.PermissionRequestDismissed+=_=>result.TrySetResult(false);
            UnityEngine.Android.Permission.RequestUserPermission(permission,callbacks);return await result.Task;
        }
#endif
        public async Task<FileResult> List(string uri,CancellationToken token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int id=AndroidBridge.Api.CallStatic<int>("beginList",uri);var result=await AwaitResult(id,token);
            if(!result.ok&&await TryRequestReadPermission(uri,token)){
                id=AndroidBridge.Api.CallStatic<int>("beginList",uri);result=await AwaitResult(id,token);
            }
            if(!result.ok&&IsSharedStorage(uri))result.error="无法读取目录，请检查系统存储权限或目录是否存在";
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
            int id=AndroidBridge.Api.CallStatic<int>("beginText",uri,encoding);var result=await AwaitResult(id,token);
            if(!result.ok&&await TryRequestReadPermission(uri,token)){
                id=AndroidBridge.Api.CallStatic<int>("beginText",uri,encoding);result=await AwaitResult(id,token);
            }
            return result;
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
            if(location.StartsWith("content:",StringComparison.OrdinalIgnoreCase))return null; // SAF document IDs are opaque; directory rows record their actual parents.
            if(location.StartsWith("smb:",StringComparison.OrdinalIgnoreCase)){if(!Uri.TryCreate(location,UriKind.Absolute,out var uri))return null;string path=uri.AbsolutePath.TrimEnd('/');int slash=path.LastIndexOf('/');return slash>=0&&!string.IsNullOrEmpty(path)?uri.GetLeftPart(UriPartial.Authority)+path.Substring(0,slash)+"/":null;}
            try{string path=Path.GetFullPath(location.StartsWith("file:")?new Uri(location).LocalPath:location);if(path==Path.GetPathRoot(path))return null;path=path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);string parent=Directory.GetParent(path)?.FullName;return parent==null?null:new Uri(parent).AbsoluteUri;}catch{return null;}
        }
    }
}
