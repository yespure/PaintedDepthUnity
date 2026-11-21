Shader "Custom/LightContribution"
{
    Properties
    {
        _Strength("Output Strength", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "LightContribution"

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM

            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
            CBUFFER_END

            float3 EvaluateLight(
                Light light,
                float3 normalWS)
            {
                float lambert = saturate(
                    dot(normalWS, light.direction)
                );

                return
                    light.color *
                    light.distanceAttenuation *
                    light.shadowAttenuation *
                    lambert;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 1. 读取当前相机的深度。
                float rawDepth = SampleSceneDepth(uv);

                // 没有场景几何的背景输出黑色。
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001)
                        return float4(0, 0, 0, 1);
                #else
                    if (rawDepth >= 0.999999)
                        return float4(0, 0, 0, 1);
                #endif

                // 2. 把设备深度转成用于重建位置的 NDC 深度。
                float deviceDepth = rawDepth;

                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(
                        UNITY_NEAR_CLIP_VALUE,
                        1.0,
                        rawDepth
                    );
                #endif

                // 3. 还原这个像素对应的世界位置。
                float3 positionWS = ComputeWorldSpacePosition(
                    uv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP
                );

                // 4. 读取世界空间法线。
                // SampleSceneNormals 已经返回解码后的法线，
                // 这里不需要再执行 * 2 - 1。
                float3 normalWS = SampleSceneNormals(uv);

                normalWS *= rsqrt(
                    max(dot(normalWS, normalWS), 0.000001)
                );

                float3 contribution = float3(0, 0, 0);

                // 5. 主方向光及其阴影。
                float4 shadowCoord;

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    shadowCoord = float4(uv, 0, 1);
                #else
                    shadowCoord =
                        TransformWorldToShadowCoord(positionWS);
                #endif

                Light mainLight = GetMainLight(shadowCoord);

                // 全屏绘制不使用模型自己的光源剔除标记。
                mainLight.distanceAttenuation = 1.0;

                float mainShadowFade =
                    GetMainLightShadowFade(positionWS);

                mainLight.shadowAttenuation = lerp(
                    mainLight.shadowAttenuation,
                    1.0,
                    mainShadowFade
                );

                contribution += EvaluateLight(
                    mainLight,
                    normalWS
                );

                // URP 17.3 的 Forward+ 光源循环。
#if USE_CLUSTER_LIGHT_LOOP

    // LIGHT_LOOP_BEGIN 要求变量名称必须是 inputData。
    InputData inputData = (InputData)0;

    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS =
        GetWorldSpaceNormalizeViewDir(positionWS);

    inputData.normalizedScreenSpaceUV = uv;

    // 其他方向光。主方向光已在前面计算。
    uint directionalCount = min(
        URP_FP_DIRECTIONAL_LIGHTS_COUNT,
        (uint)MAX_VISIBLE_LIGHTS
    );

    [loop]
    for (uint i = 0; i < directionalCount; i++)
    {
        Light light = GetAdditionalLight(
            i,
            positionWS,
            half4(1, 1, 1, 1)
        );

        contribution += EvaluateLight(light, normalWS);
    }

    // 点光源与聚光灯。
    // Forward+ 中由宏查询当前像素对应的灯光列表，
    // 此时 GetAdditionalLightsCount() 返回 0 是正常的。
    uint pixelLightCount = GetAdditionalLightsCount();

    LIGHT_LOOP_BEGIN(pixelLightCount)

        Light light = GetAdditionalLight(
            lightIndex,
            positionWS,
            half4(1, 1, 1, 1)
        );

        contribution += EvaluateLight(light, normalWS);

    LIGHT_LOOP_END

#endif

                // 不做 saturate，保留超过 1 的光照。
                return float4(contribution * _Strength, 1);
            }

            ENDHLSL
        }
    }
}