Shader "Runtime/Gallery Orbit"
{
    Properties {
        _PhaseOffset("Phase offset",Float)=0 _TravelDirection("Travel direction",Float)=1
        _TravelSpeed("Travel speed",Float)=0.22 _GlowSharpness("Glow sharpness",Float)=18 _GlowIntensity("Glow intensity",Float)=1.25
        _SurfaceSoftness("Surface softness",Range(0.2,3))=1.6 _Tint("Tint", Color)=(0.57,0.67,0.63,1)
        _Opacity("Opacity", Range(0,1))=0.5 _RearFade("Rear Fade",Range(0,1))=0.98
        _Phase("Phase",Float)=0 _Mode("Mode",Float)=0 _Radius("Radius",Float)=2 _TubeRadius("Tube radius",Float)=0.5
        _Clearance("Sphere radius",Float)=0.825 _Feather("Clearance feather",Float)=0.35
        _DustDensity("Dust density",Range(.1,2))=1 _DustScale("Dust detail",Range(.2,5))=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float3 positionOS:TEXCOORD3; };
            float4 _Tint,_Accent,_Origin,_Facing,_RevealCenter;
            float _PhaseOffset,_TravelDirection,_TravelSpeed,_GlowSharpness,_GlowIntensity,_SurfaceSoftness;
            float _Opacity,_RearFade,_Phase,_Mode,_Radius,_TubeRadius,_Clearance,_Feather,_DustDensity,_DustScale;
            float _RevealActive,_RevealProgress,_RevealRadius;
            int _SphereCount;float4 _SphereCenters[32];
            Varyings Vert(Attributes v) { Varyings o;o.positionOS=v.positionOS.xyz;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;return o; }
            float Hash(float3 p){return frac(sin(dot(p,float3(12.9898,78.233,37.719)))*43758.5453);}
            float2 Dust(float3 entry,float3 view)
            {
                // Integrate a world-space volume inside the existing rounded bounds. No screen-facing ribbon.
                float2 sum=0;float width=max(.015,_TubeRadius);
                [unroll] for(int stepIndex=0;stepIndex<8;stepIndex++) {
                    float3 p=entry-view*((stepIndex+.5)*width*.29);
                    float radial=length(p.xz)-_Radius;
                    float2 q=float2(radial/width,p.y/(width*.38));float q2=dot(q,q);
                    float angle=atan2(p.z,p.x);float twist=angle*3-_Phase*.035*_TravelDirection+_PhaseOffset;
                    float2 strand=q-float2(sin(twist)*.34,cos(twist)*.25);
                    float2 strand2=q-float2(sin(twist+2.4)*.42,cos(twist+2.4)*.2);
                    float body=(exp(-dot(strand,strand)*5)+.5*exp(-dot(strand2,strand2)*10)+.25*exp(-q2*2))*(1-smoothstep(.5,1,q2));
                    float3 detail=p*_DustScale;
                    float clouds=.5+.5*sin(angle*19+radial*9+sin(detail.y*8+detail.z*2+_PhaseOffset));
                    float filaments=.38+.62*clouds*clouds;
                    float3 grid=detail*14+_PhaseOffset;float3 cell=floor(grid);
                    float3 seed=float3(Hash(cell),Hash(cell+31),Hash(cell+67));float3 offset=frac(grid)-seed;
                    float spark=step(.98,Hash(cell+113))*exp(-dot(offset,offset)*140);
                    sum+=float2(body*filaments*.125,body*spark);
                }
                return sum;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float nearest=10000;
                [loop] for(int n=0;n<32;n++){if(n>=_SphereCount)break;nearest=min(nearest,distance(i.positionWS,_SphereCenters[n].xyz));}
                float clearance=smoothstep(_Clearance+.025,_Clearance+max(.04,_Feather),nearest);
                float2 offset=i.positionWS.xz-_Origin.xz;
                float front=dot(offset/max(.001,length(offset)),_Facing.xz);
                float rear=1-_RearFade*smoothstep(.15,.95,.5-.5*front);
                float revealDistance=distance(i.positionWS,_RevealCenter.xyz)/max(.1,_RevealRadius);
                float reveal=saturate((_RevealProgress-revealDistance*.72)/.28);reveal=reveal*reveal*(3-2*reveal);
                reveal=lerp(1,reveal,_RevealActive);
                float angle=i.uv.x*6.283185;
                float glow=pow(saturate(.5+.5*cos(angle-_Phase*_TravelSpeed*_TravelDirection-_PhaseOffset)),max(2,_GlowSharpness));
                if(_Mode>3.5) {
                    float3 view=SafeNormalize(TransformWorldToObject(GetCameraPositionWS())-i.positionOS);
                    float2 dust=Dust(i.positionOS,view);
                    float density=_DustDensity*(dust.x*4.1*(.65+glow*_GlowIntensity)+dust.y*.9);
                    float3 colour=lerp(_Tint.rgb,_Accent.rgb,saturate(glow*.95))*(1.08+glow*.6)+dust.y*.9;
                    return half4(colour,saturate(_Opacity*density*clearance*rear*reveal));
                }
                float effect=.28+glow*_GlowIntensity;
                if(_Mode>.5&&_Mode<1.5)effect=1;
                if(_Mode>1.5&&_Mode<2.5){float cell=frac(i.uv.x*_Radius*10);effect=smoothstep(.04,.12,cell)*(1-smoothstep(.62,.70,cell));}
                if(_Mode>2.5){float dots=sin(i.uv.x*700)*sin(i.uv.y*80);effect=smoothstep(.55,.95,dots)*(.4+glow);}
                float3 normal=normalize(i.normalWS),view=SafeNormalize(GetCameraPositionWS()-i.positionWS);
                float edge=pow(saturate(abs(dot(normal,view))),max(.2,_SurfaceSoftness));
                float light=.68+.32*saturate(dot(normal,normalize(float3(-.4,.8,-.5))));
                return half4(lerp(_Tint.rgb,_Accent.rgb,glow*.9)*(light+glow*.15),saturate(_Opacity*effect*clearance*rear*edge*reveal));
            }
            ENDHLSL
        }
    }
}
