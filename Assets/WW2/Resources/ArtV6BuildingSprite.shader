Shader "WW2/ArtV6 Grounded Building Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest+20"
            "IgnoreProjector" = "True"
            "RenderType" = "TransparentCutout"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite On
        ZTest LEqual
        Offset -1, -4
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct VertexToFragment
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            VertexToFragment Vert(AppData input)
            {
                VertexToFragment output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                // Resolve the billboard against the map in depth only. Unlike a
                // Transform offset, this cannot separate its visible base from
                // the contact silhouette on screen.
                #if defined(UNITY_REVERSED_Z)
                    output.vertex.z += 0.00008 * output.vertex.w;
                #else
                    output.vertex.z -= 0.00008 * output.vertex.w;
                #endif
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(VertexToFragment input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * input.color;
                clip(color.a - 0.02);
                return color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
