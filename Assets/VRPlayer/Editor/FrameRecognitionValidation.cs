using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace VRPlayer.Editor
{
    public static class FrameRecognitionValidation
    {
        [Serializable] private sealed class Manifest {public int width,height;public Sample[] samples;}
        [Serializable] private sealed class Sample {public string name;public int stereo;public string[] frames;}
        [Serializable] private sealed class Row {public string name;public int expected,predicted;public float score,margin;public bool applied,correct;}
        [Serializable] private sealed class Report {public int algorithm,total,applied,correct,falsePositives,positives,recognized;public float precision,coverage,abstention;public bool profileEligible;public string dataset;public Row[] rows;}
        [MenuItem("VRPlayer/Validate Frame Recognition")]
        public static void Validate()
            =>ValidateDataset("TestMedia/Recognition","Docs/Validation/frame-recognition.json",false);
        public static void ValidateDataset(string root,string output,bool writeProfile)
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(root+"/manifest.json"));
            var rows=manifest.samples.Select(sample=>{
                var accumulator=new FrameEvidenceAccumulator();MediaFormatHints evidence=null;
                foreach(string frame in sample.frames)evidence=accumulator.Add(FrameFormatAnalyzer.Analyze(File.ReadAllBytes(Path.Combine(root,frame)),manifest.width,manifest.height,-1,CancellationToken.None))??evidence;
                bool applied=FormatDetector.MeetsFrameThreshold(evidence);int predicted=applied?evidence.stereo:0;
                return new Row{name=sample.name,expected=sample.stereo,predicted=predicted,score=evidence?.score??0,margin=evidence?.margin??0,applied=applied,correct=predicted==sample.stereo};
            }).ToArray();
            var report=new Report{algorithm=FormatDetector.AlgorithmVersion,total=rows.Length,applied=rows.Count(x=>x.applied),correct=rows.Count(x=>x.applied&&x.correct),falsePositives=rows.Count(x=>x.applied&&!x.correct),positives=rows.Count(x=>x.expected>0),recognized=rows.Count(x=>x.applied&&x.correct),rows=rows,dataset="Independent generated textured surfaces; not a real-film compatibility claim"};
            report.precision=report.applied==0?0:report.correct/(float)report.applied;report.coverage=report.positives==0?0:report.recognized/(float)report.positives;report.abstention=1-report.applied/(float)report.total;
            report.profileEligible=report.applied>=50&&report.precision>=.98f&&report.coverage>=.8f;
            report.dataset="Procedural texture set: "+Path.GetFileName(root)+"; not a real-film compatibility claim";
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(writeProfile){
                var profile=new FrameRecognitionProfile{algorithm=report.algorithm,version=3,stereoAutoApproved=report.profileEligible,evaluated=report.applied,correct=report.correct};
                File.WriteAllText("Assets/VRPlayer/Resources/FormatRecognitionProfile.json",JsonUtility.ToJson(profile,true));AssetDatabase.ImportAsset("Assets/VRPlayer/Resources/FormatRecognitionProfile.json");
            }
            Debug.Log($"Frame recognition: {report.correct}/{report.applied} auto selections correct; {report.recognized}/{report.positives} stereo samples recognized; approved={report.profileEligible}");
        }
    }
}
