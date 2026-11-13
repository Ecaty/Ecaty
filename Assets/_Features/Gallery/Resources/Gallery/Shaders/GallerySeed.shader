Shader "Gallery/Seed"
{
    Properties {_Tint("Tint",Color)=(1,1,1,1) _Opacity("Opacity",Range(0,1))=1}
    SubShader{
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
        Pass{
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A{float4 position:POSITION;float3 normal:NORMAL;float4 color:COLOR;};
            struct V{float4 position:SV_POSITION;float3 normal:TEXCOORD0;float4 color:COLOR;};
            float4 _Tint;float _Opacity;
            V Vert(A a){V v;v.position=TransformObjectToHClip(a.position.xyz);v.normal=TransformObjectToWorldNormal(a.normal);v.color=a.color;return v;}
            half4 Frag(V v):SV_Target {float light=.5+.5*saturate(dot(normalize(v.normal),normalize(float3(-.4,.8,-.5))));return half4(v.color.rgb*_Tint.rgb*light,v.color.a*_Opacity);}
            ENDHLSL
        }
    }
}
