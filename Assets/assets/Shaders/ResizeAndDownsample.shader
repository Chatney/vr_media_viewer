Shader "Custom/ResizeAndDownsample"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
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
            float4 _TexelSize;
            float _DownsampleFactor;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                
                if (_DownsampleFactor > 1)
                {
                    // Adjust UV to sample center of downsampled region
                    float2 adjustment = (_DownsampleFactor - 1) * _TexelSize.xy * 0.5;
                    uv += adjustment;
                    
                    // Downsample by averaging
                    fixed4 col = 0;
                    for (int y = 0; y < _DownsampleFactor; y++)
                    {
                        for (int x = 0; x < _DownsampleFactor; x++)
                        {
                            float2 offset = float2(x, y) * _TexelSize.xy;
                            col += tex2D(_MainTex, uv + offset);
                        }
                    }
                    col /= (_DownsampleFactor * _DownsampleFactor);
                    return col;
                }
                else
                {
                    // No downsampling needed, just sample directly
                    return tex2D(_MainTex, uv);
                }
            }
            ENDCG
        }
    }
}