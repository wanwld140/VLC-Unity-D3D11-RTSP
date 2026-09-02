Shader "Hidden/VlcD3D11Rtsp/HikvisionYv12"
{
    Properties
    {
        [PerRendererData] _MainTex ("Y", 2D) = "black" {}
        _UTex ("U", 2D) = "gray" {}
        _VTex ("V", 2D) = "gray" {}
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

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
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            sampler2D _UTex;
            sampler2D _VTex;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // PlayM4 的 T_YV12 是连续 Y/V/U 4:2:0；按视频范围 BT.601 转换。
                float y = 1.16438356 * (tex2D(_MainTex, input.uv).r - 0.0625);
                float u = tex2D(_UTex, input.uv).r - 0.5;
                float v = tex2D(_VTex, input.uv).r - 0.5;
                float3 rgb;
                rgb.r = y + 1.59602678 * v;
                rgb.g = y - 0.39176229 * u - 0.81296764 * v;
                rgb.b = y + 2.01723214 * u;
                return fixed4(saturate(rgb), 1.0) * input.color;
            }
            ENDCG
        }
    }
}
