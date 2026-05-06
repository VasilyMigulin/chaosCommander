Shader "AwesomeUI/CardHighlight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // Цвет обводки
        _RimColor        ("Rim Color",         Color)           = (1, 0.85, 0.2, 1)

        // Доля UV от края до внутренней границы карты (вычисляется из C#)
        _BorderFractionX ("Border Fraction X", Range(0.0, 0.5)) = 0.08
        _BorderFractionY ("Border Fraction Y", Range(0.0, 0.5)) = 0.06

        // Мягкость края (fade внутрь)
        _RimSoftness     ("Rim Softness",      Range(0.001, 0.15)) = 0.03

        // Интенсивность свечения
        _GlowIntensity   ("Glow Intensity",    Range(0.0, 4.0))  = 1.6

        // Пульсация
        _PulseSpeed      ("Pulse Speed",       Range(0.0, 8.0))  = 2.0
        _PulseDepth      ("Pulse Depth",       Range(0.0, 1.0))  = 0.45

        // Shimmer — скользящий блик
        _ShimmerSpeed    ("Shimmer Speed",     Range(0.0, 4.0))  = 0.8
        _ShimmerWidth    ("Shimmer Width",     Range(0.0, 1.0))  = 0.18
        _ShimmerIntensity("Shimmer Intensity", Range(0.0, 2.0))  = 0.75

        // Стандартные UI
        _Color           ("Tint",              Color)            = (1,1,1,1)
        _StencilComp     ("Stencil Comparison",Float)            = 8
        _Stencil         ("Stencil ID",        Float)            = 0
        _StencilOp       ("Stencil Operation", Float)            = 0
        _StencilWriteMask("Stencil Write Mask",Float)            = 255
        _StencilReadMask ("Stencil Read Mask", Float)            = 255
        _ColorMask       ("Color Mask",        Float)            = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
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
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   2.0

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
            float4    _MainTex_ST;

            fixed4 _RimColor;
            float  _BorderFractionX;
            float  _BorderFractionY;
            float  _RimSoftness;
            float  _GlowIntensity;
            float  _PulseSpeed;
            float  _PulseDepth;
            float  _ShimmerSpeed;
            float  _ShimmerWidth;
            float  _ShimmerIntensity;
            fixed4 _Color;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = v.vertex;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = TRANSFORM_TEX(v.uv, _MainTex);
                o.color    = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── UI clip ───────────────────────────────────────────────────
                float clip = UnityGet2DClipping(i.worldPos.xy, _ClipRect);

                // ── Граница внутреннего прямоугольника (= карта) ──────────────
                // Пиксели внутри [fracX .. 1-fracX] x [fracY .. 1-fracY] — это
                // область самой карты, здесь обводка полностью прозрачна.
                float2 innerMin = float2(_BorderFractionX, _BorderFractionY);
                float2 innerMax = 1.0 - innerMin;

                // Расстояние до ближайшей стенки inner-rect (>0 = снаружи = в полосе обводки)
                float2 distToInner = max(innerMin - i.uv, i.uv - innerMax);
                float  borderDist  = max(distToInner.x, distToInner.y);

                // rim: 1 в зоне обводки, плавно 0 на внутренней границе
                float rim = smoothstep(-_RimSoftness, 0.0, borderDist);

                // ── Пульсация ─────────────────────────────────────────────────
                float pulse = 1.0 - _PulseDepth * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed));

                // ── Shimmer ───────────────────────────────────────────────────
                float diag    = i.uv.x + i.uv.y;
                float shimPos = frac(_Time.y * _ShimmerSpeed * 0.5);
                float shimmer = smoothstep(_ShimmerWidth, 0.0, abs(diag * 0.5 - shimPos))
                                * _ShimmerIntensity * rim;

                // ── Затухание к внешнему краю (мягкое glow) ──────────────────
                // Нормализуем расстояние: 0 на внутр. границе, 1 на краю квада
                float maxBorder = max(_BorderFractionX, _BorderFractionY);
                float rimNorm   = saturate(borderDist / maxBorder);
                float outerFade = 1.0 - rimNorm * rimNorm; // яркость спадает к краю

                // ── Итог ──────────────────────────────────────────────────────
                float3 glowColor = _RimColor.rgb * _GlowIntensity * pulse * outerFade
                                 + _RimColor.rgb * shimmer;
                float  alpha     = rim * _RimColor.a * outerFade * clip;

                return fixed4(glowColor, alpha);
            }
            ENDCG
        }
    }
}

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // Цвет обводки (задаётся из кода / инспектора)
        _RimColor       ("Rim Color",        Color)  = (1, 0.85, 0.2, 1)

        // Толщина обводки (0..1 в UV-пространстве)
        _RimWidth       ("Rim Width",        Range(0.0, 0.25)) = 0.06

        // Мягкость края обводки
        _RimSoftness    ("Rim Softness",     Range(0.001, 0.1)) = 0.02

        // Интенсивность базового свечения
        _GlowIntensity  ("Glow Intensity",   Range(0.0, 3.0))   = 1.4

        // Скорость пульсации
        _PulseSpeed     ("Pulse Speed",      Range(0.0, 8.0))   = 2.0

        // Глубина пульсации (0 = нет, 1 = полная)
        _PulseDepth     ("Pulse Depth",      Range(0.0, 1.0))   = 0.4

        // Shimmer — скользящий блик
        _ShimmerSpeed   ("Shimmer Speed",    Range(0.0, 4.0))   = 0.8
        _ShimmerWidth   ("Shimmer Width",    Range(0.0, 1.0))   = 0.18
        _ShimmerIntensity("Shimmer Intensity",Range(0.0, 2.0))  = 0.7

        // Стандартные UI-параметры
        _Color          ("Tint",             Color)  = (1,1,1,1)
        _StencilComp    ("Stencil Comparison", Float) = 8
        _Stencil        ("Stencil ID",         Float) = 0
        _StencilOp      ("Stencil Operation",  Float) = 0
        _StencilWriteMask("Stencil Write Mask",Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask      ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType"      = "Transparent"
            "PreviewType"     = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref   [_Stencil]
            Comp  [_StencilComp]
            Pass  [_StencilOp]
            ReadMask  [_StencilReadMask]
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
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 uv       : TEXCOORD0;
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
            float4    _MainTex_ST;

            fixed4  _RimColor;
            float   _RimWidth;
            float   _RimSoftness;
            float   _GlowIntensity;
            float   _PulseSpeed;
            float   _PulseDepth;
            float   _ShimmerSpeed;
            float   _ShimmerWidth;
            float   _ShimmerIntensity;
            fixed4  _Color;
            float4  _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = v.vertex;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = TRANSFORM_TEX(v.uv, _MainTex);
                o.color    = v.color * _Color;
                return o;
            }

            // Расстояние от UV до ближайшего края [0..1] (0 = центр, 1 = угол)
            float edgeDist(float2 uv)
            {
                float2 d = min(uv, 1.0 - uv);   // дистанция до каждого края
                return min(d.x, d.y);            // ближайший край
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── Базовый спрайт ────────────────────────────────────────────
                fixed4 sprite = tex2D(_MainTex, i.uv) * i.color;

                // UI clip rect
                sprite.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                clip(sprite.a - 0.001);

                // ── Обводка ───────────────────────────────────────────────────
                float dist  = edgeDist(i.uv);
                float rim   = smoothstep(_RimWidth, _RimWidth - _RimSoftness, dist);

                // ── Пульсация ─────────────────────────────────────────────────
                float pulse = 1.0 - _PulseDepth * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed));

                // ── Shimmer (скользящий блик по диагонали) ────────────────────
                float diag    = i.uv.x + i.uv.y;                          // 0..2
                float shimPos = frac(_Time.y * _ShimmerSpeed * 0.5);      // 0..1 зацикленно
                float shimmer = smoothstep(_ShimmerWidth, 0.0,
                                    abs(diag * 0.5 - shimPos))            // ширина блика
                                * _ShimmerIntensity;

                // ── Итоговый цвет обводки ─────────────────────────────────────
                float3 rimFinal = _RimColor.rgb * _GlowIntensity * pulse
                                + _RimColor.rgb * shimmer * rim;

                // Смешиваем: обводка накладывается поверх спрайта
                fixed3 col = lerp(sprite.rgb, rimFinal, rim * _RimColor.a);
                fixed  a   = max(sprite.a, rim * _RimColor.a);

                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
