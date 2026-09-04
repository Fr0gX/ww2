Shader "WW2/ArtV6 Unlit Color"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
            };

            struct VertexToFragment
            {
                float4 vertex : SV_POSITION;
            };

            fixed4 _Color;

            VertexToFragment Vert(AppData input)
            {
                VertexToFragment output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 Frag(VertexToFragment input) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }

    Fallback Off
}
