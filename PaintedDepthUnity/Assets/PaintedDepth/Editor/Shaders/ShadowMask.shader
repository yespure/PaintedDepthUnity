Shader "Custom/ShadowMask"
{
    Properties
    {
        
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
           Name "ShadowMask"

           ZWrite Off
           ZTest Always
           Cull Off

           HLSLPROGRAM

           #pragma vertex Vert
           #pragma fragment Frag
           // 支持主光源阴影，以及级联阴影。
           #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

           // 软阴影相关变体。
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 Frag(Varyings input): SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);

                #if UNITY_REVERSED_Z
                bool isBackground = rawDepth <= 0.000001;
                #else
                bool isBackground = rawDepth >= 0.999999;
                #endif

                if(isBackground)
                return half4(1,1,1,0);

                float deviceDepth = rawDepth;

                //转为NDC深度
                #if !UNITY_REVERSED_Z
                deviceDepth = lerp(
                    UNITY_NEAR_CLIP_VALUE,
                    1.0,
                    rawDepth
                );
                #endif

                float3 positionWS = ComputeWorldSpacePosition(
                    uv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP
                );

                float4 shadowCoord = 
                TransformWorldToShadowCoord(positionWS);

                half shadow = MainLightRealtimeShadow(shadowCoord);

                half fade = GetMainLightShadowFade(positionWS);
                shadow = lerp(shadow, 1.0h, fade);

                return half4(shadow,shadow,shadow,1);
            }

            ENDHLSL

        }
    }
}
