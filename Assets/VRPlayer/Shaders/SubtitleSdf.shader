Shader "VRPlayer/Subtitle SDF"
{
    Properties {
        _MainTex("Font Atlas",2D)="white"{}
        _FaceColor("Text",Color)=(1,1,1,1)
        _SubtitleBrightness("Subtitle brightness",Float)=1
        _FaceDilate("Dilate",Range(-1,1))=0
        _OutlineColor("Outline",Color)=(0,0,0,1)
        _OutlineWidth("Outline width",Range(0,1))=0
        _OutlineSoftness("Outline softness",Range(0,1))=0
        _UnderlayColor("Shadow",Color)=(0,0,0,.65)
        _UnderlayOffsetX("Shadow X",Float)=.3
        _UnderlayOffsetY("Shadow Y",Float)=-.3
        _UnderlaySoftness("Shadow softness",Float)=.25
        _UnderlayDilate("Shadow dilate",Float)=0
        _TextureWidth("Atlas width",Float)=4096
        _TextureHeight("Atlas height",Float)=4096
        _GradientScale("SDF spread",Float)=13
        _WeightNormal("Normal weight",Float)=0
        _WeightBold("Bold weight",Float)=.5
        _ScaleRatioA("Ratio A",Float)=1
        _ScaleRatioB("Ratio B",Float)=1
        _ScaleRatioC("Ratio C",Float)=1
        _ShaderFlags("Flags",Float)=0
        _ScaleX("Scale X",Float)=1
        _ScaleY("Scale Y",Float)=1
        _ClipRect("Clip",Vector)=(-32767,-32767,32767,32767)
        _StencilComp("Stencil comparison",Float)=8
        _Stencil("Stencil",Float)=0
        _StencilOp("Stencil operation",Float)=0
        _StencilReadMask("Read mask",Float)=255
        _StencilWriteMask("Write mask",Float)=255
        _ColorMask("Color mask",Float)=15
        _ZTest("Depth comparison",Float)=4
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [_ZTest]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local __ OUTLINE_ON
            #pragma multi_compile_local __ UNDERLAY_ON
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "CinemaComfort.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float4 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _FaceColor,_OutlineColor,_UnderlayColor,_ClipRect;
            float _FaceDilate,_OutlineWidth,_OutlineSoftness,_UnderlayOffsetX,_UnderlayOffsetY,_UnderlaySoftness,_UnderlayDilate,_TextureWidth,_TextureHeight,_GradientScale;
            int _UIVertexColorAlwaysGammaSpace;
            float _SubtitleBrightness;
            v2f vert(appdata v) {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv.xy;o.local=v.vertex.xy;o.color=v.color;
                if(_UIVertexColorAlwaysGammaSpace&&!IsGammaSpace())o.color.rgb=UIGammaToLinear(o.color.rgb);
                return o;
            }
            float4 frag(v2f i):SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float sdf=tex2D(_MainTex,i.uv).a;
                // Derivatives preserve edge width at every distance without tiny TMP scale UVs.
                float edge=max(fwidth(sdf)*.5,.0001),threshold=.5-_FaceDilate*.5;
                float face=smoothstep(threshold-edge,threshold+edge,sdf);
                float4 faceColor=i.color*_FaceColor;faceColor.rgb*=faceColor.a;
                float4 color=faceColor*face;
                #if OUTLINE_ON
                float outerThreshold=threshold-max(_OutlineWidth*.5,edge*_OutlineWidth*10);
                float softness=edge+_OutlineSoftness*.25;
                float outer=smoothstep(outerThreshold-softness,outerThreshold+softness,sdf);
                float4 outline=_OutlineColor;outline.a*=i.color.a;outline.rgb*=outline.a;
                color+=outline*max(0,outer-face);
                #endif
                #if UNDERLAY_ON
                float2 offset=float2(_UnderlayOffsetX/_TextureWidth,_UnderlayOffsetY/_TextureHeight)*_GradientScale;
                float shadowSdf=tex2D(_MainTex,i.uv-offset).a;
                float blur=edge*(1+_UnderlaySoftness*4),shadowThreshold=threshold-_UnderlayDilate*.5;
                float alpha=smoothstep(shadowThreshold-blur,shadowThreshold+blur,shadowSdf)*_UnderlayColor.a*i.color.a;
                color+=float4(_UnderlayColor.rgb*alpha,alpha)*(1-color.a);
                #endif
                #if UNITY_UI_CLIP_RECT
                color*=UnityGet2DClipping(i.local,_ClipRect);
                #endif
                if(color.a>0&&_SubtitleBrightness<1)color.rgb=CinemaSubtitleColor(color.rgb/color.a,_SubtitleBrightness)*color.a;
                return color;
            }
            ENDCG
        }
    }
}
