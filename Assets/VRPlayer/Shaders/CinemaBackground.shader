Shader "VRPlayer/Cinema Background"
{
    Properties
    {
        _MainTex("UI texture", 2D) = "white" {}
        _Mode("Mode", Float) = 0
        _GrayLevel("sRGB gray level", Float) = 0.0627451
        _Feather("Feather radians", Float) = 0.20944
        _ScreenBounds("Angular screen bounds", Vector) = (0.571,0.347,0,0)
        _RoomOffset("Room viewer offset", Vector) = (0,0,0,0)
        _Preview("Preview", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float _Mode, _GrayLevel, _Feather, _Preview;
            float4 _ScreenBounds;
            float3 _RoomOffset;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex); o.world = mul(unity_ObjectToWorld,v.vertex).xyz; o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(mul((float3x3)unity_WorldToObject, i.world - _WorldSpaceCameraPos));
                if (_Preview > .5) d = normalize(float3((i.uv.x-.5)*2.6,(i.uv.y-.5)*1.6,1));
                float yaw = atan2(d.x,d.z), pitch = atan2(d.y,length(d.xz));
                float2 outside = max(float2(abs(yaw)-_ScreenBounds.x,abs(pitch-_ScreenBounds.z)-_ScreenBounds.y),0);
                float surround = 1-smoothstep(0,max(.001,_Feather),length(outside));
                float level = 0;
                if (_Mode > .5 && _Mode < 1.5) level = 1;
                else if (_Mode < 2.5 && _Mode > 1.5) level = surround;
                else if (_Mode > 2.5)
                {
                    // Broad matte wall, ceiling and floor planes; no bright seams or moving detail.
                    float3 origin = _Preview > .5 ? float3(0,0,0) : _RoomOffset;
                    origin = clamp(origin,float3(-6,-1.5,-7),float3(6,3,7));
                    float3 extent = float3(7, d.y > 0 ? 3.5 : 2, 8);
                    float3 distanceToWall = (extent-sign(d)*origin)/max(abs(d),.0001);
                    float t = min(distanceToWall.x,min(distanceToWall.y,distanceToWall.z));
                    float3 hitPosition = origin+d*t;
                    float floor = 1-smoothstep(-2,-1.65,hitPosition.y);
                    float ceiling = smoothstep(3.1,3.5,hitPosition.y);
                    float side = smoothstep(6.5,7,abs(hitPosition.x));
                    if (_Mode < 3.5)
                    {
                        float panels = .5+.5*cos(hitPosition.x*1.3);
                        level = (.43 + .07*panels - .10*side - .15*ceiling - .06*floor) + .40*surround;
                    }
                    else
                    {
                        // Fixed matte surfaces: no screen-shaped light, stripes or reflections.
                        level = .72 - .08*side - .16*ceiling - .12*floor;
                    }
                }
                float gray = saturate(level)*clamp(_GrayLevel,0,48.0/255.0);
                // Keep UI values in SDR code space, even in the project's linear rendering path.
                #ifndef UNITY_COLORSPACE_GAMMA
                    gray = GammaToLinearSpaceExact(gray);
                #endif
                return float4(gray,gray,gray,1);
            }
            ENDCG
        }
    }
}
