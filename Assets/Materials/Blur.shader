Shader "Custom/Blur"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _BlurSize ("Blur Size", Float) = 1.0
        _Direction ("Blur Direction", Vector) = (1,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // x = 1/width, y = 1/height
            float _BlurSize;
            float4 _Direction; // (x, y) direction

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 col = fixed4(0,0,0,0);
                // Sample the center
                col += tex2D(_MainTex, i.uv) * 0.2941176471;
                // Sample in one direction
                col += tex2D(_MainTex, i.uv + _Direction.xy * _BlurSize * _MainTex_TexelSize.xy) * 0.3529411765;
                // Sample in the opposite direction
                col += tex2D(_MainTex, i.uv - _Direction.xy * _BlurSize * _MainTex_TexelSize.xy) * 0.3529411765;
                return col;
            }
            ENDCG
        }
    }
}