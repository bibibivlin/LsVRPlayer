using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRPlayer
{
    public enum FormatDetectionMode { Off, Metadata, Enhanced }
    public enum FormatEvidence { None=0, Geometry=1, Frames=2, DirectoryPreference=3, Filename=4, Metadata=5 }
    public enum FormatFieldStatus { Unknown, Candidate, Confirmed, Unsupported }
    [Serializable] public sealed class MediaFormatHints
    {
        // -1 means unknown, including bool-valued hints. Existing renderer enums keep their values.
        public int projection=-1,stereo=-1,half=-1,swapEyes=-1,blockedFields;
        public int[] projectionCandidates=Array.Empty<int>(),stereoCandidates=Array.Empty<int>(),halfCandidates=Array.Empty<int>();
        public FormatEvidence source;
        public string reason="",unsupported="";
        public float score,margin;
        public int consistentFrames;
        public int Get(int field)=>field==0?projection:field==1?stereo:field==2?half:swapEyes;
        public void Set(int field,int value){if(field==0)projection=value;else if(field==1)stereo=value;else if(field==2)half=value;else swapEyes=value;}
        public bool HasValues=>projection>=0||stereo>=0||half>=0||swapEyes>=0;
        public MediaFormatHints Copy()=>new MediaFormatHints{projection=projection,stereo=stereo,half=half,swapEyes=swapEyes,blockedFields=blockedFields,source=source,reason=reason,unsupported=unsupported,score=score,margin=margin,consistentFrames=consistentFrames,projectionCandidates=projectionCandidates?.ToArray()??Array.Empty<int>(),stereoCandidates=stereoCandidates?.ToArray()??Array.Empty<int>(),halfCandidates=halfCandidates?.ToArray()??Array.Empty<int>()};
    }
    [Serializable] public sealed class FormatDetectionResult
    {
        public MediaFormatHints selected=new MediaFormatHints(),suggestion;
        public MediaFormatHints[] candidates=Array.Empty<MediaFormatHints>();
        public FormatEvidence[] sources=new FormatEvidence[4];
        public string explanation="";
        public FormatFieldStatus StereoStatus=>Status(1);
        public FormatFieldStatus ProjectionStatus=>Status(0);
        private FormatFieldStatus Status(int field)=>(selected.blockedFields&(1<<field))!=0?FormatFieldStatus.Unsupported:selected.Get(field)>=0?FormatFieldStatus.Confirmed:suggestion?.Get(field)>=0?FormatFieldStatus.Candidate:FormatFieldStatus.Unknown;
        public void Apply(ViewSettings view)
        {
            if(selected.projection>=0)view.projection=(Projection)selected.projection;
            if(selected.stereo>=0)view.stereo=(StereoLayout)selected.stereo;
            if(selected.half>=0)view.half=selected.half==1;
            if(selected.swapEyes>=0)view.swapEyes=selected.swapEyes==1;
        }
    }
    [Serializable] public sealed class FormatDetectionCache
    {
        public int algorithm,profileVersion;
        public string uri;
        public long size,modified;
        public MediaFormatHints metadata,frames;
        public MediaFormatHints projectionFrames;
        public bool Matches(FileEntry file)=>algorithm==FormatDetector.AlgorithmVersion&&file!=null&&file.metadataVerified&&size>0&&modified>0&&file.size==size&&file.modified==modified&&file.uri==uri;
    }
    public static class FormatDetector
    {
        public const int AlgorithmVersion=2;
        public static MediaFormatHints Filename(string name)
        {
            string stem=Path.GetFileNameWithoutExtension(name??"").ToLowerInvariant();
            bool Has(string token)=>Regex.IsMatch(stem,@"(?:^|[\s._\-\[\(])(?:"+token+@")(?:$|[\s._\-\]\)])");
            var hints=new MediaFormatHints{source=FormatEvidence.Filename,score=1,reason="文件名标签"};
            var projections=new List<int>();
            if(Has("2d|flat"))projections.Add(0);
            if(Has("180|vr180"))projections.Add(1);
            if(Has("360|vr360"))projections.Add(2);
            if(Has("fisheye|鱼眼"))projections.Add(3);
            if(Has("cubemap(?:[._ -]?3x2)?"))projections.Add(4);
            if(Has("eac(?:[._ -]?3x2)?"))projections.Add(5);
            if(projections.Count==1)hints.projection=projections[0];else if(projections.Count>1){hints.blockedFields|=1;hints.projectionCandidates=projections.ToArray();hints.reason+=" · 投影标签冲突："+string.Join(" / ",projections.Select(value=>Describe(new MediaFormatHints{projection=value})));}
            bool sbs=Has("sbs|lr|hsbs|fsbs"),tb=Has("ou|tb|hou|htb|fou|ftb"),mono=Has("mono|2d");
            if((sbs?1:0)+(tb?1:0)+(mono?1:0)>1){hints.blockedFields|=2;hints.stereoCandidates=new[]{mono?0:-1,sbs?1:-1,tb?2:-1}.Where(value=>value>=0).ToArray();hints.reason+=" · 立体标签冲突："+string.Join(" / ",hints.stereoCandidates.Select(value=>Describe(new MediaFormatHints{stereo=value})));}
            else if(sbs)hints.stereo=1;else if(tb)hints.stereo=2;else if(mono)hints.stereo=0;
            bool full=Has("fsbs|fou|ftb"),half=Has("hsbs|hou|htb");
            if(full&&half){hints.blockedFields|=4;hints.halfCandidates=new[]{0,1};hints.reason+=" · Half/Full 冲突";}else if(full)hints.half=0;else if(half)hints.half=1;
            if(Has("rl|right[-_]first"))hints.swapEyes=1;
            return hints;
        }
        public static FormatDetectionResult Resolve(params MediaFormatHints[] inputs)
        {
            var hints=inputs.Where(x=>x!=null).OrderByDescending(x=>x.source).ToArray();var result=new FormatDetectionResult{candidates=hints.Select(x=>x.Copy()).ToArray()};
            for(int field=0;field<4;field++){
                var available=hints.Where(x=>x.Get(field)>=0||(x.blockedFields&(1<<field))!=0).ToArray();if(available.Length==0)continue;
                var strongest=available.Where(x=>x.source==available[0].source).ToArray();
                if(strongest.Any(x=>(x.blockedFields&(1<<field))!=0)||strongest.Select(x=>x.Get(field)).Distinct().Count()>1){result.selected.blockedFields|=1<<field;continue;}
                int value=strongest[0].Get(field);
                if(value>(field==0?5:field==1?2:1))continue;
                result.selected.Set(field,value);result.sources[field]=strongest[0].source;
            }
            result.explanation=string.Join("；",hints.Where(x=>x.HasValues||x.blockedFields!=0||!string.IsNullOrEmpty(x.unsupported)).Select(x=>x.reason+(string.IsNullOrEmpty(x.unsupported)?"":" · "+x.unsupported)).Where(x=>!string.IsNullOrEmpty(x)).Distinct());
            if(result.selected.blockedFields!=0)result.explanation+="；存在冲突或不支持的格式，请手动选择";
            return result;
        }
        public static MediaFormatHints Packing(int width,int height,int stereo,int projection)
        {
            var result=new MediaFormatHints{source=FormatEvidence.Geometry,reason="分眼后显示比例"};
            if(width<=0||height<=0||stereo<=0||projection>0)return result;
            float aspect=width/(float)height;
            if(stereo==1){if(aspect>2.6f)result.half=0;else if(aspect>=1.3f&&aspect<=2.5f)result.half=1;}
            else if(stereo==2){if(aspect>=.6f&&aspect<=1.2f)result.half=0;else if(aspect>=1.3f&&aspect<=2.5f)result.half=1;}
            return result;
        }
        public static bool MeetsFrameThreshold(MediaFormatHints frames)=>frames!=null&&frames.source==FormatEvidence.Frames&&frames.score>=.9f&&frames.margin>=.15f&&frames.consistentFrames>=3;
        public static MediaFormatHints StreamHints(MediaSnapshot snapshot)
        {
            var hints=new MediaFormatHints{source=FormatEvidence.Metadata,reason="播放轨道元数据"};
            if(snapshot.stereoMode==1)hints.stereo=2;else if(snapshot.stereoMode==2)hints.stereo=1;
            // Mono defaults in decoder tracks do not prove that the container explicitly declared Mono.
            else if(snapshot.stereoMode==0&&snapshot.formatHints?.stereo==0)hints.stereo=0;
            else if(snapshot.stereoMode>2){hints.blockedFields=3;hints.unsupported="网格／多视图立体排列暂不支持";}
            return hints;
        }
        public static string Describe(MediaFormatHints hints)
        {
            if(hints==null||!hints.HasValues)return "无法确定";
            var parts=new List<string>();if(hints.projection>=0&&hints.projection<=5)parts.Add(new[]{"平面","VR180","VR360","鱼眼","Cubemap 3×2","EAC 3×2"}[hints.projection]);
            if(hints.stereo>=0&&hints.stereo<=2)parts.Add(new[]{"2D","左右 SBS","上下 OU"}[hints.stereo]);if(hints.half>=0)parts.Add(hints.half==1?"Half":"Full");if(hints.swapEyes==1)parts.Add("交换眼序");return string.Join(" / ",parts);
        }
    }
}
