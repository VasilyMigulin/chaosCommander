// UI dissolve для карты. Растворяет спрайт по шуму (_NoiseTex) с подсветкой края.
// _DissolveAmount: 0 = карта целая, 1 = полностью растворилась. Драйвер — CardDissolveDriver.
// Совместим с UGUI (Canvas рендерит своим UI-пайплайном и в URP тоже).
Shader "UI/CardDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _NoiseTex ("Noise", 2D) = "gray" {}
        _DissolveAmount ("Dissolve", Range(0,1)) = 0
        _EdgeWidth ("Edge Width", Range(0,0.3)) = 0.08
        [HDR] _EdgeColor ("Edge Color", Color) = (1,0.6,0.1,1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True"
        }
        Cull Off  Lighting Off  ZWrite Off  ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;  float4 _MainTex_ST;
            sampler2D _NoiseTex; float4 _NoiseTex_ST;
            fixed4 _Color; float _DissolveAmount; float _EdgeWidth; fixed4 _EdgeColor;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float  n = tex2D(_NoiseTex, TRANSFORM_TEX(i.uv, _NoiseTex)).r;
                float  d = _DissolveAmount;

                clip(n - d);                                   // растворяем по шуму
                float edge = smoothstep(d, d + _EdgeWidth, n); // край
                c.rgb = lerp(_EdgeColor.rgb, c.rgb, edge);
                return c;
            }
            ENDCG
        }
    }
}
