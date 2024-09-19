Shader "Custom/AverageColorShader"
{
    Properties
    {
        _RgbTex ("Input RGB Texture", 2D) = "white" {}
        _DepthArray ("Depth Array", Float) = 0
        _BackgroundDepthThreshold ("Background Depth Threshold", Float) = 0.95
        _DepthWidth ("Depth Width", Int) = 1
        _DepthHeight ("Depth Height", Int) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
// Upgrade NOTE: excluded shader from DX11, OpenGL ES 2.0 because it uses unsized arrays
#pragma exclude_renderers d3d11 gles
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _RgbTex;
            float _BackgroundDepthThreshold;
            float _DepthWidth;
            float _DepthHeight;
            float _DepthArray[];

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float3 color = tex2D(_RgbTex, uv).rgb;

                // Calculate the depth index
                int x = (int)(uv.x * _DepthWidth);
                int y = (int)(uv.y * _DepthHeight);
                int index = y * (int)_DepthWidth + x;

                // Get the depth from the array
                float depth = _DepthArray[index];

                // Check if the depth is greater than or equal to the threshold
                if (depth < _BackgroundDepthThreshold)
                {
                    // Ignore this pixel by setting alpha to 0
                    return float4(0, 0, 0, 0);
                }

                // Valid pixel with alpha = 1
                return float4(color, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
