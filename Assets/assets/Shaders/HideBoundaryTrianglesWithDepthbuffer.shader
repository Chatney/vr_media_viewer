Shader "Custom/HideBoundaryTrianglesWithDepthbuffer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Threshold ("Threshold", Float) = 1.0
        _Softness ("Softness", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha // Enable alpha blending
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

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

            sampler2D _MainTex;
            StructuredBuffer<float> _DepthBuffer;
            int _TextureWidth;
            int _TextureHeight;
            float _Threshold;
            float _Softness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float GetDepthValue(int x, int y)
            {
                if (x < 0 || x >= _TextureWidth || y < 0 || y >= _TextureHeight)
                    return 0.0;

                int index = y * _TextureWidth + x;
                return _DepthBuffer[index];
            }

            half4 frag (v2f i) : SV_Target
            {
                int x = (int)(i.uv.x * _TextureWidth);
                int y = (int)((1.0 - i.uv.y) * _TextureHeight); // Flip y

                float depth = GetDepthValue(x, y);

                // Get neighboring depth values (right and up)
                float depthRight = GetDepthValue(x + 1, y);
                float depthUp = GetDepthValue(x, y + 1);

                // Calculate maximum difference
                float maxDifference = max(abs(depth - depthRight), abs(depth - depthUp));

                float threshold = _Threshold * 0.0039215686; // equivalent to _Threshold / 255.0
                float softness = _Softness * 0.0039215686; // equivalent to _Softness / 255.0

                // Compute alpha value based on the difference
                float alpha = 1.0 - smoothstep(threshold, threshold + softness, maxDifference);

                // Check if the pixel is on the outer edges and make it fully transparent
                if (i.uv.x <= (1.0 / _TextureWidth) || i.uv.x >= (1.0 - (1.0 / _TextureWidth)) ||
                    i.uv.y <= (1.0 / _TextureHeight) || i.uv.y >= (1.0 - (1.0 / _TextureHeight)))
                {
                    alpha = 0.0;
                }

                half4 color = tex2D(_MainTex, i.uv);
                color.a *= alpha; // Apply the computed alpha value
                return color;
            }
            ENDCG
        }
    }
}
