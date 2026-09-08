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
}
