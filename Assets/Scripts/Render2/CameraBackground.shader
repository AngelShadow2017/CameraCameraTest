Shader "Hidden/URP/CameraBackground"
{
    Properties
    {
        _SourceTex ("Source Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "FullScreenBackground"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP核心
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_Position;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                // 全屏三角形
                float2 uv = float2((v.vertexID << 1) & 2, v.vertexID & 2);
                o.uv = uv;
                o.positionHCS = float4(uv * 2.0 - 1.0, 0, 1);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // 如果需要处理平台Y翻转，可在材质侧控制或这里处理：
                 bool flipY = UNITY_UV_STARTS_AT_TOP; // 根据需要自行调整
                float2 uv = float2(i.uv.x, flipY ? 1.0 - i.uv.y : i.uv.y);
                //float2 uv = i.uv;
                half4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv);
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
