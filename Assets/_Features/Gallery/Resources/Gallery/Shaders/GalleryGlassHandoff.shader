Shader "Runtime/Gallery Glass Handoff"
{
    Properties { [MainTexture]_BaseMap("World",2D)="white"{} [MainColor]_BaseColor("Tint",Color)=(1,1,1,1) _GlassProgress("Crossing",Float)=1 _GlassStrength("Strength",Float)=.55 _GlassDirection("Direction",Float)=1 _GlassTint("Glass",Color)=(.7,.85,1,1) }
    SubShader {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            float4 _BaseColor,_GlassTint;float _GlassProgress,_GlassStrength,_GlassDirection;
            struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            V Vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;return o;}
            half4 Frag(V i):SV_Target {
                float2 p=i.uv-.5;float radius=length(p);float envelope=sin(saturate(_GlassProgress)*3.14159265)*_GlassStrength;
                float wave=sin(radius*32-_GlassProgress*15*_GlassDirection)*exp(-abs(radius-lerp(.12,.8,_GlassProgress))*2);
                float2 offset=p/max(.001,radius)*wave*envelope*.012;
                half3 colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,saturate(i.uv+offset)).rgb;
                float rim=exp(-pow((radius-lerp(.06,.78,_GlassProgress))*28,2))*envelope;
                return half4(colour+_GlassTint.rgb*rim*.18,_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
