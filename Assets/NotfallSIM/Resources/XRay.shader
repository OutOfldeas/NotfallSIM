// Röntgenblick: Haut als leuchtender Umriss (Fresnel), additiv, ohne Tiefenschreiben —
// die Organe dahinter bleiben sichtbar. URP, ohne Shader Graph.
Shader "NotfallSIM/XRay"
{
    Properties
    {
        _Color ("Farbe", Color) = (0.45, 0.75, 1, 1)
        _Power ("Randschärfe", Range(0.5, 8)) = 2.2
        _Intensity ("Intensität", Range(0, 3)) = 0.9
        _Base ("Grundhelligkeit", Range(0, 0.5)) = 0.04
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "XRay"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Power;
                half _Intensity;
                half _Base;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                half rim = pow(1.0h - saturate(abs(dot(n, v))), _Power);
                return half4(_Color.rgb * (rim * _Intensity + _Base), 1);
            }
            ENDHLSL
        }
    }
}
