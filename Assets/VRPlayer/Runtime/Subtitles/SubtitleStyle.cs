using System;
using UnityEngine;

namespace VRPlayer
{
    public enum SubtitleBackground { None, TextBox, FullLine }
    [Serializable] public sealed class SubtitleStyle
    {
        public Color textColor=new Color(.89f,.94f,.98f,1),outlineColor=Color.black,backgroundColor=Color.black;
        public bool outline=true,shadow;
        public float outlineWidth=.2f,shadowStrength=.65f,backgroundOpacity=.6f;
        public SubtitleBackground background;
        public void Normalize()
        {
            outlineWidth=Clamp(outlineWidth,.2f,0,.4f);shadowStrength=Clamp(shadowStrength,.65f,0,1);backgroundOpacity=Clamp(backgroundOpacity,.6f,0,1);
            if(!Enum.IsDefined(typeof(SubtitleBackground),background))background=SubtitleBackground.None;
        }
        private static float Clamp(float value,float fallback,float min,float max)=>Mathf.Clamp(float.IsNaN(value)||float.IsInfinity(value)?fallback:value,min,max);
        public static SubtitleStyle Preset(int index)
        {
            var style=new SubtitleStyle();
            if(index==1)style.textColor=Color.yellow;
            if(index==2){style.shadow=true;style.outlineWidth=.08f;}
            if(index==3){style.background=SubtitleBackground.TextBox;style.outlineWidth=.08f;}
            return style;
        }
    }

    [Serializable]
    public sealed class FlatSubtitleSettings
    {
        public const float MinSize = 12, MaxSize = 200;
        public const float MinAssScale = .5f, MaxAssScale = 1.5f, MaxAssOffset = .25f;

        public float size = 42;
        public float verticalPosition;
        public SubtitleStyle style = new SubtitleStyle();
        public float assScale = 1;
        public float assOffset;

        public void Normalize()
        {
            size = Clamp(size, 42, MinSize, MaxSize);
            verticalPosition = Clamp(verticalPosition, 0, 0, 1);
            style ??= new SubtitleStyle();
            style.Normalize();
            assScale = Clamp(assScale, 1, MinAssScale, MaxAssScale);
            assOffset = Clamp(assOffset, 0, -MaxAssOffset, MaxAssOffset);
        }

        private static float Clamp(float value, float fallback, float min, float max) =>
            Mathf.Clamp(float.IsFinite(value) ? value : fallback, min, max);
    }
}
