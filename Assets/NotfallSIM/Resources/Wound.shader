// Verletzungszeichen auf der Haut und Blutlachen: Farbe aus der Vertexfarbe, Ausdehnung über _Amount mit
// unregelmäßigem Rand (Rauschen je Pixel), feuchter Glanz über _Wet. Transparent über der Haut, URP, ohne Shader Graph.
// UV0 = Modellposition (für das Rauschen, bleibt beim Wachsen ortsfest), UV1.x = Abfall 1 (Mitte) … 0 (Rand).
Shader "NotfallSIM/Wound"
{
    Properties
    {
        _Amount ("Ausprägung", Range(0, 1)) = 1
        _Wet ("Feuchtigkeit", Range(0, 1)) = 0
        _NoiseScale ("Rauschmaßstab", Float) = 60
        _Edge ("Randunschärfe", Range(0.01, 0.5)) = 0.18
        _Irregular ("Unregelmäßigkeit des Rands", Range(0, 1)) = 0.35
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Amount;
                half _Wet;
                float _NoiseScale;
                half _Edge;
                half _Irregular;
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

            half4 frag (Varyings i) : SV_Target
            {
                // Sichtbar, wo der Abfall (verrauscht) über der Schwelle liegt: wächst mit _Amount von der Mitte nach außen
                float n = fbm(i.model * _NoiseScale);
                float f = i.falloff + (n - 0.5) * _Irregular;
                float thr = 1 - _Amount;
                float a = smoothstep(thr, thr + _Edge, f) * i.color.a * step(0.001, _Amount);
                // leichte Fleckigkeit der Farbe
                half3 baseCol = i.color.rgb * (0.85 + 0.3 * fbm(i.model * _NoiseScale * 2.7));

                float3 nrm = normalize(i.normalWS);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndl = saturate(dot(nrm, l.direction));
                half3 diffuse = baseCol * (l.color * ndl * l.shadowAttenuation + SampleSH(nrm));
                float3 h = normalize(l.direction + v);
                half spec = pow(saturate(dot(nrm, h)), lerp(16, 160, _Wet)) * _Wet * 1.2 * l.shadowAttenuation;
                return half4(diffuse + spec * l.color, a);
            }
            ENDHLSL
        }
    }
}
