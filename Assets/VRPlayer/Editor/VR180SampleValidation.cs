using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace VRPlayer.Editor
{
    public static class VR180SampleValidation
    {
        [Serializable] private sealed class Manifest {public Sample[] samples;}
        [Serializable] private sealed class Sample {public string name;public int width,height,stereo,projection;public string[] frames;}
        [Serializable] private sealed class Row {public string name;public int expectedStereo,expectedProjection;public bool stereoRecognized,projectionRecognized,wrongStereo,wrongProjection;public FrameAnalysis[] frames;public float milliseconds;}
        [Serializable] private sealed class Report {public int algorithm,total,stereoRecognized,projectionRecognized,wrongStereo,wrongProjection;public Row[] rows;}
        public static string Validate(string root,string output)
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(root,"manifest.json")));var rows=new List<Row>();
            var report=new Report{algorithm=FormatDetector.AlgorithmVersion,total=manifest.samples.Length};
            foreach(var sample in manifest.samples){
                var frames=new List<FrameAnalysis>();var accumulator=new FrameEvidenceAccumulator();var timer=System.Diagnostics.Stopwatch.StartNew();
                bool stereo=false,projection=false,wrongStereo=false,wrongProjection=false;
                foreach(string filename in sample.frames){
                    var analysis=FrameFormatAnalyzer.Analyze(File.ReadAllBytes(Path.Combine(root,filename)),sample.width,sample.height,-1,CancellationToken.None);frames.Add(analysis);
                    var evidence=accumulator.Add(analysis);var candidate=accumulator.ProjectionSuggestion();
                    stereo|=FormatDetector.MeetsFrameThreshold(evidence)&&evidence.stereo==sample.stereo;
                    projection|=FormatDetector.MeetsFrameThreshold(candidate)&&candidate.projection==sample.projection;
                    wrongStereo|=FormatDetector.MeetsFrameThreshold(evidence)&&evidence.stereo!=sample.stereo;
                    wrongProjection|=FormatDetector.MeetsFrameThreshold(candidate)&&candidate.projection!=sample.projection;
                }
                if(stereo)report.stereoRecognized++;if(projection)report.projectionRecognized++;
                if(wrongStereo)report.wrongStereo++;if(wrongProjection)report.wrongProjection++;
                rows.Add(new Row{name=sample.name,expectedStereo=sample.stereo,expectedProjection=sample.projection,stereoRecognized=stereo,projectionRecognized=projection,wrongStereo=wrongStereo,wrongProjection=wrongProjection,frames=frames.ToArray(),milliseconds=(float)timer.Elapsed.TotalMilliseconds});
            }
            report.rows=rows.ToArray();Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            return $"{report.total} clips: SBS {report.stereoRecognized}, VR180 {report.projectionRecognized}. Scores only; no validation gate changed.";
        }
    }
}
