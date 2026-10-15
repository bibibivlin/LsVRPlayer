Shader "VRPlayer/UI Surface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 geometry : TEXCOORD1;
                float4 treatment : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 geometry : TEXCOORD1;
                float4 treatment : TEXCOORD2;
                float4 worldPosition : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            fixed4 _Color;
            float4 _ClipRect;
            float3 Palette(float3 color)
            {
                #ifndef UNITY_COLORSPACE_GAMMA
                return GammaToLinearSpace(color);
                #else
                return color;
                #endif
            }
            float DistanceToPanel(float2 samplePosition, float2 halfSize, float radius)
            {
                float2 q=abs(samplePosition)-halfSize+radius;
                return min(max(q.x,q.y),0)+length(max(q,0))-radius;
            }
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition=v.vertex;
                o.vertex=UnityObjectToClipPos(v.vertex);
                o.color=v.color*_Color;
                o.uv=v.uv;o.geometry=v.geometry;o.treatment=v.treatment;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 size=max(i.geometry.xy,1);
                float2 samplePosition=(i.uv-.5)*size;
                float radius=min(i.geometry.z,min(size.x,size.y)*.5);
                float distance=DistanceToPanel(samplePosition,size*.5,radius);
                float aa=max(fwidth(distance),.65);
                float coverage=1-smoothstep(-aa,aa,distance);
                // Analytic lighting is stationary in panel space, with no screen grabs or blur passes.
                float top=saturate(i.uv.y);
                float glow=saturate(1-dot((i.uv-float2(.24,.95))*float2(.9,1.1),(i.uv-float2(.24,.95))*float2(.9,1.1)));
                float3 surface=i.color.rgb*lerp(.68,1.18,top);
                float glowStrength=i.treatment.x>3.5?.48:(i.treatment.x>2.5?.72:(i.treatment.x>1.5?.12:.24));
                surface+=Palette(float3(.13,.23,.32))*glow*glowStrength;
                float selected=i.treatment.y;
                if(selected>.5){surface=i.color.rgb*lerp(.80,1.12,top);surface+=Palette(float3(.08,.12,.12))*glow*.35;}
                // Transport progress is the background itself, with a broad vertical light falloff.
                if(i.treatment.x>2.5 && i.treatment.x<3.5){
                    float3 played=lerp(Palette(float3(.014,.19,.21)),Palette(float3(.008,.57,.57)),pow(top,1.4));
                    surface=lerp(surface,played,step(i.uv.x,i.treatment.z));
                }
                float stroke=(1-smoothstep(i.geometry.w-aa,i.geometry.w+aa,-distance))*coverage;
                float3 edge=lerp(Palette(float3(.24,.39,.49)),Palette(float3(.40,.91,.88)),selected);
                float edgeLight=lerp(.20,.63,pow(top,3));
                surface=lerp(surface,edge,stroke*edgeLight);
                // A narrow inner highlight gives the lip definition without a heavy bevel.
                float lip=exp2(-max(-distance-i.geometry.w,0)*1.5)*pow(top,7)*.07;
                surface+=Palette(float3(.38,.58,.68))*lip;
                // Subtitle color samples show their actual color, without blue surface lighting.
                if(i.treatment.x>5.5)surface=i.color.rgb;
                float shadowDistance=DistanceToPanel(samplePosition+float2(0,5),size*.5,radius);
                float shadow=exp2(-pow(max(shadowDistance,0)/13,2)*3)*i.treatment.w*i.color.a*(1-coverage);
                float alpha=coverage*i.color.a;
                float total=alpha+shadow*(1-alpha);
                float3 rgb=(surface*alpha+Palette(float3(.002,.006,.010))*shadow*(1-alpha))/max(total,.0001);
                #ifdef UNITY_UI_CLIP_RECT
                total*=UnityGet2DClipping(i.worldPosition.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                // Shadows must never expand a stencil mask or a seek surface.
                clip(coverage-.01);
                #endif
                return fixed4(rgb,total);
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
