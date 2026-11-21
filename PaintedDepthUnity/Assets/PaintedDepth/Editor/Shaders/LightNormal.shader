Shader "Custom/LightNormal"
{


    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "LightNormal"

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

           

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

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
                return half4(0,0,0,0);

                float3 normalWS = SampleSceneNormals(uv);
                float lengthSquared = dot(normalWS, normalWS);

                if(lengthSquared < 0.000001)
                return half4(0,0,0,0);

                normalWS *= rsqrt(lengthSquared);

                float3 encodedNormal = normalWS * 0.5 + 0.5;

                return half4(encodedNormal, 1);
            }
            ENDHLSL
        }
    }
}
