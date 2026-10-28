// Haut des Patienten (URP, ohne Shader Graph). Werte aus skin.json, gesetzt von AnatomyPatient.
// - Licht unter der Haut: Wrap-Lighting je Farbkanal (Rot streut am weitesten), Green 2004 (GPU Gems Kap. 16)
// - Glanz: zwei GGX-Keulen, F0 0,028 (Weyrich 2006); Schweiß macht die Oberfläche glatter
// - Feinstruktur: Poren als Normalenstörung aus Rauschen auf der Ruhelage (UV0 = Modellposition, schwimmt nicht)
// - Durchblutung: Blässe, Zyanose (betont an Gesicht und Händen), Marmorierung von den Knien aus
Shader "NotfallSIM/Skin"
{
    Properties
    {
        _BaseColor ("Hautton", Color) = (0.86, 0.67, 0.55, 1)
        _PaleColor ("Blass", Color) = (0.87, 0.80, 0.75, 1)
        _CyanColor ("Zyanotisch", Color) = (0.56, 0.60, 0.76, 1)
        _MottleColor ("Marmoriert", Color) = (0.52, 0.30, 0.42, 1)
        _Pallor ("Blässe", Range(0, 1)) = 0
        _Cyanosis ("Zyanose", Range(0, 1)) = 0
        _Mottling ("Marmorierung", Range(0, 1)) = 0
        _Sweat ("Schweiß", Range(0, 1)) = 0
        _Wrap ("Wrap je Kanal", Vector) = (0.55, 0.25, 0.15, 0)
        _F0 ("F0", Float) = 0.028
        _Rough ("Rauheit (2 Keulen)", Vector) = (0.35, 0.6, 0, 0)
        _PoreScale ("Porenmaßstab", Float) = 900
        _PoreStrength ("Porenstärke", Range(0, 1)) = 0.35
        _KneeR ("Knie rechts", Vector) = (0.074, -0.415, 0, 0)
        _KneeL ("Knie links", Vector) = (-0.075, -0.414, 0, 0)
        _Head ("Gesicht", Vector) = (0, 0.70, 0.08, 0)
        _HandR ("Hand rechts", Vector) = (0.27, -0.08, 0.05, 0)
        _HandL ("Hand links", Vector) = (-0.27, -0.08, 0.05, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _PaleColor, _CyanColor, _MottleColor;
            half _Pallor, _Cyanosis, _Mottling, _Sweat;
            float4 _Wrap, _Rough;
            float _F0, _PoreScale, _PoreStrength;
            float4 _KneeR, _KneeL, _Head, _HandR, _HandL;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 rest : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float3 rest : TEXCOORD2; float fog : TEXCOORD3;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.rest = i.rest;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash3(i), hash3(i + float3(1,0,0)), f.x), lerp(hash3(i + float3(0,1,0)), hash3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash3(i + float3(0,0,1)), hash3(i + float3(1,0,1)), f.x), lerp(hash3(i + float3(0,1,1)), hash3(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            float D_GGX(float nh, float a) { float a2 = a * a; float d = nh * nh * (a2 - 1) + 1; return a2 / (PI * d * d + 1e-6); }

            half3 Shade(Light l, float3 n, float3 v, half3 albedo, float rough1, float rough2)
            {
                float nl = dot(n, l.direction);
                // Streuung: je Kanal „umgewickeltes“ Licht, Rot dringt am tiefsten ein
                float3 wrap = saturate((nl + _Wrap.xyz) / (1 + _Wrap.xyz));
                half atten = l.distanceAttenuation * l.shadowAttenuation;
                // Schattenkante leicht rötlich (Streuung in den Schatten hinein)
                half3 shadowTint = lerp(half3(1, 0.35, 0.25), half3(1, 1, 1), l.shadowAttenuation);
                half3 diffuse = albedo * wrap * l.color * l.distanceAttenuation * lerp(l.shadowAttenuation, 1, 0.15 * shadowTint);
                float3 h = normalize(l.direction + v);
                float nh = saturate(dot(n, h)), lh = saturate(dot(l.direction, h)), nlp = saturate(nl);
                float F = _F0 + (1 - _F0) * pow(1 - lh, 5);
                float D = 0.85 * D_GGX(nh, rough1 * rough1) + 0.15 * D_GGX(nh, rough2 * rough2);
                float vis = 0.25 / max(lh * lh * (rough1 + 0.5), 0.05);
                half3 spec = (F * D * vis * nlp) * l.color * atten;
                return diffuse + spec;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                // Poren: Gradient eines feinen Rauschens auf der Ruhelage
                float3 q = i.rest * _PoreScale;
                float e = 0.35;
                float n0 = vnoise(q);
                float3 g = float3(vnoise(q + float3(e, 0, 0)) - n0, vnoise(q + float3(0, e, 0)) - n0, vnoise(q + float3(0, 0, e)) - n0) / e;
                float3 gw = TransformObjectToWorldDir(g, false);
                n = normalize(n - _PoreStrength * 0.15 * (gw - dot(gw, n) * n));

                // Durchblutung
                half3 albedo = _BaseColor.rgb * (0.94 + 0.12 * vnoise(i.rest * 40));             // leichte Fleckigkeit
                albedo = lerp(albedo, _PaleColor.rgb, _Pallor * 0.85);
                float face = exp(-dot(i.rest - _Head.xyz, i.rest - _Head.xyz) / 0.004);
                float hands = exp(-dot(i.rest - _HandR.xyz, i.rest - _HandR.xyz) / 0.006) + exp(-dot(i.rest - _HandL.xyz, i.rest - _HandL.xyz) / 0.006);
                float cyan = _Cyanosis * saturate(0.45 + 0.55 * saturate(face + hands));
                albedo = lerp(albedo, _CyanColor.rgb, cyan * 0.75);
                // Marmorierung: netzförmig, von den Knien aus mit der Schwere größer
                float dk = min(distance(i.rest.xy, _KneeR.xy), distance(i.rest.xy, _KneeL.xy));
                float reach = saturate(1 - dk / (0.06 + 0.45 * _Mottling));
                float3 wp = i.rest * 28 + 2.5 * float3(vnoise(i.rest * 9), vnoise(i.rest * 9 + 3.1), vnoise(i.rest * 9 + 6.7)); // verzerrtes Netz
                float ridge = 1 - abs(2 * vnoise(wp) - 1);
                float spots = smoothstep(0.35, 0.75, vnoise(i.rest * 6));                                               // fleckig, nicht flächig
                float mottle = smoothstep(0.62, 0.95, ridge) * (0.35 + 0.65 * spots) * reach * _Mottling;
                albedo = lerp(albedo, _MottleColor.rgb, mottle * 0.6);

                float rough1 = lerp(_Rough.x, 0.18, _Sweat) * (0.9 + 0.2 * n0);
                float rough2 = _Rough.y;

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                half3 col = albedo * SampleSH(n);
                col += Shade(GetMainLight(shadowCoord), n, v, albedo, rough1, rough2);
                #if defined(_ADDITIONAL_LIGHTS)
                // Zusatzlichter (OP-Leuchte usw.); LIGHT_LOOP_BEGIN deckt Forward und Forward+/Cluster ab
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    col += Shade(al, n, v, albedo, rough1, rough2);
                LIGHT_LOOP_END
                #endif
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert (Attributes i) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }
            half4 frag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert (Attributes i) : SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half frag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert (Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag (Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
