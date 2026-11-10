Shader "Runtime/Gallery Crystal"
{
    Properties { _Loaded("Loaded world",Range(0,1))=0 _LoadedTint("Loaded world tint",Color)=(.55,.84,1,1)  _Tint("Tint",Color)=(0.72,0.83,0.84,1) _Opacity("Sphere Opacity",Range(0,1))=0.2 _Rim("Rim Strength",Range(0,1))=0.72 _Frost("Frost",Range(0,1))=1 _WorldOpacity("World Opacity",Range(0,1))=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;};
            float4 _Tint,_LoadedTint;float _Loaded;float _Opacity,_Rim,_Frost,_WorldOpacity;
            Varyings Vert(Attributes v){Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);return o;}
            half4 Frag(Varyings i):SV_Target{float f=pow(1-abs(dot(normalize(i.normalWS),SafeNormalize(GetCameraPositionWS()-i.positionWS))),2.2);float recall=pow(f,1.35)*_Loaded;return half4(lerp(_Tint.rgb,_LoadedTint.rgb,recall),_WorldOpacity*(_Opacity*(.055+_Frost*.18+f*_Rim*.8)+recall*.6));}
            ENDHLSL
        }
    }
}
