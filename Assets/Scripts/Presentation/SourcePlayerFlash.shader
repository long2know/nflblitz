Shader "Blitz/SourcePlayerFlashPortable"
{
    Properties { _MainTex ("Actual original player texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            ZWrite On
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct output { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            output vert(input v)
            {
                output o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
            }
            float4 frag(output i) : SV_Target
            {
                float4 texel = tex2D(_MainTex, i.uv);
                // Portable realization of the source's DECAL_TEXTURE_ADD_CCALPHA / 0xc0000000 state.
                return float4(saturate(texel.rgb + (192.0 / 255.0)), texel.a);
            }
            ENDCG
        }
    }
}
