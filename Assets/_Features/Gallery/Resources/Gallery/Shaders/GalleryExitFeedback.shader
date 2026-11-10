Shader "Runtime/Gallery Exit Feedback"
{
    Properties { _Strength("Elastic vignette",Float)=0 _Tint("Tint",Color)=(.02,.04,.05,1) _GlassProgress("Glass crossing",Float)=1 _GlassStrength("Glass strength",Float)=.55 _GlassTint("Glass tint",Color)=(.7,.85,1,1) }
    SubShader {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Overlay" "RenderType"="Transparent"}
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Strength,_GlassProgress,_GlassStrength;float4 _Tint,_GlassTint;
            struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            V Vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;return o;}
            half4 Frag(V i):SV_Target {
                float radius=length((i.uv-.5)*1.42);float vignette=smoothstep(.25,.95,radius)*_Strength*.7;
                float glass=sin(saturate(_GlassProgress)*3.14159265)*_GlassStrength;
                float ring=exp(-pow((radius-lerp(.06,1.1,_GlassProgress))*24,2))*glass*.48;
                return half4(lerp(_Tint.rgb,_GlassTint.rgb,saturate(ring*4)),saturate(vignette+ring));
            }
            ENDHLSL
        }
    }
}
