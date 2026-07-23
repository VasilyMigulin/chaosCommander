Shader "AwesomeUI/CardHighlight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // Цвет обводки
        _RimColor ("Rim Color", Color) = (1, 0.85, 0.2, 1)

        // Толщина рамки относительно UV
        _BorderFractionX ("Border Fraction X", Range(0.0, 0.5)) = 0.08
        _BorderFractionY ("Border Fraction Y", Range(0.0, 0.5)) = 0.06

        // Мягкость внутреннего края
        _RimSoftness ("Rim Softness", Range(0.001, 0.15)) = 0.03

        // Интенсивность свечения
        _GlowIntensity ("Glow Intensity", Range(0.0, 4.0)) = 1.6

        // «Тело» рамки: насколько долго свечение держит яркость, прежде чем утонуть к внешнему краю.
        // Больше = толще/плотнее рамка (2 — старая квадратичная кривая, 3-4 — жирная как в ХС).
        _OuterBody ("Outer Body (толщина тела)", Range(1.0, 6.0)) = 3.0

        // Пульсация
        _PulseSpeed ("Pulse Speed", Range(0.0, 8.0)) = 2.0
        _PulseDepth ("Pulse Depth", Range(0.0, 1.0)) = 0.45

        // Блик
        _ShimmerSpeed ("Shimmer Speed", Range(0.0, 4.0)) = 0.8
        _ShimmerWidth ("Shimmer Width", Range(0.0, 1.0)) = 0.18
        _ShimmerIntensity ("Shimmer Intensity", Range(0.0, 2.0)) = 0.75

        // Волнистый «живой» край (как в ХС): шум смещает границу рамки. "gray" по умолчанию = без волн,
        // если текстура не назначена. Текстура должна тайлиться (Wrap = Repeat).
        _NoiseTex ("Noise (R, tileable)", 2D) = "gray" {}
        _NoiseScale ("Noise Scale", Range(0.5, 8.0)) = 2.0
        _NoiseStrength ("Noise Strength (waviness)", Range(0.0, 0.2)) = 0.06
        _NoiseScrollSpeed ("Noise Scroll Speed", Range(0.0, 2.0)) = 0.35

        // UI
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]

        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CardHighlight"

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            fixed4 _RimColor;

            float _BorderFractionX;
            float _BorderFractionY;

            float _RimSoftness;

            float _GlowIntensity;
            float _OuterBody;

            float _PulseSpeed;
            float _PulseDepth;

            float _ShimmerSpeed;
            float _ShimmerWidth;
            float _ShimmerIntensity;

            sampler2D _NoiseTex;
            float _NoiseScale;
            float _NoiseStrength;
            float _NoiseScrollSpeed;

            fixed4 _Color;

            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);

                o.worldPos = v.vertex;

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                o.color = v.color * _Color;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Базовый спрайт
                fixed4 sprite = tex2D(_MainTex, i.uv) * i.color;

                // UI clipping
                float clipMask = UnityGet2DClipping(i.worldPos.xy, _ClipRect);

                sprite.a *= clipMask;

                clip(sprite.a - 0.001);

                // Внутренний прямоугольник
                float2 innerMin = float2(_BorderFractionX, _BorderFractionY);
                float2 innerMax = float2(1.0, 1.0) - innerMin;

                // Расстояние до внутренней области
                float2 distToInner = max(innerMin - i.uv, i.uv - innerMax);

                // Нормируем ПО-ОСЕВО: band = 0 на границе карты, 1 у края квада ПО ОБЕИМ осям.
                // Раньше борта нормировались одним общим max-фракшном в UV: у карты 5:9 боковая доля
                // (padding/width) больше верхней (padding/height) → бока выцветали «быстрее в пикселях»
                // и рамка по бокам выглядела заметно тоньше, чем сверху/снизу.
                float maxBorder = max(max(_BorderFractionX, _BorderFractionY), 1e-4);
                float nx = distToInner.x / max(_BorderFractionX, 1e-4);
                float ny = distToInner.y / max(_BorderFractionY, 1e-4);
                float band = max(nx, ny);

                // «Живой» огонь как в ХС: две октавы тайлящегося шума ползут в РАЗНЫХ направлениях —
                // их сумма никогда не выглядит статичной или зацикленной. wave колышет ГРАНИЦУ рамки
                // (волнистый край), boil дальше модулирует ЯРКОСТЬ свечения (кипение пламени).
                float2 t = _Time.y * _NoiseScrollSpeed * float2(1.0, 1.0);
                float n1 = tex2D(_NoiseTex, i.uv * _NoiseScale        + t * float2( 0.7, -0.4)).r;
                float n2 = tex2D(_NoiseTex, i.uv * _NoiseScale * 1.7  - t * float2( 0.4,  0.6)).r;
                float boil = (n1 + n2) * 0.5;                       // 0..1, медленно «кипит»
                float wave = (boil - 0.5) * _NoiseStrength;         // смещение границы (± в UV)

                // Волну применяем ТОЛЬКО к внешней части (bandWaved → outerFade): внутренняя кромка
                // остаётся ровной по краю карты, свечение внутрь карты НЕ заползает — начинается от
                // края и разгорается наружу (rim по НЕволнованному band, от 0 и наружу).
                float bandWaved = band + wave / maxBorder;

                float rim = smoothstep(0.0, max(_RimSoftness / maxBorder, 1e-3), band);

                // Пульсация
                float pulse =
                    1.0 -
                    _PulseDepth *
                    (0.5 + 0.5 * sin(_Time.y * _PulseSpeed));

                // Shimmer
                float diag = i.uv.x + i.uv.y;

                float shimmerPos =
                    frac(_Time.y * _ShimmerSpeed * 0.5);

                float shimmer =
                    smoothstep(
                        _ShimmerWidth,
                        0.0,
                        abs(diag * 0.5 - shimmerPos)
                    )
                    * _ShimmerIntensity
                    * rim;

                // Затухание к внешнему краю — по ВОЛНОВАННОМУ band (языки пламени наружу).
                // _OuterBody держит яркость тела: 2 — старая квадратичная кривая, больше — толще рамка.
                float rimNorm = saturate(bandWaved);

                float outerFade =
                    1.0 - pow(rimNorm, _OuterBody);

                // Кипение яркости: шум локально разгоняет/гасит свечение (0.7..1.3), у самой карты
                // (outerFade→1) держим огонь горячим, к внешнему краю даём шуму рвать его на языки.
                float flame = lerp(1.0, 0.7 + 0.6 * boil, saturate(rimNorm * 1.5));

                // Страховка у среза квада: волна не должна упираться в прямой край Image —
                // гасим альфу в последних % UV, чтобы силуэт всегда завершался сам, а не обрезался.
                float2 edge2 = min(i.uv, 1.0 - i.uv);
                float edgeGuard = smoothstep(0.0, 0.04, min(edge2.x, edge2.y));

                // Glow
                float3 glowColor =
                    _RimColor.rgb *
                    _GlowIntensity *
                    pulse *
                    outerFade *
                    flame;

                glowColor +=
                    _RimColor.rgb *
                    shimmer;

                // Тело рамки — спрайт, ТИНТОВАННЫЙ вторым статусом (i.color = vertexColor * _Color),
                // тоже гаснет к краю; свечение (_RimColor) — поверх. АЛЬФА строится ТОЛЬКО из масок
                // (rim × outerFade × flame × edgeGuard) — все они прошиты шумом, поэтому силуэт живой
                // и волнистый. Спрайт участвует лишь МНОЖИТЕЛЕМ (маска формы, если назначен фигурный;
                // белый дефолт = no-op) — раньше max(sprite.a, …) держал непрозрачным весь квад,
                // и внешний край оставался прямым прямоугольником.
                fixed3 finalColor =
                    sprite.rgb * outerFade + glowColor * rim;

                float finalAlpha =
                    rim * _RimColor.a * outerFade * flame * edgeGuard * sprite.a;

                return fixed4(finalColor, finalAlpha);
            }

            ENDCG
        }
    }
}