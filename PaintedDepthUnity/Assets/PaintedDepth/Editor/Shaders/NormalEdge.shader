Shader "Custom/NormalEdge"
{
    Properties
    {
        _Thickness("Sample Radius (Pixels)", Range(1, 4)) = 1
        _Threshold("Edge Threshold", Range(0.001, 1)) = 0.15
        _Softness("Edge Softness", Range(0.001, 0.3)) = 0.03
        [Toggle] _Invert("White Lines On Black", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "NormalEdge"

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM

            // 注意大小写。
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Thickness;
                float _Threshold;
                float _Softness;
                float _Invert;
            CBUFFER_END

            float3 ReadNormal(float2 uv)
            {
                // 避免采样越过画面边界。
                float2 halfPixel = 0.5 / _ScaledScreenParams.xy;
                uv = clamp(uv, halfPixel, 1.0 - halfPixel);

                float3 normalWS = SampleSceneNormals(uv);

                // 避免零向量归一化产生无效值。
                return normalWS * rsqrt(
                    max(dot(normalWS, normalWS), 0.000001)
                );
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 offset = _Thickness / _ScaledScreenParams.xy;

                float3 center = ReadNormal(uv);
                float difference = 0.0;

                // 比较周围八个方向。
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float3 neighbor = ReadNormal(
                            uv + float2(x, y) * offset
                        );

                        difference = max(
                            difference,
                            length(center - neighbor)
                        );
                    }
                }

                // 以下代码必须仍然位于 Frag 内部。
                float edge = smoothstep(
                    _Threshold,
                    _Threshold + max(_Softness, 0.0001),
                    difference
                );

                float value = lerp(
                    1.0 - edge,
                    edge,
                    saturate(_Invert)
                );

                return float4(value, value, value, 1.0);
            }

            ENDHLSL
        }
    }
}