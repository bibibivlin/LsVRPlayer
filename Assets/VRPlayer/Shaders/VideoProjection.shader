Shader "VRPlayer/VideoProjection"
{
    Properties {
        _MainTex("Video",2D)="black"{}
        _Projection("Projection",Int)=0
        _Stereo("Stereo",Int)=0
        _SwapEyes("Swap",Float)=0
        _MonoEye("Single eye",Float)=0
        _GammaInput("Gamma input",Float)=1
        _Brightness("Brightness",Float)=1
        _FisheyeFov("Fisheye FOV",Float)=180
        _PanoramaZoom("Panorama zoom",Float)=1
        _PanoramaForward("Panorama forward",Vector)=(0,0,1,0)
        _AssTex("ASS overlay",2D)="black"{}
        _AssEnabled("ASS enabled",Float)=0
    }
    SubShader {
        // Draw after the skybox and before world-space menus, without occluding those menus.
        Tags {"RenderType"="Opaque" "Queue"="Transparent-100"}
        Cull Off ZWrite Off ZTest LEqual
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float3 direction:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
            sampler2D _MainTex,_AssTex;int _Projection,_Stereo;float _AssEnabled;
            float _SwapEyes,_MonoEye,_GammaInput,_Brightness,_FisheyeFov,_VRPlayerEye;
            float _PanoramaZoom;float3 _PanoramaForward;
            v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.direction=v.vertex.xyz;return o;}
            float2 cubeUV(float3 d,bool equiangular){
                float3 a=abs(d);float2 p;float2 cell;
                // Declared layout, read from the top: +X -X +Y / -Y +Z -Z.
                if(a.x>=a.y&&a.x>=a.z){if(d.x>0){p=float2(-d.z,d.y)/a.x;cell=float2(0,1);}else{p=float2(d.z,d.y)/a.x;cell=float2(1,1);}}
                else if(a.y>=a.z){if(d.y>0){p=float2(d.x,-d.z)/a.y;cell=float2(2,1);}else{p=float2(d.x,d.z)/a.y;cell=float2(0,0);}}
                else{if(d.z>0){p=float2(d.x,d.y)/a.z;cell=float2(1,0);}else{p=float2(-d.x,d.y)/a.z;cell=float2(2,0);}}
                if(equiangular)p=atan(p)*1.2732395447;
                return (cell+p*.5+.5)/float2(3,2);
            }
            fixed4 frag(v2f i):SV_Target{
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float eye=_VRPlayerEye;
                #if defined(USING_STEREO_MATRICES)
                eye=unity_StereoEyeIndex;
                #endif
                if(_MonoEye>.5)eye=0;if(_SwapEyes>.5)eye=1-eye;
                float2 uv=i.uv;float3 d=normalize(i.direction);
                // Stereographic angular scaling: stable at both poles, identity at 1x.
                if(_Projection!=0&&abs(_PanoramaZoom-1)>.00001){
                    float3 axis=normalize(_PanoramaForward);float c=clamp(dot(d,axis),-1,1);
                    float z=clamp(_PanoramaZoom,.5,2),z2=z*z;
                    float denom=(z2+1)+(z2-1)*c;
                    d=normalize((d-c*axis)*(2*z/denom)+axis*((z2-1+(z2+1)*c)/denom));
                }
                if(_Projection==1||_Projection==2){float longitude=atan2(d.x,d.z);if(_Projection==1){if(abs(longitude)>UNITY_PI*.5)return fixed4(0,0,0,1);uv.x=longitude/UNITY_PI+.5;}else uv.x=longitude/(UNITY_PI*2)+.5;uv.y=asin(clamp(d.y,-1,1))/UNITY_PI+.5;}
                else if(_Projection==3){float theta=acos(clamp(d.z,-1,1));float halfFov=radians(_FisheyeFov*.5);if(theta>halfFov)return fixed4(0,0,0,1);uv=.5+(d.xy/max(length(d.xy),.00001))*(theta/halfFov)*.5;}
                else if(_Projection==4||_Projection==5)uv=cubeUV(d,_Projection==5);
                if(_Stereo==1)uv.x=uv.x*.5+eye*.5;
                else if(_Stereo==2)uv.y=uv.y*.5+(1-eye)*.5;
                float3 rgb=tex2D(_MainTex,uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                if(_GammaInput>.5)rgb=GammaToLinearSpace(rgb);
                #endif
                rgb*=_Brightness;
                // Script coordinates cover the displayed video rectangle in both eyes.
                // Sample before stereo packing: subtitle geometry never leaves the mesh.
                if(_Projection==0&&_AssEnabled>.5){
                    float4 ass=tex2D(_AssTex,i.uv);
                    float3 assRgb=ass.a>0?ass.rgb/ass.a:0;
                    #ifndef UNITY_COLORSPACE_GAMMA
                    assRgb=GammaToLinearSpace(assRgb);
                    #endif
                    rgb=rgb*(1-ass.a)+assRgb*ass.a;
                }
                return float4(rgb,1);
            }
            ENDCG
        }
    }
}
