using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace VRPlayer.Editor
{
    public static class DeviceValidationBuild
    {
        [MenuItem("VRPlayer/Validation/Build Thumbnail Test APK")]
        public static void QueueThumbnails()=>EditorApplication.delayCall+=BuildThumbnails;

        // Side-by-side installation keeps release signatures, accounts and settings intact.
        public static void BuildThumbnails()
        {
            PlayerBuild.Configure();
            string identifier=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),name=PlayerSettings.productName;
            bool keystore=PlayerSettings.Android.useCustomKeystore;
            try{
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.linxyi.LsVRPlayer.thumbnailtest");
                PlayerSettings.productName="VRPlayer Thumbnail Test";PlayerSettings.Android.useCustomKeystore=false;
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlayerBuild.ScenePath},locationPathName="Builds/ThumbnailValidation.apk",target=BuildTarget.Android,options=BuildOptions.Development|BuildOptions.CompressWithLz4});
                if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Thumbnail device validation build failed");
                UnityEngine.Debug.Log("VRPlayer thumbnail validation APK built successfully: Builds/ThumbnailValidation.apk");
            }finally{PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,identifier);PlayerSettings.productName=name;PlayerSettings.Android.useCustomKeystore=keystore;}
        }
    }
}
