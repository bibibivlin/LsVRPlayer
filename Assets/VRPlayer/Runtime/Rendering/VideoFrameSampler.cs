using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRPlayer
{
    [Serializable] public sealed class FrameRecognitionProfile
    {
        public int algorithm,version=2;
        public bool stereoAutoApproved,vr180AutoApproved;
        public int evaluated,correct,vr180Evaluated,vr180Correct,realPositiveVideos,realNegativeVideos;
        public bool Approved=>algorithm==FormatDetector.AlgorithmVersion&&stereoAutoApproved&&evaluated>=50&&correct/(float)evaluated>=.98f;
        public bool Vr180Approved=>Approved&&vr180AutoApproved&&realPositiveVideos>=200&&realNegativeVideos>=200&&vr180Evaluated>=50&&vr180Correct/(float)vr180Evaluated>=.98f;
    }
    public sealed class FrameSamplingBudget
    {
        public float ActiveSeconds{get;private set;}
        public int Attempts{get;private set;}
        private float next;
        private int validInScene;
        public bool Expired=>ActiveSeconds>=60||Attempts>=20;
        public void Tick(float seconds,bool active){if(active)ActiveSeconds+=Math.Max(0,seconds);}
        public bool TrySample()
        {
            if(Expired||ActiveSeconds<next||ActiveSeconds<20&&Attempts>=12)return false;
            Attempts++;next=ActiveSeconds+(ActiveSeconds<20?1:5);return true;
        }
        public void RecordResult(bool valid,bool sceneChanged)
        {
            if(sceneChanged)validInScene=0;
            if(valid&&++validInScene<3&&ActiveSeconds>=20)next=Math.Min(next,ActiveSeconds+1);
        }
    }
    public sealed class VideoFrameSampler : MonoBehaviour
    {
        private CancellationTokenSource lifetime;
        private PlayerApp app;
        private int generation,revision;
        private bool busy;
        private byte[] previous,rgbaBuffer;
        private readonly FrameSamplingBudget budget=new FrameSamplingBudget();
        private readonly FrameEvidenceAccumulator accumulator=new FrameEvidenceAccumulator();
        public void Initialize(PlayerApp owner,int token){app=owner;generation=token;lifetime=new CancellationTokenSource();}
        public void Cancel(){revision++;lifetime?.Cancel();enabled=false;}
        private void Update()
        {
            if(app==null||lifetime.IsCancellationRequested||generation!=app.Generation)return;
            if(!app.NeedsFrameAnalysis){Cancel();return;}
            var state=app.Backend?.Snapshot;
            bool active=state!=null&&state.generation==generation&&state.playing&&app.HasFocus;
            budget.Tick(Time.unscaledDeltaTime,active);
            if(!active||busy)return;
            if(budget.Expired){app.FrameAnalysisUnavailable(app.AnalysisValidFrames<3?"有效画面不足，可在内容开始后重新识别":"本次采样结束；未确认的格式可采用建议或重新识别");Cancel();return;}
            if(state.width<64||state.height<64||app.Backend.Texture==null)return;
            if(!SystemInfo.supportsAsyncGPUReadback){app.FrameAnalysisUnavailable("设备不支持异步画面读取");Cancel();return;}
            if(!budget.TrySample())return;
            busy=true;int edge=budget.Attempts>16?768:512;
            float scale=Math.Min(1,edge/(float)Math.Max(state.width,state.height));int width=Math.Max(32,(int)(state.width*scale)/2*2),height=Math.Max(32,(int)(state.height*scale)/2*2);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var token=lifetime.Token;int requestRevision=revision,stereo=app.Detection.selected.stereo;
            try{
                if(rgbaBuffer==null||rgbaBuffer.Length!=width*height*4)rgbaBuffer=new byte[width*height*4];
                Graphics.Blit(app.Backend.Texture,target);
                AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32,request=>{
                    bool analyzing=false;var readTimer=System.Diagnostics.Stopwatch.StartNew();
                    try{
                        if(token.IsCancellationRequested||requestRevision!=revision||this==null||app==null||app.Generation!=generation)return;
                        if(request.hasError){app.FrameAnalysisUnavailable("GPU 画面回读失败");Cancel();return;}
                        request.GetData<byte>().CopyTo(rgbaBuffer);
                        analyzing=true;_ = Analyze(rgbaBuffer,width,height,stereo,token,requestRevision);
                    }finally{RenderTexture.ReleaseTemporary(target);if(!analyzing)busy=false;if(app!=null&&app.Generation==generation&&!token.IsCancellationRequested)app.RecordRecognitionTiming((float)readTimer.Elapsed.TotalMilliseconds);}
                });app.RecordRecognitionTiming((float)timer.Elapsed.TotalMilliseconds);
            }catch(Exception){RenderTexture.ReleaseTemporary(target);busy=false;app.FrameAnalysisUnavailable("画面提交失败");Cancel();}
        }
        private async Task Analyze(byte[] rgba,int width,int height,int stereo,CancellationToken token,int requestRevision)
        {
            try{
                byte[] gray=null;var previousFrame=previous;
                var result=await Task.Run(()=>{
                    gray=new byte[width*height];long difference=0;var histogram=new int[16];var previousHistogram=new int[16];
                    for(int i=0;i<gray.Length;i++){
                        if((i&4095)==0)token.ThrowIfCancellationRequested();int p=i*4;
                        gray[i]=(byte)((rgba[p]*77+rgba[p+1]*150+rgba[p+2]*29)>>8);histogram[gray[i]>>4]++;
                        if(previousFrame!=null&&previousFrame.Length==gray.Length){difference+=Math.Abs(gray[i]-previousFrame[i]);previousHistogram[previousFrame[i]>>4]++;}
                    }
                    if(previousFrame!=null&&previousFrame.Length==gray.Length&&difference<gray.Length)return null;
                    var analysis=FrameFormatAnalyzer.Analyze(gray,width,height,stereo,token);long histogramDifference=0;
                    for(int i=0;i<16;i++)histogramDifference+=Math.Abs(histogram[i]-previousHistogram[i]);
                    analysis.sceneChanged=previousFrame!=null&&previousFrame.Length==gray.Length&&difference>gray.Length*35&&histogramDifference>gray.Length*.45;
                    return analysis;
                },token);
                if(token.IsCancellationRequested||requestRevision!=revision||this==null||app==null||app.Generation!=generation)return;
                if(result==null){app.ReportFrameAnalysis(generation,budget.Attempts,null,"画面重复，等待新的有效内容");return;}
                if(result.valid)previous=gray;
                budget.RecordResult(result.valid,result.sceneChanged);
                app.ReportFrameAnalysis(generation,budget.Attempts,result,result.valid?$"采样 {budget.Attempts}/20 · 匹配 {result.matches} 区块 · "+result.reason:"画面过暗或纹理不足，等待内容");
                app.AcceptFrameEvidence(accumulator.Add(result),accumulator.ProjectionSuggestion());
            }catch(OperationCanceledException){}catch(Exception){if(!token.IsCancellationRequested&&app!=null&&app.Generation==generation)app.FrameAnalysisUnavailable("画面分析失败，保留元数据结果");}
            finally{busy=false;}
        }
        private void OnDestroy(){Cancel();lifetime?.Dispose();}
    }
}
