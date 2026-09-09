using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRPlayer
{
    public sealed class SubtitleCue { public double Start,End;public string Text; }
    public sealed class SrtDocument
    {
        public readonly List<SubtitleCue> Cues=new List<SubtitleCue>();
        public int InvalidBlocks{get;private set;}
        private double[] maximumEnd=Array.Empty<double>();
        private static readonly Regex Timing=new Regex(@"^\s*(\d+:\d{2}:\d{2}[,.]\d{1,3})\s*-->\s*(\d+:\d{2}:\d{2}[,.]\d{1,3})(?:\s+.*)?$",RegexOptions.Compiled);
        public static SrtDocument Parse(string input)
        {
            var result=new SrtDocument();
            foreach(string block in Regex.Split((input??"").TrimStart('\uFEFF').Replace("\r\n","\n").Replace('\r','\n'),@"\n[\t ]*\n"))
            {
                if(string.IsNullOrWhiteSpace(block))continue;
                string[] lines=block.Trim('\n').Split('\n');int timingIndex=lines.Length>0&&Timing.IsMatch(lines[0])?0:1;
                if(timingIndex>=lines.Length){result.InvalidBlocks++;continue;}
                Match match=Timing.Match(lines[timingIndex]);
                if(!match.Success||!TryTime(match.Groups[1].Value,out double start)||!TryTime(match.Groups[2].Value,out double end)||end<=start||timingIndex+1>=lines.Length){result.InvalidBlocks++;continue;}
                string text=string.Join("\n",lines.Skip(timingIndex+1)).Trim();
                // Only recognized SRT formatting is removed. TMP rich text is disabled in the display.
                text=Regex.Replace(text,@"</?(?:i|b|u|font)(?:\s[^>]*)?>","",RegexOptions.IgnoreCase);
                text=text.Replace("&lt;","<").Replace("&gt;",">").Replace("&amp;","&");
                result.Cues.Add(new SubtitleCue{Start=start,End=end,Text=text});
            }
            result.Cues.Sort((a,b)=>a.Start.CompareTo(b.Start));result.maximumEnd=new double[result.Cues.Count];
            double latest=0;for(int i=0;i<result.Cues.Count;i++)result.maximumEnd[i]=latest=Math.Max(latest,result.Cues[i].End);
            return result;
        }
        private static bool TryTime(string value,out double time)
        {
            time=0;string[] parts=value.Replace(',',':').Replace('.',':').Split(':');
            if(parts.Length!=4||!long.TryParse(parts[0],out long h)||!int.TryParse(parts[1],out int m)||!int.TryParse(parts[2],out int s)||m>59||s>59)return false;
            if(!double.TryParse("0."+parts[3],NumberStyles.Float,CultureInfo.InvariantCulture,out double fraction))return false;
            time=h*3600d+m*60+s+fraction;return true;
        }
        public string TextAt(double mediaSeconds,double delay=0)
        {
            double time=mediaSeconds-delay;int low=0,high=Cues.Count;
            while(low<high){int mid=(low+high)/2;if(Cues[mid].Start<=time)low=mid+1;else high=mid;}
            var active=new List<string>();for(int i=low-1;i>=0&&maximumEnd[i]>time;i--)if(Cues[i].End>time)active.Add(Cues[i].Text);
            active.Reverse();return string.Join("\n",active);
        }
    }
    public static class SubtitleMatcher
    {
        private const string ChineseTag=@"zh(?:[-_]?(?:cn|tw|hans|hant))?|chs|cht|chi|zho|简体中文|簡體中文|繁体中文|繁體中文|简体|簡體|繁体|繁體|简中|簡中|繁中|简英|簡英|繁英|中英双语|中英雙語|中英|中文";
        private const string Separator=@"[._\s\-\[\]()&+]";
        private static readonly Regex Suffix=new Regex(@"^(?:"+Separator+"+(?:"+ChineseTag+@"|eng|en|ja|jpn|ko|kor|fr|fra|de|deu|es|spa|forced|sdh|cc|default|bilingual|双语|雙語))+[\]\)\s]*$",RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex ChineseSuffix=new Regex(@"(?:^|"+Separator+")(?:"+ChineseTag+")(?:$|"+Separator+")",RegexOptions.IgnoreCase|RegexOptions.Compiled);
        public static bool Matches(string video,string subtitle)
        {
            if(!subtitle.EndsWith(".srt",StringComparison.OrdinalIgnoreCase))return false;
            string stem=Path.GetFileNameWithoutExtension(video),candidate=Path.GetFileNameWithoutExtension(subtitle);
            if(candidate.Equals(stem,StringComparison.OrdinalIgnoreCase))return true;
            return candidate.StartsWith(stem,StringComparison.OrdinalIgnoreCase)&&Suffix.IsMatch(candidate.Substring(stem.Length));
        }
        public static int Priority(string video,string subtitle)
        {
            string stem=Path.GetFileNameWithoutExtension(video),candidate=Path.GetFileNameWithoutExtension(subtitle);
            if(candidate.Equals(stem,StringComparison.OrdinalIgnoreCase))return 1;
            string suffix=candidate.Length>=stem.Length?candidate.Substring(stem.Length):"";
            return ChineseSuffix.IsMatch(suffix)?0:2;
        }
        public static List<FileEntry> Find(string video,IEnumerable<FileEntry> entries)=>entries.Where(e=>e!=null&&e.IsSubtitle&&Matches(video,e.name)).OrderBy(e=>Priority(video,e.name)).ThenBy(e=>e.name,StringComparer.OrdinalIgnoreCase).ThenBy(e=>e.uri,StringComparer.Ordinal).ToList();
    }
}
