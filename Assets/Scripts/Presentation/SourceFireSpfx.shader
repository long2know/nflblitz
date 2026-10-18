Shader "Blitz/SourceFireSpfxPortable"
{
    Properties
    {
        _MainTex ("Authentic original TRAIL authoring frame", 2D) = "black" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Explicit inherited cull", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite On
            ZTest Always
            Cull [_Cull]
            Blend SrcAlpha OneMinusSrcAlpha, SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _SourceRaster;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varying { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varying vert(Input v)
            {
                Varying o;
                // Source camera-space cm and664 projection. Depth is explicitly portable Unity, not native WBUFFER.
                float depth = v.vertex.z;
                float4 portableDepth = mul(UNITY_MATRIX_P, float4(0, 0, -depth, 1));
                o.position = float4(-2 * _SourceRaster.z * v.vertex.x / _SourceRaster.x,
                    2 * _SourceRaster.z * _SourceRaster.w * v.vertex.y / _SourceRaster.y, portableDepth.z, depth);
                o.uv = v.uv;
                return o;
            }
            float4 frag(Varying i) : SV_Target
            {
                float4 texel = tex2D(_MainTex, i.uv);
                return float4(texel.rgb * texel.a, texel.a);
            }
            ENDCG
        }
    }
}
