Shader "Blitz/SourceFreeBallPortable"
{
    Properties
    {
        _MainTex ("Original HEDSHU/HEDSHU2 authoring atlas", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            ZTest Less
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha, One Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4x4 _BallSourceMatrix;
            float4 _SourceRaster;
            float _BallWhite;
            float _BallAlpha;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varying { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varying vert(Input v)
            {
                Varying o;
                float4 cm = mul(_BallSourceMatrix, v.vertex);
                // Same active source664 projection as world/FX; Unity depth remains portable.
                float4 portableDepth = mul(UNITY_MATRIX_P, float4(0, 0, -cm.z, 1));
                o.position = float4(-2 * _SourceRaster.z * cm.x / _SourceRaster.x,
                    2 * _SourceRaster.z * _SourceRaster.w * cm.y / _SourceRaster.y, portableDepth.z, cm.z);
                o.uv = v.uv;
                return o;
            }
            float4 frag(Varying i) : SV_Target
            {
                float3 color = tex2D(_MainTex, i.uv).rgb;
                float alpha = _BallWhite > 0 ? 1 : _BallAlpha;
                return float4(_BallWhite > 0 ? float3(1, 1, 1) : color * alpha, alpha);
            }
            ENDCG
        }
    }
}
