using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VRPlayer
{
    public sealed partial class PlayerApp
    {
        private CancellationTokenSource embyPlayedChange;
        private bool embyMarkingPlayback;
        private int embyPlayedGeneration = -1;
        public bool EmbyPlayedBusy => embyPlayedChange != null;
        // An explicit mark wins over this playback's later check-ins and snapshots until a new open.
        private bool EmbyProgressSuppressed => embyPlayedGeneration == Generation;

        private void CancelEmbyPlayedChange() => embyPlayedChange?.Cancel();

        public async Task SetEmbyPlayed(EmbyItem item, bool played)
        {
            var account = ActiveEmbyAccount;
            var state = EmbyState;
            if (item == null || !item.IsPlayable || account == null || EmbyApi == null || EmbyPlayedBusy || state.loading || Source != FileSource.Emby) return;
            var request = new CancellationTokenSource();
            embyPlayedChange = request;
            var token = request.Token;
            var captured = CopyEmbyAccount(account);
            state.playedItemId = item.Id; state.playedError = ""; PublishEmby(state);
            TaskCompletionSource<bool> drained = null;
            bool changedLocal = false;
            int completed = 0, playbackGeneration = Generation;
            try
            {
                var targets = await EmbyApi.GetPlayedStateItems(captured, item.Id, token);
                token.ThrowIfCancellationRequested();
                bool current = targets.Any(target => Current?.uri == EmbyIdentity.Create(captured.id, target.Id));
                if (current && embyPlayback != null && embyPlaybackAccount?.id == captured.id)
                {
                    var snapshot = Backend?.Snapshot;
                    QueueEmbyReport("stop", snapshot?.generation == Generation ? snapshot.position : embyLastPosition, true);
                    embyMarkingPlayback = true; embyReportedStart = false;
                }
                // New opens and details loads must wait for the mutation, including the final old-session stop.
                var previous = embyReports;
                drained = new TaskCompletionSource<bool>(); embyReports = drained.Task;
                await previous; token.ThrowIfCancellationRequested();
                // Commit the main film last so a partial failure cannot claim that every CD was marked.
                foreach (var target in targets.Skip(1).Concat(targets.Take(1)))
                {
                    var data = await EmbyApi.SetPlayed(captured, target.Id, played, token);
                    token.ThrowIfCancellationRequested();
                    ApplyEmbyPlayedData(captured.id, target.Id, data);
                    if (Current?.uri == EmbyIdentity.Create(captured.id, target.Id) && Generation == playbackGeneration)
                        embyPlayedGeneration = played ? Generation : -1;
                    changedLocal = true; completed++;
                }
                if (!token.IsCancellationRequested && ActiveEmbyAccount == account && EmbyState == state && Source == FileSource.Emby)
                    Status = played ? "已标记为已播放" + (targets.Length > 1 ? " · 包含全部 CD" : "") : "已标记为未播放";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !embyDisposed && ActiveEmbyAccount == account && EmbyState == state && Source == FileSource.Emby)
                {
                    if (ex is EmbyAuthenticationException) FailEmby(token, ex, account);
                    else state.playedError = (completed > 0 ? "部分 CD 已更新，请重试以完成整部影片。" : "") + EmbyClient.SafeError(ex);
                }
            }
            finally
            {
                if (changedLocal)
                {
                    try { Store?.Save(); }
                    catch (Exception) { state.playedError = "服务器播放状态已更新，本地进度保存失败，请重试"; }
                }
                embyMarkingPlayback = false;
                drained?.TrySetResult(true);
                state.playedItemId = ""; embyPlayedChange = null; request.Dispose();
                if (!embyDisposed && ActiveEmbyAccount == account && EmbyState == state && Source == FileSource.Emby)
                {
                    // Keep the success/error text with the page without replacing its cards or scroll memory.
                    string status = Status; PublishEmby(state); Status = string.IsNullOrEmpty(state.playedError) ? status : state.playedError;
                }
            }
        }

        private void ApplyEmbyPlayedData(string accountId, string itemId, EmbyUserData data)
        {
            if (ActiveEmbyAccount?.id == accountId)
            {
                foreach (var page in embyPages.Concat(new[] { EmbyState }).Distinct())
                {
                    foreach (var entry in page.views.Concat(page.resume).Concat(page.latest).Concat(page.items).Concat(page.specialEpisodes).Concat(page.parts).Concat(page.extras).Concat(new[] { page.selected }).Where(entry => entry?.Id == itemId))
                        entry.UserData = data;
                    // The server clears resume position for both watched and unwatched actions.
                    page.resume = page.resume.Where(entry => entry?.Id != itemId).ToArray();
                }
            }
            foreach (var entry in (embyMovieParts?.AccountId == accountId ? embyMovieParts.Items : Array.Empty<EmbyItem>()).Where(entry => entry.Id == itemId)) entry.UserData = data;
            string uri = EmbyIdentity.Create(accountId, itemId);
            var record = Store?.Find(uri);
            if (record != null) { record.position = 0; record.lastPlayed = data.LastPlayedDate ?? ""; }
        }
    }
}
