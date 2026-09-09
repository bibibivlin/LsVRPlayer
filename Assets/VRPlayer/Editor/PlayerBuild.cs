using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using Unity.XR.CoreUtils;
using Unity.XR.PXR;

namespace VRPlayer.Editor
{
    public static class PlayerBuild
    {
        public const string ScenePath="Assets/VRPlayer/Scenes/Player.unity";
        [MenuItem("VRPlayer/Configure Project and Open Player")]
        public static void Configure()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before configuring the project.");
            Directory.CreateDirectory("Assets/VRPlayer/Scenes");Directory.CreateDirectory("Builds");Directory.CreateDirectory("TestMedia");
            PlayerSettings.productName="L's VRPlayer";PlayerSettings.companyName="Linxyi";PlayerSettings.bundleVersion="0.6.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.linxyi.LsVRPlayer");PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel29;PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevel35;PlayerSettings.Android.bundleVersionCode=6;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;PlayerSettings.runInBackground=false;PlayerSettings.Android.forceInternetPermission=true;
            var pico=AssetDatabase.LoadAssetAtPath<PXR_Settings>("Assets/XR/Settings/PXR_Settings.asset");if(pico!=null){pico.stereoRenderingModeAndroid=PXR_Settings.StereoRenderingModeAndroid.Multiview;pico.systemDisplayFrequency=PXR_Settings.SystemDisplayFrequency.RefreshRate72;EditorUtility.SetDirty(pico);}
            if(TMP_Settings.instance==null){var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Settings).Assembly);AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath,"Package Resources/TMP Essential Resources.unitypackage"),false);throw new InvalidOperationException("TMP essentials imported. Run Configure once importing has finished.");}
            EnsureShaders();EnsureFont();
            var quality=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels=quality.FindProperty("m_QualitySettings");for(int i=0;i<levels.arraySize;i++)levels.GetArrayElementAtIndex(i).FindPropertyRelative("antiAliasing").intValue=4;quality.ApplyModifiedPropertiesWithoutUndo();
            foreach(string path in new[]{"Assets/Plugins/Android/vrplayer-android.aar","Assets/Plugins/Android/arm64-v8a/libvrplayerrender.so"}){
                if(AssetImporter.GetAtPath(path) is PluginImporter importer){importer.SetCompatibleWithAnyPlatform(false);importer.SetCompatibleWithEditor(false);importer.SetCompatibleWithPlatform(BuildTarget.Android,true);if(path.EndsWith(".so"))importer.SetPlatformData(BuildTarget.Android,"CPU","ARM64");importer.SaveAndReimport();}
            }
            EditorSceneManager.SaveOpenScenes();
            if(File.Exists(ScenePath))EditorSceneManager.OpenScene(ScenePath);
            else CreateScene();
            var manager=UnityEngine.Object.FindFirstObjectByType<PXR_Manager>();if(manager!=null){manager.useRecommendedAntiAliasingLevel=false;manager.adaptiveResolution=false;EditorUtility.SetDirty(manager);}
            Camera camera=Camera.main;if(camera!=null){camera.allowMSAA=true;camera.allowDynamicResolution=false;EditorUtility.SetDirty(camera);}
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets();Debug.Log("VRPlayer: project configured; Player scene ready.");
        }
        private static void EnsureShaders()
        {
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);var shaders=settings.FindProperty("m_AlwaysIncludedShaders");
            foreach(string name in new[]{"VRPlayer/VideoProjection","VRPlayer/Subtitle Background","VRPlayer/Subtitle SDF","Unlit/Color","TextMeshPro/Distance Field","TextMeshPro/Mobile/Distance Field","UI/Default"}){
                Shader shader=Shader.Find(name);if(shader==null)continue;bool exists=false;for(int i=0;i<shaders.arraySize;i++)if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader)exists=true;
                if(!exists){int index=shaders.arraySize;shaders.InsertArrayElementAtIndex(index);shaders.GetArrayElementAtIndex(index).objectReferenceValue=shader;}
            }settings.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void EnsureFont()
        {
            const string path="Assets/VRPlayer/Resources/Fonts/UIChinese.asset";var existing=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if(existing!=null&&existing.material!=null&&existing.atlasTextures.Length>0&&existing.atlasTextures[0]!=null&&existing.faceInfo.pointSize==90&&existing.atlasWidth==4096&&existing.atlasPadding==12)return;
            var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/VRPlayer/Resources/Fonts/NotoSansCJKsc-Regular.otf");if(font==null)throw new InvalidOperationException("Chinese font has not been imported.");
            var asset=TMP_FontAsset.CreateFontAsset(font,90,12,GlyphRenderMode.SDFAA,4096,4096,AtlasPopulationMode.Dynamic,true);asset.name="UIChinese";
            if(existing!=null){foreach(var child in AssetDatabase.LoadAllAssetsAtPath(path))if(child!=existing)UnityEngine.Object.DestroyImmediate(child,true);EditorUtility.CopySerialized(asset,existing);asset.material=null;asset.atlasTextures=Array.Empty<Texture2D>();UnityEngine.Object.DestroyImmediate(asset);asset=existing;}
            else AssetDatabase.CreateAsset(asset,path);
            AssetDatabase.AddObjectToAsset(asset.material,asset);
            foreach(var atlas in asset.atlasTextures)AssetDatabase.AddObjectToAsset(atlas,asset);
            // Populate the shipped UI alphabet; dynamic atlases cover user filenames and subtitles.
            string alphabet=string.Concat(Directory.GetFiles("Assets/VRPlayer/Runtime","*.cs",SearchOption.AllDirectories).Select(File.ReadAllText));
            asset.TryAddCharacters(new string(alphabet.Where(c=>c>=32).Distinct().ToArray()));EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
        }
        private static void CreateScene()
        {
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var rig=new GameObject("XR Origin");var origin=rig.AddComponent<XROrigin>();rig.AddComponent<PXR_Manager>();
            var offset=new GameObject("Camera Offset");offset.transform.SetParent(rig.transform,false);
            var head=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));head.tag="MainCamera";head.transform.SetParent(offset.transform,false);head.transform.localPosition=new Vector3(0,1.6f,0);
            var camera=head.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.008f,.012f,.02f);camera.nearClipPlane=.05f;camera.farClipPlane=100;camera.fieldOfView=75;camera.allowHDR=false;
            var driver=head.AddComponent<TrackedPoseDriver>();driver.positionInput=new InputActionProperty(new InputAction("Head position",InputActionType.Value,"<XRHMD>/centerEyePosition",expectedControlType:"Vector3"));driver.rotationInput=new InputActionProperty(new InputAction("Head rotation",InputActionType.Value,"<XRHMD>/centerEyeRotation",expectedControlType:"Quaternion"));driver.trackingStateInput=new InputActionProperty(new InputAction("Head tracking",InputActionType.Value,"<XRHMD>/trackingState",expectedControlType:"Integer"));
            origin.Camera=camera;origin.CameraFloorOffsetObject=offset;origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
            var light=new GameObject("Soft Directional Light",typeof(Light));light.GetComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=.2f;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var app=new GameObject("VRPlayer").AddComponent<PlayerApp>();app.headCamera=camera;
            RenderSettings.ambientLight=new Color(.025f,.035f,.05f);EditorSceneManager.SaveScene(scene,ScenePath);
        }
        [MenuItem("VRPlayer/Build Android APK")]
        public static void BuildAndroid()
        {
            Configure();var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/LsVRPlayer.apk",target=BuildTarget.Android,options=BuildOptions.Development|BuildOptions.CompressWithLz4|BuildOptions.DetailedBuildReport});
            if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("VRPlayer Android build failed: "+result.summary.totalErrors+" errors");
            Debug.Log("VRPlayer APK built: "+Path.GetFullPath("Builds/LsVRPlayer.apk"));
        }
    }
}
