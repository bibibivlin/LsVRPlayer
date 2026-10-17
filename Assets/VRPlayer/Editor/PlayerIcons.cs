using System;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace VRPlayer.Editor
{
    public static class PlayerIcons
    {
        private const string IconPath="Assets/VRPlayer/Art/AppIcon/";

        [MenuItem("VRPlayer/Apply App Icon")]
        public static void Configure()
        {
            var legacy=LoadIcon("AppIcon.png");
            var background=LoadIcon("AppIconBackground.png");
            var foreground=LoadIcon("AppIconForeground.png");
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown,new[]{legacy},IconKind.Any);
            int adaptive=SetIcons(AndroidPlatformIconKind.Adaptive,background,foreground);
            int compatibility=0;
            foreach(var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android)){
                if(!kind.Equals(AndroidPlatformIconKind.Adaptive))compatibility+=SetIcons(kind,legacy);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"VRPlayer: A app icon configured; adaptive={adaptive}, compatibility={compatibility} slots verified.");
        }

        private static Texture2D LoadIcon(string filename)
        {
            string path=IconPath+filename;
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new InvalidOperationException("App icon is missing: "+path);
            if(importer.textureType!=TextureImporterType.Default||importer.mipmapEnabled||
                importer.textureCompression!=TextureImporterCompression.Uncompressed||
                importer.npotScale!=TextureImporterNPOTScale.None||importer.maxTextureSize!=2048||
                !importer.alphaIsTransparency||!importer.sRGBTexture||importer.wrapMode!=TextureWrapMode.Clamp){
                importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=false;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
                importer.sRGBTexture=true;importer.wrapMode=TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static int SetIcons(PlatformIconKind kind,params Texture2D[] textures)
        {
            var target=NamedBuildTarget.Android;
            var icons=PlayerSettings.GetPlatformIcons(target,kind);
            if(icons.Length==0)throw new InvalidOperationException("Android icon slots are unavailable: "+kind);
            // Unity 6000.3 adaptive layers are background first, foreground second.
            foreach(var icon in icons)icon.SetTextures(textures);
            PlayerSettings.SetPlatformIcons(target,kind,icons);
            foreach(var icon in PlayerSettings.GetPlatformIcons(target,kind)){
                for(int layer=0;layer<textures.Length;layer++){
                    if(icon.GetTexture(layer)!=textures[layer])throw new InvalidOperationException("App icon assignment failed: "+icon);
                }
            }
            return icons.Length;
        }
    }
}
