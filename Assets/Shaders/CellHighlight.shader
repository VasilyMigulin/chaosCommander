// URP-Unlit процедурная подсветка клетки. Текстуры не нужны — вся форма считается в шейдере
// (скруглённый квадрат через SDF), поэтому масштабируется без мыла и настраивается ползунками.
//
// ОДИН шейдер на два применения — разница только в меше:
//   • горизонтальный Quad НА клетке  → рамка + заливка, волна бежит ПО клетке;
//   • вертикальный Quad/Box вокруг клетки → V ползёт вверх, та же волна читается как ПОДЪЁМ,
//     а _TopFade гасит верхний край, чтобы «столб» растворялся, а не обрезался.
//
// Блендинг вынесен в свойства (_SrcBlend/_DstBlend): по умолчанию One/One — чистый аддитив, как
// у партикл-свечения. Для тёмного фона/светлой доски можно переключить на One/OneMinusSrcAlpha
// (обычная альфа) прямо в инспекторе материала — RGB уже премультиплены на альфу.
Shader "URP/CellHighlight"
{
    Properties
    {
        [HDR] _Color ("Цвет", Color) = (0.25, 0.85, 1, 1)
        _Alpha ("Общая яркость", Range(0, 4)) = 1

        [Header(Frame)]
        _Inset         ("Отступ рамки от края", Range(0, 0.4)) = 0.05
        _Corner        ("Скругление углов", Range(0, 0.5)) = 0.12
        _FrameWidth    ("Толщина рамки", Range(0.001, 0.5)) = 0.02
        _FrameSoft     ("Мягкость рамки", Range(0.001, 0.5)) = 0.06
        _FrameStrength ("Яркость рамки", Range(0, 4)) = 1.6

        [Header(Fill)]
        _FillStrength ("Яркость заливки", Range(0, 2)) = 0.15
        _FillSoft     ("Мягкость заливки внутрь", Range(0.001, 1)) = 0.3

        [Header(Wave)]
        _WaveStrength ("Яркость волны", Range(0, 4)) = 1.2
        _WaveWidth    ("Ширина волны", Range(0.01, 1)) = 0.16
        _WaveSpeed    ("Скорость волны (проходов в сек)", Range(0, 4)) = 0.5
        _WaveDir      ("Направление волны (xy в UV)", Vector) = (0, 1, 0, 0)

        [Header(Pulse)]
        _Pulse      ("Амплитуда пульса", Range(0, 1)) = 0.1
        _PulseSpeed ("Скорость пульса", Range(0, 8)) = 1.2

        _TopFade ("Затухание к верху (для вертикальных мешей)", Range(0, 1)) = 0

        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "CellHighlight"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            // Клетка-декаль лежит вплотную к меху доски — без сдвига по глубине они z-файтятся.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _WaveDir;
                float  _Alpha;
                float  _Inset;
                float  _Corner;
                float  _FrameWidth;
                float  _FrameSoft;
                float  _FrameStrength;
                float  _FillStrength;
                float  _FillSoft;
                float  _WaveStrength;
                float  _WaveWidth;
                float  _WaveSpeed;
                float  _Pulse;
                float  _PulseSpeed;
                float  _TopFade;
                float  _SrcBlend;
                float  _DstBlend;
            CBUFFER_END

            // Знаковое расстояние до скруглённого прямоугольника: <0 внутри, 0 на контуре, >0 снаружи.
            // Именно из него получаются И рамка (|d| мал), И заливка (d<0) — одной формулой, без текстур.
            float SdRoundBox(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p  = uv - 0.5;

                float  edge     = 0.5 - _Inset;
                float2 halfSize = float2(edge, edge);
                float  radius   = min(_Corner, min(halfSize.x, halfSize.y));
                float  d        = SdRoundBox(p, halfSize, radius);

                // Рамка: свечение вокруг контура d=0, симметрично наружу и внутрь.
                float frame = 1.0 - smoothstep(_FrameWidth, _FrameWidth + _FrameSoft, abs(d));

                // Заливка — НЕ по d, а по суперэллипсу. Внутри прямоугольного SDF расстояние по сути
                // чебышевское (max(|x|,|y|)), и его изолинии дают диагональные складки — заливка читалась
                // как «пирамидка» с рёбрами из углов. Суперэллипс даёт гладкий градиент без рёбер.
                float2 an = abs(p) / max(edge, 1e-4);
                float  se = pow(pow(an.x, 4.0) + pow(an.y, 4.0), 0.25);   // 1 на контуре, 0 в центре
                float  softN = saturate(_FillSoft / max(edge, 1e-4));
                float  inner = 1.0 - smoothstep(1.0 - softN, 1.0, se);

                // Бегущая волна. Диапазон шире [0,1], чтобы полоса успевала ПОЛНОСТЬЮ уйти за край
                // до перезапуска — иначе на стыке цикла виден щелчок.
                float2 dir = _WaveDir.xy;
                dir = (dot(dir, dir) < 1e-6) ? float2(0.0, 1.0) : normalize(dir);
                float coord = dot(p, dir) + 0.5;
                float t     = lerp(-0.25, 1.25, frac(_Time.y * _WaveSpeed));
                // Гауссова полоса. Квадрат — умножением, а НЕ pow(x, 2.0): pow считается через
                // exp(y*log(x)) и на отрицательном основании даёт NaN, то есть волна пропала бы
                // на половине клетки (там, где coord < t).
                float k     = (coord - t) / max(_WaveWidth, 1e-4);
                float band  = exp(-k * k);

                float area = saturate(inner + frame);   // маска «это клетка» — волна не вылезает наружу

                float a = frame * _FrameStrength
                        + inner * _FillStrength
                        + band  * _WaveStrength * area;

                a *= 1.0 + _Pulse * sin(_Time.y * _PulseSpeed * 6.2831853);

                // Затухание к верху: нужно только вертикальным мешам. При _TopFade≈0 порог уезжает
                // на самый край и множитель фактически равен 1 — ветвление не нужно.
                a *= 1.0 - smoothstep(1.0 - max(_TopFade, 1e-4), 1.0, uv.y);

                a = max(a, 0.0) * _Alpha * _Color.a;

                // Премультиплаед: rgb уже умножены на альфу → работает и One/One (аддитив),
                // и One/OneMinusSrcAlpha (обычная прозрачность) без правки шейдера.
                return half4(_Color.rgb * a, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
