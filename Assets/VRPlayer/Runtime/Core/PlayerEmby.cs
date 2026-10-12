using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRPlayer
{
    public enum EmbyPage { Accounts, Home, Library, Details, Person }
    public sealed class EmbyBrowserState
    {
        public EmbyPage page=EmbyPage.Accounts;
        public string title="Emby 服务器与账户",error="",search="";
        public string filterKind="",filterValue="",filterId="";
        public string viewLibraryId="";
        public bool loading,canGoBack;
        public int revision,totalCount,startIndex;
        public EmbyItem[] views=Array.Empty<EmbyItem>(),resume=Array.Empty<EmbyItem>(),latest=Array.Empty<EmbyItem>(),items=Array.Empty<EmbyItem>(),parts=Array.Empty<EmbyItem>(),extras=Array.Empty<EmbyItem>();
        public EmbyItem selected,parent;
    }

    public sealed partial class PlayerApp
    {
        public EmbyClient EmbyApi{get;private set;}
        public EmbyBrowserState EmbyState{get;private set;}=new EmbyBrowserState();
        public IReadOnlyList<EmbyAccount> EmbyAccounts=>embyAccounts?.Accounts??(IReadOnlyList<EmbyAccount>)Array.Empty<EmbyAccount>();
        public EmbyAccount ActiveEmbyAccount{get;private set;}
        public int EmbyPendingReports=>embyPendingReports;
        public string[] EmbyPlaybackPartIds=>embyMovieParts?.Items.Select(x=>x.Id).ToArray()??Array.Empty<string>();
        public int EmbyPlaybackPartIndex=>embyMovieParts?.IndexOfUri(Current?.uri)??-1;
        public string EmbyPlaybackQueueKind=>embyMovieParts!=null?"Parts":embyMovieExtras!=null?"Extras":"None";
        public string[] EmbyPlaybackQueueIds=>embyMovieParts!=null?EmbyPlaybackPartIds:embyMovieExtras?.Items.Select(x=>x.Id).ToArray()??Array.Empty<string>();
        public int EmbyPlaybackQueueIndex=>embyMovieParts!=null?EmbyPlaybackPartIndex:embyMovieExtras?.IndexOfUri(Current?.uri)??-1;
        public string EmbyPlaybackMainId{get{EmbyIdentity.TryParse(Current?.uri,out _,out string id);return embyMovieParts?.MainId??embyMovieExtras?.MainId??id;}}
        private EmbyAccountStore embyAccounts;
        private CancellationTokenSource embyBrowsing,embyOpening;
        private readonly Stack<EmbyBrowserState> embyPages=new Stack<EmbyBrowserState>();
        private int embyRevision;
        private EmbyPlayback embyPlayback;
        private EmbyAccount embyPlaybackAccount;
        private int embyPlaybackGeneration;
        private bool embyReportedStart,embyWasPlaying;
        private long embyLastPosition;
        private float embyReportAt;
        private Task embyReports=Task.CompletedTask;
        private int embyPendingReports;
        private bool embyDisposed;
        private EmbyMovieParts embyMovieParts;
        private EmbyMovieExtras embyMovieExtras;
        private bool embyRecentReturn;

        private void InitializeEmby()
        {
            EmbyApi=new EmbyClient();
            embyAccounts=new EmbyAccountStore(Path.Combine(Application.persistentDataPath,"emby-accounts.json"));
            ActiveEmbyAccount=EmbyAccounts.FirstOrDefault(a=>a.id==embyAccounts.SelectedId);
        }

        public void ShowEmby()
        {
            embyRecentReturn=false;
            StopDiscovery();
            if(Source==FileSource.Emby){
                listing?.Cancel();listing?.Dispose();listing=null;Loading=false;CancelEmbyBrowsing(false);
            }else CancelListing();
            directories.Clear();DirectoryUri="";Files=new List<FileEntry>();Source=FileSource.Emby;
            if(ActiveEmbyAccount!=null&&!string.IsNullOrEmpty(ActiveEmbyAccount.accessToken)){EmbyHome();return;}
            ShowEmbyAccounts();
        }

        public void ShowEmbyAccounts()
        {
            CancelEmbyBrowsing(false);embyPages.Clear();Source=FileSource.Emby;
            PublishEmby(new EmbyBrowserState());
        }

        public void SelectEmbyAccount(string id)
        {
            var account=EmbyAccounts.FirstOrDefault(a=>a.id==id);
            if(account==null)return;
            CancelEmbyBrowsing();CancelEmbyPlaybackRequest();
            try{embyAccounts.SelectedId=id;embyAccounts.Save();}
            catch(Exception ex){PublishEmby(new EmbyBrowserState{error=EmbyClient.SafeError(ex)});return;}
            ActiveEmbyAccount=account;embyPages.Clear();Source=FileSource.Emby;
            if(string.IsNullOrEmpty(account.accessToken)){
                PublishEmby(new EmbyBrowserState{error="请重新登录此账户"});return;
            }
            EmbyHome();
        }

        public async Task LoginEmby(string serverUrl,string username,string password,bool remember)
        {
            if(EmbyApi==null)return;
            CancelEmbyPlaybackRequest();
            var token=BeginEmbyRequest(new EmbyBrowserState{loading=true,title="正在登录 Emby…"});
            EmbyAccount authenticated=null;bool saved=false;
            try{
                authenticated=await EmbyApi.Authenticate(serverUrl,username,password,token);
                if(token.IsCancellationRequested||embyDisposed)return;
                CancelEmbyPlaybackRequest();
                authenticated.remember=remember;embyAccounts.Upsert(authenticated);saved=true;ActiveEmbyAccount=authenticated;
                embyPages.Clear();EmbyHome();
            }catch(OperationCanceledException){}catch(Exception ex){FailEmby(token,ex);}
            finally{
                if(authenticated!=null&&!saved){try{await EmbyApi.Logout(authenticated,CancellationToken.None);}catch(Exception){}}
            }
        }

        public void RemoveEmbyAccount(string id)
        {
            var account=EmbyAccounts.FirstOrDefault(a=>a.id==id);if(account==null)return;
            CancelEmbyBrowsing();CancelEmbyPlaybackRequest();
            try{embyAccounts.Remove(id);}
            catch(Exception ex){PublishEmby(new EmbyBrowserState{error=EmbyClient.SafeError(ex)});return;}
            if(embyPlaybackAccount?.id==id)Stop();
            if(ActiveEmbyAccount?.id==id)ActiveEmbyAccount=null;
            // Logout follows the final playback check-in for this captured account.
            var captured=CopyEmbyAccount(account);
            embyPendingReports++;
            embyReports=LogoutAfterReports(embyReports,captured);
            ShowEmbyAccounts();
        }

        private async Task LogoutAfterReports(Task previous,EmbyAccount account)
        {
            try{await previous;if(!string.IsNullOrEmpty(account.accessToken))await EmbyApi.Logout(account,CancellationToken.None);}catch(Exception){}
            finally{embyPendingReports--;}
        }

        public void EmbyHome()
        {
            if(ActiveEmbyAccount==null){ShowEmbyAccounts();return;}
            embyPages.Clear();Source=FileSource.Emby;_ = LoadEmbyHome();
        }

        private async Task LoadEmbyHome()
        {
            var account=ActiveEmbyAccount;if(account==null)return;
            var state=new EmbyBrowserState{page=EmbyPage.Home,title=account.name,loading=true};
            var token=BeginEmbyRequest(state);
            try{
                var views=EmbyApi.GetViews(account,token);var resume=EmbyApi.GetResume(account,token);var latest=EmbyApi.GetLatest(account,"",token);
                await Task.WhenAll(views,resume,latest);
                if(token.IsCancellationRequested||ActiveEmbyAccount!=account)return;
                state.views=views.Result.Items??Array.Empty<EmbyItem>();state.resume=resume.Result.Items??Array.Empty<EmbyItem>();state.latest=latest.Result.Items??Array.Empty<EmbyItem>();
                state.loading=false;PublishEmby(state);
            }catch(OperationCanceledException){}catch(Exception ex){FailEmby(token,ex,account);}
        }

        public void OpenEmbyLibrary(EmbyItem item,int startIndex=0,string search="")
        {
            if(item==null||ActiveEmbyAccount==null)return;
            if(EmbyState.page!=EmbyPage.Library||EmbyState.parent?.Id!=item.Id)RememberEmbyPage();
            _ = LoadEmbyLibrary(item,startIndex,search);
        }

        public void SearchEmbyLibrary(string search)
        {
            if(EmbyState.page!=EmbyPage.Library||ActiveEmbyAccount==null)return;
            if(!string.IsNullOrEmpty(EmbyState.filterKind))
                _ = LoadEmbyCategory(EmbyState.filterKind,EmbyState.filterValue,EmbyState.filterId,search);
            else if(EmbyState.parent!=null)_ = LoadEmbyLibrary(EmbyState.parent,0,search);
        }

        private async Task LoadEmbyLibrary(EmbyItem parent,int startIndex,string search)
        {
            var account=ActiveEmbyAccount;if(account==null)return;
            string libraryId=parent.Type=="CollectionFolder"||parent.Type=="UserView"||EmbyState.page==EmbyPage.Home||string.IsNullOrEmpty(EmbyState.viewLibraryId)?parent.Id:EmbyState.viewLibraryId;
            var state=new EmbyBrowserState{page=EmbyPage.Library,parent=parent,title=parent.Name,loading=true,search=search??"",canGoBack=true,viewLibraryId=libraryId};
            var token=BeginEmbyRequest(state);
            try{
                var metadata=string.IsNullOrEmpty(parent.Type)?await EmbyApi.GetItem(account,parent.Id,token):parent;
                var result=await EmbyApi.GetAllItems(account,metadata,state.search,token);
                if(token.IsCancellationRequested||ActiveEmbyAccount!=account)return;
                state.parent=metadata;state.title=metadata.Name;
                state.items=result.Items??Array.Empty<EmbyItem>();state.totalCount=result.TotalRecordCount;state.loading=false;PublishEmby(state);
            }catch(OperationCanceledException){}catch(Exception ex){FailEmby(token,ex,account);}
        }

        public void OpenEmbyItem(EmbyItem item)
        {
            if(item==null||ActiveEmbyAccount==null)return;
            if(item.Type=="Person"){OpenEmbyPerson(new EmbyPerson{Id=item.Id,Name=item.Name});return;}
            if(item.IsBrowsable){OpenEmbyLibrary(item);return;}
            RememberEmbyPage();_ = LoadEmbyDetails(item);
        }

        private async Task LoadEmbyDetails(EmbyItem item,string directory=null,string playbackItemId=null,CancellationToken playbackToken=default)
        {
            var account=ActiveEmbyAccount;if(account==null)return;
            var state=new EmbyBrowserState{page=EmbyPage.Details,title=item.Name,selected=item,loading=true,canGoBack=true,viewLibraryId=EmbyState.viewLibraryId};
            var token=BeginEmbyRequest(state);
            try{
                await embyReports;token.ThrowIfCancellationRequested();
                var detail=await EmbyApi.GetItem(account,item.Id,token);
                if(directory!=null)detail=await ResolveEmbyContainingItem(account,detail,directory,token);
                var parts=EmbyApi.GetAdditionalParts(account,detail.Id,token);
                var extras=EmbyApi.GetExtras(account,detail.Id,token);
                await Task.WhenAll(parts,extras);
                if(token.IsCancellationRequested||ActiveEmbyAccount!=account)return;
                state.selected=detail;state.title=detail.Name;
                state.parts=EmbyMovieParts.Create(account.id,detail,parts.Result)?.Items.ToArray()??Array.Empty<EmbyItem>();
                state.extras=extras.Result??Array.Empty<EmbyItem>();state.loading=false;PublishEmby(state);
                if(playbackItemId!=null&&!token.IsCancellationRequested&&!playbackToken.IsCancellationRequested&&ActiveEmbyAccount==account&&Source==FileSource.Emby){
                    var sequence=EmbyMovieParts.FromDetails(account.id,state);
                    var features=EmbyMovieExtras.FromDetails(account.id,state);
                    bool isPart=sequence?.IndexOf(playbackItemId)>=0,isExtra=features?.IndexOf(playbackItemId)>=0;
                    if(detail.Id==playbackItemId||isPart||isExtra)
                        _ = ResolveEmbyVideo(account,playbackItemId,directory,null,false,true,isPart?sequence:null,isExtra?features:null);
                }
            }catch(OperationCanceledException){}catch(Exception ex){FailEmby(token,ex,account);}
            finally{if(playbackItemId!=null&&embyOpening?.Token==playbackToken){embyOpening.Dispose();embyOpening=null;}}
        }

        public void OpenEmbyPerson(EmbyPerson person)
        {
            if(person==null||string.IsNullOrEmpty(person.Id)||ActiveEmbyAccount==null)return;
            RememberEmbyPage();_ = LoadEmbyPerson(person);
        }

        private async Task LoadEmbyPerson(EmbyPerson person)
        {
            var account=ActiveEmbyAccount;if(account==null)return;
            var state=new EmbyBrowserState{page=EmbyPage.Person,title=person.Name,selected=new EmbyItem{Id=person.Id,Name=person.Name,Type="Person"},loading=true,canGoBack=true,viewLibraryId=EmbyState.viewLibraryId};
            var token=BeginEmbyRequest(state);
            try{
                var detail=EmbyApi.GetPerson(account,person.Id,token);var movies=EmbyApi.GetPersonMovies(account,person.Id,token);
                await Task.WhenAll(detail,movies);
                if(token.IsCancellationRequested||ActiveEmbyAccount!=account)return;
                state.selected=detail.Result;state.title=state.selected.Name;state.items=movies.Result.Items??Array.Empty<EmbyItem>();
                state.totalCount=movies.Result.TotalRecordCount;state.loading=false;PublishEmby(state);
            }catch(OperationCanceledException){}catch(Exception ex){FailEmby(token,ex,account);}
        }

        public void OpenEmbyCategory(string kind,string name,string id="")
        {
            if(ActiveEmbyAccount==null||string.IsNullOrWhiteSpace(name)||(kind!="Genre"&&kind!="Tag"&&kind!="Studio"))return;
            RememberEmbyPage();_ = LoadEmbyCategory(kind,name,id,"");
        }

        private async Task LoadEmbyCategory(string kind,string value,string id,string search)
        {
            var account=ActiveEmbyAccount;if(account==null)return;
            var state=new EmbyBrowserState{page=EmbyPage.Library,title=value,filterKind=kind,filterValue=value,filterId=id??"",search=search??"",loading=true,canGoBack=true,viewLibraryId=EmbyState.viewLibraryId};
            var token=BeginEmbyRequest(state);
            try{
                var result=await EmbyApi.GetFilteredMovies(account,kind,value,id,state.search,token);
                if(token.IsCancellationRequested||ActiveEmbyAccount!=account)return;
                state.items=result.Items??Array.Empty<EmbyItem>();state.totalCount=result.TotalRecordCount;state.loading=false;PublishEmby(state);
            }catch(OperationCanceledException){}catch(Exception ex){FailEmby(token,ex,account);}
        }

        public void EmbyBack()
        {
            CancelEmbyBrowsing(false);
            if(embyPages.Count>0){PublishEmby(embyPages.Pop());return;}
            if(ReturnToRecentFromEmby())return;
            if(EmbyState.page==EmbyPage.Home)ShowEmbyAccounts();else EmbyHome();
        }

        public void RefreshEmby()
        {
            switch(EmbyState.page){
                case EmbyPage.Home:_ = LoadEmbyHome();break;
                case EmbyPage.Library:SearchEmbyLibrary(EmbyState.search);break;
                case EmbyPage.Details:if(EmbyState.selected!=null)_ = LoadEmbyDetails(EmbyState.selected);break;
                case EmbyPage.Person:if(EmbyState.selected!=null)_ = LoadEmbyPerson(new EmbyPerson{Id=EmbyState.selected.Id,Name=EmbyState.selected.Name});break;
                default:ShowEmbyAccounts();break;
            }
        }

        private void RememberEmbyPage(){if(!EmbyState.loading)embyPages.Push(EmbyState);}
        private CancellationToken BeginEmbyRequest(EmbyBrowserState state)
        {
            CancelEmbyBrowsing(false);embyBrowsing=new CancellationTokenSource();Source=FileSource.Emby;PublishEmby(state);return embyBrowsing.Token;
        }
        public void CancelEmbyBrowsing(bool cancelPlayback=true)
        {
            embyBrowsing?.Cancel();embyBrowsing?.Dispose();embyBrowsing=null;
            if(EmbyState.loading){EmbyState.loading=false;EmbyState.revision=++embyRevision;}
            if(cancelPlayback){embyRecentReturn=false;CancelEmbyPlaybackRequest();}
        }
        private void PublishEmby(EmbyBrowserState state)
        {
            if(embyDisposed)return;
            EmbyState=state;state.revision=++embyRevision;Status=state.loading?"正在连接 Emby…":string.IsNullOrEmpty(state.error)?"Emby · "+state.title:state.error;Notify();
        }
        private void FailEmby(CancellationToken token,Exception error,EmbyAccount requestAccount=null)
        {
            if(token.IsCancellationRequested||embyDisposed)return;
            if(error is EmbyAuthenticationException){
                if(requestAccount!=null){requestAccount.accessToken="";try{embyAccounts?.Save();}catch(Exception){}}
                embyPages.Clear();EmbyState=new EmbyBrowserState();
            }
            EmbyState.loading=false;EmbyState.error=EmbyClient.SafeError(error);PublishEmby(EmbyState);
        }

        public void PlayEmby(EmbyItem item,bool resume=true)
        {
            if(item==null||ActiveEmbyAccount==null)return;
            var parts=EmbyState.selected?.Id==item.Id?EmbyMovieParts.FromDetails(ActiveEmbyAccount.id,EmbyState):null;
            _ = ResolveEmbyVideo(ActiveEmbyAccount,item.Id,null,null,false,resume,parts);
        }

        public EmbyPartResume EmbyMovieResume
        {
            get{
                var parts=ActiveEmbyAccount==null?null:EmbyMovieParts.FromDetails(ActiveEmbyAccount.id,EmbyState);
                if(parts==null)return null;
                var snapshot=Backend?.Snapshot;
                if(parts.IndexOfUri(Current?.uri)>=0&&snapshot?.generation==Generation&&snapshot.state==3)
                    return ReadEmbyPartProgress(parts,parts.Items[parts.IndexOfUri(Current.uri)]);
                return parts.LastPlayed(Store==null?null:new Func<string,PlaybackRecord>(Store.Find));
            }
        }

        public EmbyPartResume EmbyPartProgress(EmbyItem item)
        {
            var parts=ActiveEmbyAccount==null?null:EmbyMovieParts.FromDetails(ActiveEmbyAccount.id,EmbyState);
            return parts==null?null:ReadEmbyPartProgress(parts,item);
        }

        private EmbyPartResume ReadEmbyPartProgress(EmbyMovieParts parts,EmbyItem item)
        {
            var progress=parts.ProgressFor(item,Store==null?null:new Func<string,PlaybackRecord>(Store.Find));
            var snapshot=Backend?.Snapshot;
            if(progress!=null&&Current?.uri==EmbyIdentity.Create(parts.AccountId,item.Id)&&snapshot?.generation==Generation&&snapshot.state==3)
                return new EmbyPartResume(item,Math.Min(Math.Max(0,snapshot.position),long.MaxValue/10000)*10000,DateTimeOffset.UtcNow);
            return progress;
        }

        public void ContinueEmbyMovie()
        {
            var target=EmbyMovieResume;
            if(target==null){PlayEmby(EmbyState.selected,true);return;}
            var parts=EmbyMovieParts.FromDetails(ActiveEmbyAccount.id,EmbyState);
            _ = ResolveEmbyVideo(ActiveEmbyAccount,target.Item.Id,null,null,false,true,parts);
        }

        public void PlayEmbyPart(EmbyItem item)
        {
            if(item==null||ActiveEmbyAccount==null)return;
            var parts=EmbyMovieParts.FromDetails(ActiveEmbyAccount.id,EmbyState);
            if(parts==null||parts.IndexOf(item.Id)<0)return;
            var inherited=embyMovieParts?.SameMovie(parts)==true&&embyMovieParts.IndexOfUri(Current?.uri)>=0?Presenter?.Settings.Copy():null;
            _ = ResolveEmbyVideo(ActiveEmbyAccount,item.Id,null,inherited,false,true,parts);
        }

        public void PlayEmbyExtra(EmbyItem item)
        {
            if(item==null||ActiveEmbyAccount==null||!EmbyState.extras.Any(x=>x.Id==item.Id))return;
            var extras=EmbyMovieExtras.FromDetails(ActiveEmbyAccount.id,EmbyState);
            if(extras==null||extras.IndexOf(item.Id)<0)return;
            _ = ResolveEmbyVideo(ActiveEmbyAccount,item.Id,null,null,false,true,null,extras);
        }

        private bool TryOpenEmby(FileEntry entry,string directory,ViewSettings inheritedFormat,bool keepPlaylist,EmbyPlayback resolved)
        {
            if(resolved!=null||!EmbyIdentity.TryParse(entry.uri,out string accountId,out string itemId))return false;
            var account=EmbyAccounts.FirstOrDefault(a=>a.id==accountId);
            if(account==null||string.IsNullOrEmpty(account.accessToken)){ShowEmbyAccounts();SetStatus("该 Emby 记录需要重新添加或登录对应账户");return true;}
            var parts=keepPlaylist&&embyMovieParts?.IndexOfUri(entry.uri)>=0?embyMovieParts:null;
            var extras=keepPlaylist&&embyMovieExtras?.IndexOfUri(entry.uri)>=0?embyMovieExtras:null;
            _ = ResolveEmbyVideo(account,itemId,directory,inheritedFormat,keepPlaylist,true,parts,extras);return true;
        }

        private async Task ResolveEmbyVideo(EmbyAccount account,string itemId,string directory,ViewSettings inheritedFormat,bool keepPlaylist,bool resume,EmbyMovieParts parts=null,EmbyMovieExtras extras=null)
        {
            CancelEmbyPlaybackRequest();embyOpening=new CancellationTokenSource();var token=embyOpening.Token;
            int generation=Generation;var captured=CopyEmbyAccount(account);
            SetStatus("正在准备 Emby 原始视频…");
            try{
                // Check-ins from the preceding playback must reach the server before asking for resume data.
                await embyReports;
                token.ThrowIfCancellationRequested();
                var item=await EmbyApi.GetItem(captured,itemId,token);
                if(item.IsBrowsable){if(!token.IsCancellationRequested){ActiveEmbyAccount=account;Source=FileSource.Emby;OpenEmbyLibrary(item);}return;}
                if(extras==null&&parts==null){
                    parts=await ResolveEmbyParts(captured,item,directory,token);
                    if(parts==null)extras=await ResolveEmbyExtras(captured,item,directory,token);
                }
                var partProgress=resume?parts?.ProgressFor(item,Store==null?null:new Func<string,PlaybackRecord>(Store.Find)):null;
                long position=resume?Math.Max(0,(partProgress?.PositionTicks??item.UserData?.PlaybackPositionTicks??0)/10000):0;
                var playback=await EmbyApi.GetPlayback(captured,itemId,position,token);
                if(token.IsCancellationRequested||generation!=Generation||embyDisposed)return;
                var currentState=Backend?.Snapshot;
                if(resume&&Current?.uri==EmbyIdentity.Create(account.id,itemId)&&currentState?.generation==Generation&&currentState.state==3)position=currentState.position;
                playback.startPositionMs=position;
                string parent=directory;
                if(parts!=null)parent=EmbyIdentity.Create(account.id,parts.MainId);
                else if(extras!=null)parent=EmbyIdentity.Create(account.id,extras.MainId);
                if(string.IsNullOrEmpty(parent))parent=EmbyIdentity.Create(account.id,string.IsNullOrEmpty(item.ParentId)?"library":item.ParentId);
                var entry=EmbyFile(account,playback.item??item,playback.source);
                var siblings=parts!=null?parts.Items.Select(x=>EmbyFile(account,x,null)).ToList():
                    extras!=null?extras.Items.Select(x=>EmbyFile(account,x,null)).ToList():new List<FileEntry>{entry};
                embyOpening.Dispose();embyOpening=null;
                OpenVideo(entry,parent,inheritedFormat,keepPlaylist,playback);
                CompleteEmbyOpen(parts,siblings,keepPlaylist,entry.uri,generation+1,extras);
            }catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested&&!embyDisposed){if(ex is EmbyAuthenticationException)FailEmby(token,ex,account);else SetStatus(EmbyClient.SafeError(ex));}}
        }

        private async Task<EmbyMovieParts> ResolveEmbyParts(EmbyAccount account,EmbyItem item,string directory,CancellationToken token)
        {
            if(item.IsExtra)return null;
            var main=item;
            string parentId="";
            if(EmbyIdentity.TryParse(directory,out string accountId,out string directoryId)&&accountId==account.id&&directoryId!=item.Id&&directoryId!="library")parentId=directoryId;
            else if(item.IsAdditionalPart)parentId=item.ParentId;
            if(!string.IsNullOrEmpty(parentId)){
                var candidate=await EmbyApi.GetItem(account,parentId,token);
                if(!candidate.IsBrowsable&&!candidate.IsExtra)main=candidate;
            }
            var additional=await EmbyApi.GetAdditionalParts(account,main.Id,token);
            var parts=EmbyMovieParts.Create(account.id,main,additional);
            return parts?.IndexOf(item.Id)>=0?parts:null;
        }

        private async Task<EmbyMovieExtras> ResolveEmbyExtras(EmbyAccount account,EmbyItem item,string directory,CancellationToken token)
        {
            if(!EmbyIdentity.TryParse(directory,out string accountId,out string mainId)||accountId!=account.id||mainId==item.Id||mainId=="library")return null;
            var main=await EmbyApi.GetItem(account,mainId,token);
            if(main.IsBrowsable||main.IsExtra||!main.IsPlayable)return null;
            var extras=EmbyMovieExtras.Create(account.id,main.Id,await EmbyApi.GetExtras(account,main.Id,token));
            return extras?.IndexOf(item.Id)>=0?extras:null;
        }

        private async Task<EmbyItem> ResolveEmbyContainingItem(EmbyAccount account,EmbyItem item,string directory,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(!item.IsAdditionalPart&&(!EmbyIdentity.TryParse(directory,out string accountId,out string parentId)||
                accountId!=account.id||parentId==item.Id||parentId=="library"))return item;
            try{
                var parts=await ResolveEmbyParts(account,item,directory,token);
                var extras=parts==null?await ResolveEmbyExtras(account,item,directory,token):null;
                string mainId=EmbyContainingMainId(account.id,item.Id,parts,extras);
                return mainId==item.Id?item:await EmbyApi.GetItem(account,mainId,token);
            }catch(OperationCanceledException){throw;}catch(EmbyAuthenticationException){throw;}
            catch(Exception){token.ThrowIfCancellationRequested();return item;}
        }

        private static string EmbyContainingMainId(string accountId,string itemId,EmbyMovieParts parts,EmbyMovieExtras extras)
        {
            if(parts?.AccountId==accountId&&parts.IndexOf(itemId)>=0)return parts.MainId;
            if(extras?.AccountId==accountId&&extras.IndexOf(itemId)>=0)return extras.MainId;
            return itemId;
        }

        private bool InheritEmbyPartFormat(string targetUri)=>embyMovieParts?.IndexOfUri(Current?.uri)>=0&&embyMovieParts.IndexOfUri(targetUri)>=0;

        private bool AllowEmbyAdjacent(string targetUri,bool automatic)
        {
            if(!EmbyIdentity.TryParse(Current?.uri,out _,out _))return true;
            if(embyMovieParts?.IndexOfUri(Current.uri)>=0)return embyMovieParts.IndexOfUri(targetUri)>=0;
            return !automatic&&embyMovieExtras?.IndexOfUri(Current.uri)>=0&&embyMovieExtras.IndexOfUri(targetUri)>=0;
        }

        private bool CompleteEmbyOpen(EmbyMovieParts parts,List<FileEntry> siblings,bool keepPlaylist,string uri,int generation,EmbyMovieExtras extras=null)
        {
            if(Current?.uri!=uri||Generation!=generation)return false;
            embyMovieParts=parts;embyMovieExtras=parts==null?extras:null;
            // Every Emby open replaces the movie-list queue, including a one-item ordinary movie.
            playlist=siblings.Where(x=>x.uri==uri||parts?.IndexOfUri(x.uri)>=0||extras?.IndexOfUri(x.uri)>=0).ToList();
            Notify();return true;
        }

        private static FileEntry EmbyFile(EmbyAccount account,EmbyItem item,EmbyMediaSource source)
        {
            string path=source?.Path??"";string filename=path.Replace('\\','/').Split('/').LastOrDefault()??"";
            long modified=DateTimeOffset.TryParse(source?.DateModified??item.DateModified,out var date)?date.ToUnixTimeMilliseconds():0;
            return new FileEntry{name=item.Name,formatName=filename,uri=EmbyIdentity.Create(account.id,item.Id),size=source?.Size??0,modified=modified,metadataVerified=source?.Size>0&&modified>0};
        }

        private bool OpenEmbyContainingItem(string uri)=>OpenEmbyHistoryItem(uri,false);

        private bool OpenEmbyHistoryItem(string uri,bool play)
        {
            if(!EmbyIdentity.TryParse(uri,out string accountId,out string itemId))return false;
            bool fromRecent=Source==FileSource.Recent;
            if(fromRecent)RememberBrowserLocation();
            CancelListing();StopDiscovery();embyRecentReturn=fromRecent;
            var account=EmbyAccounts.FirstOrDefault(x=>x.id==accountId);
            if(account==null||string.IsNullOrEmpty(account.accessToken)){ShowEmbyAccounts();SetStatus("请登录此播放记录对应的 Emby 账户");return true;}
            Source=FileSource.Emby;ActiveEmbyAccount=account;embyPages.Clear();
            var record=Store.Find(uri);
            UI?.PrepareDirectoryNavigation();
            CancellationToken playbackToken=default;
            if(play){embyOpening=new CancellationTokenSource();playbackToken=embyOpening.Token;}
            _ = LoadEmbyDetails(new EmbyItem{Id=itemId,Name=record?.name??"影片"},record?.directory??"",play?itemId:null,playbackToken);return true;
        }

        public bool ReturnToRecentFromEmby()
        {
            if(Source!=FileSource.Emby||!embyRecentReturn)return false;
            CancelEmbyBrowsing();Source=FileSource.Recent;return BrowseBack();
        }

        private static EmbyAccount CopyEmbyAccount(EmbyAccount account)=>new EmbyAccount{
            id=account.id,name=account.name,serverUrl=account.serverUrl,serverId=account.serverId,userId=account.userId,username=account.username,accessToken=account.accessToken,remember=account.remember
        };
        private void CancelEmbyPlaybackRequest(){embyOpening?.Cancel();embyOpening?.Dispose();embyOpening=null;}
        private void BeginEmbyPlayback(EmbyPlayback playback)
        {
            embyPlayback=playback;
            EmbyIdentity.TryParse(Current.uri,out string accountId,out _);
            embyPlaybackAccount=CopyEmbyAccount(EmbyAccounts.First(a=>a.id==accountId));
            embyPlaybackGeneration=Generation;embyReportedStart=false;embyLastPosition=playback.startPositionMs;embyReportAt=0;
        }
        private void TickEmbyPlayback(MediaSnapshot state)
        {
            if(embyPlayback==null||state.generation!=embyPlaybackGeneration||Generation!=embyPlaybackGeneration)return;
            if(state.state==3&&!state.HasError){
                embyLastPosition=state.position;
                if(!embyReportedStart){embyReportedStart=true;QueueEmbyReport("start",state.position,!state.playing);}
                else if(state.playing!=embyWasPlaying||Time.unscaledTime>=embyReportAt)QueueEmbyReport("progress",state.position,!state.playing);
                embyWasPlaying=state.playing;
            }
            if(state.state==4&&!Store.Settings.loop||state.HasError)EndEmbyPlayback();
        }
        private void QueueEmbyReport(string kind,long position,bool paused)
        {
            if(embyPlayback==null)return;
            if(kind=="progress"&&embyPendingReports>=2)return;
            embyReportAt=Time.unscaledTime+10;
            embyPendingReports++;
            embyReports=ReportEmbyAfter(embyReports,embyPlaybackAccount,embyPlayback,kind,Math.Max(0,position),paused);
        }
        private async Task ReportEmbyAfter(Task previous,EmbyAccount account,EmbyPlayback playback,string kind,long position,bool paused)
        {
            try{await previous;await EmbyApi.ReportPlayback(account,playback,kind,position,paused,CancellationToken.None);}
            catch(Exception){if(!embyDisposed&&embyPlayback==playback)SetStatus("Emby 播放进度同步失败，将在下次更新时重试");}
            finally{embyPendingReports--;}
        }
        private void EndEmbyPlayback()
        {
            if(embyPlayback==null)return;
            var state=Backend?.Snapshot;
            long position=state?.generation==embyPlaybackGeneration?state.position:embyLastPosition;
            QueueEmbyReport("stop",position,true);embyPlayback=null;embyPlaybackAccount=null;
        }
        private void DisposeEmby(){CancelEmbyBrowsing();EndEmbyPlayback();embyDisposed=true;}
    }
}
