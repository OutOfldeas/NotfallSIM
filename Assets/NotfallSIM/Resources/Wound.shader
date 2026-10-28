// Verletzungszeichen auf der Haut und Blutlachen: Farbe aus der Vertexfarbe, Ausdehnung über _Amount mit
// unregelmäßigem Rand (Rauschen je Pixel), feuchter Glanz über _Wet. Transparent über der Haut, URP, ohne Shader Graph.
// UV0 = Modellposition (für das Rauschen, bleibt beim Wachsen ortsfest), UV1.x = Abfall 1 (Mitte) … 0 (Rand).
// Tiefe: der Abfall dient als Höhenfeld (Wundgrund in der Mitte), daraus Normale (Oberflächengradient, Mikkelsen 2010),
// Verschattung im Wundgrund und ein leicht erhabener Wundrand. Trocknung: _Dry 0 frisch … 1 eingetrocknet (dunkler, matt).
Shader "NotfallSIM/Wound"
{
    Properties
    {
        _Amount ("Ausprägung", Range(0, 1)) = 1
        _Wet ("Feuchtigkeit", Range(0, 1)) = 0
        _NoiseScale ("Rauschmaßstab", Float) = 60
        _Edge ("Randunschärfe", Range(0.01, 0.5)) = 0.18
        _Irregular ("Unregelmäßigkeit des Rands", Range(0, 1)) = 0.35
        _Depth ("Wundtiefe", Range(0, 1)) = 0
        _Dry ("Eingetrocknet", Range(0, 1)) = 0
        _DryColor ("Trockenes Blut", Color) = (0.24, 0.05, 0.04, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Wound"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Amount;
                half _Wet;
                float _NoiseScale;
                half _Edge;
                half _Irregular;
                half _Depth;
                half _Dry;
                half4 _DryColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR;
                float3 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1;
                half4 color : COLOR; float3 model : TEXCOORD2; float falloff : TEXCOORD3;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.model = i.uv0;
                o.falloff = i.uv1.x;
                return o;
            }

            float hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash3(i), hash3(i + float3(1,0,0)), f.x), lerp(hash3(i + float3(0,1,0)), hash3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash3(i + float3(0,0,1)), hash3(i + float3(1,0,1)), f.x), lerp(hash3(i + float3(0,1,1)), hash3(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 x) { return 0.5 * vnoise(x) + 0.3 * vnoise(x * 2.1) + 0.2 * vnoise(x * 4.3); }

            half3 ShadeWound(Light l, float3 nrm, float3 v, half3 baseCol, half wet)
            {
                half ndl = saturate(dot(nrm, l.direction));
                half atten = l.distanceAttenuation * l.shadowAttenuation;
                float3 h = normalize(l.direction + v);
                half spec = pow(saturate(dot(nrm, h)), lerp(16, 200, wet)) * wet * 1.5;
                return (baseCol * ndl + spec) * l.color * atten;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // Sichtbar, wo der Abfall (verrauscht) über der Schwelle liegt: wächst mit _Amount von der Mitte nach außen
                float n = fbm(i.model * _NoiseScale);
                float f = i.falloff + (n - 0.5) * _Irregular;
                float thr = 1 - _Amount;
                float a = smoothstep(thr, thr + _Edge, f) * i.color.a * step(0.001, _Amount);
                half3 baseCol = i.color.rgb * (0.85 + 0.3 * fbm(i.model * _NoiseScale * 2.7));

                float3 nrm = normalize(i.normalWS);
                // Wundtiefe: Höhe h = −Tiefe im Wundgrund, Rand leicht erhaben; Normale aus dem Oberflächengradienten
                float bed = smoothstep(0.35, 0.8, f);
                float rim = smoothstep(0.15, 0.35, f) * (1 - bed);
                float hgt = (_Depth * (-0.004 * bed + 0.0008 * rim));
                float3 dpdx = ddx(i.positionWS), dpdy = ddy(i.positionWS);
                float3 r1 = cross(dpdy, nrm), r2 = cross(nrm, dpdx);
                float det = dot(dpdx, r1);
                float3 grad = (abs(det) > 1e-12) ? (ddx(hgt) * r1 + ddy(hgt) * r2) / det : 0;
                nrm = normalize(nrm - grad);
                // Verschattung im Wundgrund, Rand leicht gerötet/erhaben
                baseCol *= lerp(1, 0.45, bed * _Depth);
                baseCol = lerp(baseCol, baseCol * half3(1.25, 0.9, 0.9), rim * _Depth);

                // Trocknung: dunkler, bräunlich, matt
                baseCol = lerp(baseCol, _DryColor.rgb, _Dry * 0.8);
                half wet = _Wet * (1 - _Dry) * lerp(1, 1.2, bed);

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 col = baseCol * SampleSH(nrm);
                col += ShadeWound(GetMainLight(TransformWorldToShadowCoord(i.positionWS)), nrm, v, baseCol, wet);
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = nrm;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    col += ShadeWound(al, nrm, v, baseCol, wet);
                LIGHT_LOOP_END
                #endif
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
