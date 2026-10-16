Shader "Hidden/Blitz/SourceHud"
{
    Properties { _MainTex ("Original interface texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            sampler2D _MainTex;
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target { return tex2D(_MainTex, input.uv) * input.color; }
            ENDCG
        }
    }
}
