using System;
using System.Linq;
using UnityEngine;

namespace VRPlayer
{
    public sealed partial class PlayerApp
    {
        public FormatDetectionResult Detection{get;private set;}=new FormatDetectionResult();
        public string FormatDescription{get;private set;}="打开影片后识别格式";
        private MediaFormatHints filenameHints,metadataHints,streamHints,frameHints,projectionFrameHints,directoryHints;
        private VideoFrameSampler frameSampler;
        private FrameRecognitionProfile recognitionProfile;
        private ViewSettings beforeDetection;
        private FileEntry detectionFile;
        private bool formatActive;
        public string AnalysisStatus{get;private set;}="";
        public FrameAnalysis LastFrameAnalysis{get;private set;}
        public int AnalysisAttempts{get;private set;}
        public int AnalysisValidFrames{get;private set;}
        public bool NeedsFrameAnalysis=>formatActive&&Current!=null&&!Current.formatLocked&&Detection.selected.stereo!=0&&
            ((Detection.selected.stereo<0&&(Detection.selected.blockedFields&2)==0)||(Detection.selected.projection<0&&(Detection.selected.blockedFields&1)==0));
        public bool CurrentDirectoryPrefersVr180=>Current!=null&&!string.IsNullOrEmpty(Current.directory)&&Store.Settings.vr180Directories.Contains(Current.directory);
        private string nativeHintsKey="";
        private readonly System.Collections.Generic.List<float> recognitionTimings=new System.Collections.Generic.List<float>(128);
        public float RecognitionMainThreadP95=>recognitionTimings.Count==0?0:recognitionTimings.OrderBy(x=>x).ElementAt(Math.Min(recognitionTimings.Count-1,(int)(recognitionTimings.Count*.95f)));
        public void RecordRecognitionTiming(float milliseconds){if(recognitionTimings.Count<128)recognitionTimings.Add(milliseconds);}
        private void BeginFormatDetection(FileEntry entry,bool force=false)
        {
            CancelFormatDetection();recognitionTimings.Clear();detectionFile=entry;nativeHintsKey="";
            AnalysisAttempts=AnalysisValidFrames=0;LastFrameAnalysis=null;AnalysisStatus="等待有效播放画面";
            var profile=Resources.Load<TextAsset>("FormatRecognitionProfile");recognitionProfile=profile==null?new FrameRecognitionProfile():JsonUtility.FromJson<FrameRecognitionProfile>(profile.text);
            beforeDetection=Presenter.Settings.Copy();
            Detection=new FormatDetectionResult();filenameHints=metadataHints=streamHints=frameHints=projectionFrameHints=directoryHints=null;
            formatActive=!Current.formatLocked&&(force||Store.Settings.formatDetection!=FormatDetectionMode.Off);
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Api.CallStatic("setFormatProbeEnabled",formatActive);
#endif
            if(!formatActive){AnalysisStatus=Current.formatLocked?(Current.formatLockOrigin==FormatLockOrigin.Inherited?"继承格式，未运行自动识别":Current.formatLockOrigin==FormatLockOrigin.Manual?"手动格式，未运行自动识别":"已保存格式，未运行自动识别"):"自动识别已关闭";FormatDescription=AnalysisStatus+"；可重新识别格式";return;}
            // Reset only source-format fields; preserve the user's viewing and subtitle settings.
            var view=Presenter.Settings;view.projection=Projection.Flat;view.stereo=StereoLayout.Mono;view.half=true;view.swapEyes=false;
            filenameHints=FormatDetector.Filename(entry.name);
            if(!force&&Current.formatCache?.Matches(entry)==true){
                var cache=Current.formatCache;metadataHints=cache.metadata;
                if(cache.profileVersion==recognitionProfile.version){if(recognitionProfile.Approved)frameHints=cache.frames;if(recognitionProfile.Vr180Approved)projectionFrameHints=cache.projectionFrames;}
            }
            ResolveFormat();
            if((Store.Settings.formatDetection==FormatDetectionMode.Enhanced||force&&Store.Settings.formatDetection==FormatDetectionMode.Off)&&NeedsFrameAnalysis){
                frameSampler=gameObject.AddComponent<VideoFrameSampler>();frameSampler.Initialize(this,Generation);
            }else{AnalysisStatus=NeedsFrameAnalysis?"当前仅使用标签与元数据":"格式字段已确认";RefreshFormatDescription();}
        }
        private void CancelFormatDetection(){formatActive=false;if(frameSampler!=null){frameSampler.Cancel();Destroy(frameSampler);frameSampler=null;}}
        private void TickFormatDetection(MediaSnapshot snapshot)
        {
            if(!formatActive||Current==null||Current.formatLocked||snapshot.generation!=Generation)return;
            var hints=snapshot.formatHints;
            string key=snapshot.stereoMode+"/"+snapshot.formatProbeComplete+"/"+snapshot.width+"/"+snapshot.height+"|"+(hints==null?"":$"{hints.projection}/{hints.stereo}/{hints.half}/{hints.swapEyes}/{hints.blockedFields}/{hints.unsupported}/{hints.reason}");
            if(key==nativeHintsKey)return;nativeHintsKey=key;var timer=System.Diagnostics.Stopwatch.StartNew();
            if(hints!=null&&(hints.HasValues||hints.blockedFields!=0||snapshot.formatProbeComplete)){metadataHints=hints.Copy();metadataHints.source=FormatEvidence.Metadata;}
            streamHints=FormatDetector.StreamHints(snapshot);
            ResolveFormat();RecordRecognitionTiming((float)timer.Elapsed.TotalMilliseconds);
        }
        private void ResolveFormat()
        {
            var suggestion=Detection.suggestion;
            Detection=FormatDetector.Resolve(metadataHints,streamHints,filenameHints,frameHints,projectionFrameHints);
            directoryHints=CurrentDirectoryPrefersVr180&&Detection.selected.stereo==1?new MediaFormatHints{source=FormatEvidence.DirectoryPreference,projection=1,reason="目录偏好＋已确认左右分眼"}:null;
            Detection=FormatDetector.Resolve(metadataHints,streamHints,filenameHints,directoryHints,frameHints,projectionFrameHints);
            var state=Backend?.Snapshot;var packing=FormatDetector.Packing(state?.width??0,state?.height??0,Detection.selected.stereo,Detection.selected.projection);
            Detection=FormatDetector.Resolve(metadataHints,streamHints,filenameHints,directoryHints,frameHints,projectionFrameHints,packing);Detection.suggestion=suggestion;
            // Recompute each automatic field so a newly discovered conflict removes earlier guesses.
            var view=Presenter.Settings;view.projection=Projection.Flat;view.stereo=StereoLayout.Mono;view.half=true;view.swapEyes=false;Detection.Apply(view);
            RefreshFormatDescription();CacheFormat();
        }
        private void RefreshFormatDescription()
        {
            string result=Detection.selected.HasValues?FormatDetector.Describe(Detection.selected):"暂未确定，使用平面 2D";
            FormatDescription=result+(string.IsNullOrEmpty(Detection.explanation)?"":" · "+Detection.explanation);
            if(Detection.suggestion?.HasValues==true)FormatDescription+="\n建议："+FormatDetector.Describe(Detection.suggestion)+$"（评分 {Detection.suggestion.score:0.00}）";
            if(!string.IsNullOrEmpty(AnalysisStatus))FormatDescription+="\n"+AnalysisStatus;
        }
        private void CacheFormat()
        {
            if(Current==null||detectionFile==null||!detectionFile.metadataVerified||detectionFile.size<=0||detectionFile.modified<=0)return;
            var merged=FormatDetector.Resolve(metadataHints,streamHints).selected;merged.source=FormatEvidence.Metadata;merged.reason="已验证文件信息的元数据缓存";
            Current.formatCache=new FormatDetectionCache{algorithm=FormatDetector.AlgorithmVersion,profileVersion=recognitionProfile.version,uri=detectionFile.uri,size=detectionFile.size,modified=detectionFile.modified,metadata=merged,frames=frameHints,projectionFrames=projectionFrameHints};
        }
        public void AcceptFrameEvidence(MediaFormatHints frames,MediaFormatHints projection)
        {
            if(!formatActive||Current==null||Current.formatLocked)return;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            MediaFormatHints suggestion=frames?.HasValues==true?frames.Copy():new MediaFormatHints{source=FormatEvidence.Frames};
            if(projection?.HasValues==true){suggestion.projection=projection.projection;suggestion.score=frames?.HasValues==true?Math.Min(frames.score,projection.score):projection.score;suggestion.reason+=" · "+projection.reason;}
            Detection.suggestion=suggestion.HasValues?suggestion:null;
            bool changed=false;
            if(frameHints==null&&recognitionProfile?.Approved==true&&frames?.stereo>0&&FormatDetector.MeetsFrameThreshold(frames)){frameHints=frames.Copy();frameHints.projection=-1;changed=true;}
            if(projectionFrameHints==null&&recognitionProfile?.Vr180Approved==true&&projection?.projection==1&&projection.stereo==1&&FormatDetector.MeetsFrameThreshold(projection)){
                projectionFrameHints=projection.Copy();projectionFrameHints.stereo=-1;changed=true;
            }
            if(changed)ResolveFormat();
            if(projection?.projection==1&&recognitionProfile?.Vr180Approved!=true)AnalysisStatus="VR180 画面候选尚待真实影片验证，可采用建议";
            else if(frameHints!=null&&NeedsFrameAnalysis)AnalysisStatus=frameHints.stereo==1?"已确认左右分眼，继续判断投影":"已确认上下分眼，继续判断投影";
            else if(!NeedsFrameAnalysis)AnalysisStatus="格式识别完成";
            RefreshFormatDescription();
            RecordRecognitionTiming((float)timer.Elapsed.TotalMilliseconds);
        }
        public void ReportFrameAnalysis(int generation,int attempts,FrameAnalysis frame,string status)
        {
            if(generation!=Generation||!formatActive||Current==null||Current.formatLocked)return;
            AnalysisAttempts=attempts;if(frame!=null){LastFrameAnalysis=frame;if(frame.valid)AnalysisValidFrames++;}
            AnalysisStatus=status;RefreshFormatDescription();
        }
        public void FrameAnalysisUnavailable(string reason){AnalysisStatus=reason;RefreshFormatDescription();}
        public void SetDirectoryVr180Preference(bool enabled)
        {
            if(Current==null||string.IsNullOrEmpty(Current.directory))return;
            Store.Settings.vr180Directories.Remove(Current.directory);if(enabled)Store.Settings.vr180Directories.Add(Current.directory);Store.Save();
            if(formatActive&&!Current.formatLocked){
                ResolveFormat();
                if(NeedsFrameAnalysis&&Store.Settings.formatDetection==FormatDetectionMode.Enhanced&&(frameSampler==null||!frameSampler.enabled)){
                    if(frameSampler!=null)Destroy(frameSampler);
                    frameSampler=gameObject.AddComponent<VideoFrameSampler>();frameSampler.Initialize(this,Generation);
                }
            }
            Notify();
        }
        public void ChangeFormat(Action<ViewSettings> change)
        {
            if(Current==null){change(Presenter.Settings);return;}
            CancelFormatDetection();beforeDetection=null;change(Presenter.Settings);Current.formatLocked=true;Current.formatLockOrigin=FormatLockOrigin.Manual;Current.formatCache=null;AnalysisStatus="手动格式，未运行自动识别";FormatDescription="已保存手动格式 · "+Presenter.Settings.projection+" / "+Presenter.Settings.stereo;Store.Save();Notify();
        }
        public void SetFormatDetectionMode(FormatDetectionMode mode)
        {
            Store.Settings.formatDetection=mode;Store.Save();
            if(Current!=null&&!Current.formatLocked)BeginFormatDetection(new FileEntry{uri=Current.uri,name=Current.name,size=Current.size,modified=Current.modified});
            else if(mode==FormatDetectionMode.Off)CancelFormatDetection();Notify();
        }
        public void RedetectFormat()
        {
            if(Current==null)return;
            Current.formatLocked=false;Current.formatCache=null;
            BeginFormatDetection(new FileEntry{uri=Current.uri,name=Current.name,size=Current.size,modified=Current.modified},true);
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Api.CallStatic("probeCurrentFormat");
#endif
            Store.Save();Notify();
        }
        public void UndoFormatDetection()
        {
            if(Current==null||beforeDetection==null)return;var original=beforeDetection;
            ChangeFormat(view=>{view.projection=original.projection;view.stereo=original.stereo;view.half=original.half;view.swapEyes=original.swapEyes;});
            // An explicit undo is a user override even if the original was automatically chosen.
            beforeDetection=null;FormatDescription="已撤销识别并记住该片格式";
        }
        public void ApplyFormatSuggestion()
        {
            var hints=Detection.suggestion;if(hints?.HasValues!=true){SetStatus("暂无可采用的画面建议");return;}
            ChangeFormat(view=>new FormatDetectionResult{selected=hints}.Apply(view));
        }
    }
}
