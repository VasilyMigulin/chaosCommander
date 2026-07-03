// URP-Unlit «материализация» существа (dissolve-IN). _DissolveAmount: 0 = невидим, 1 = проявлен.
// MaterializeEffect подменяет материалы существа инстансами этого шейдера, копируя _BaseMap/_BaseColor,
// и гонит _DissolveAmount 0→1, затем возвращает оригиналы. Unlit — на короткую материализацию освещение
// не критично, зато RP-безопасно и работает с любым мешем (в т.ч. skinned).
Shader "URP/CreatureMaterialize"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _NoiseTex ("Noise", 2D) = "gray" {}
        _DissolveAmount ("Dissolve (0=hidden,1=shown)", Range(0,1)) = 0
        _EdgeWidth ("Edge Width", Range(0,0.3)) = 0.08
        [HDR] _EdgeColor ("Edge Color", Color) = (1.5, 0.8, 0.2, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Materialize"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _NoiseTex_ST;
                float4 _BaseColor;
                float4 _EdgeColor;
                float  _DissolveAmount;
                float  _EdgeWidth;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                           i.uv * _NoiseTex_ST.xy + _NoiseTex_ST.zw).r;

                // materialize-IN: чем больше amount, тем ниже порог → тем больше видно.
                float d = 1.0 - _DissolveAmount;
                clip(n - d);

                float edge = smoothstep(d, d + _EdgeWidth, n);
                c.rgb = lerp(_EdgeColor.rgb, c.rgb, edge);
                return c;
            }
            ENDHLSL
        }
    }
}
