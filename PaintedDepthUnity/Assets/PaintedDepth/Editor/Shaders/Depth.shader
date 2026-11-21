Shader "Custom/Depth"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
    }

    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline" }

        Pass
        {
           Name "Depth"

           ZWrite off//不写深度
           ZTest Always//深度检测全屏写入
           Cull off//关背面剔除

           HLSLPROGRAM
           #pragma vertex Vert
           #pragma fragment Frag

           #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
           #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
           #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

           float4 Frag(Varyings input): SV_Target
           {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float rawDepth = SampleSceneDepth(input.texcoord);
            float depth01;

            if(unity_OrthoParams.w > 0.5)//正交相机
            {
                #if UNITY_REVERSED_Z//检测z反转
                depth01 = 1.0 - rawDepth;
                #else
                depth01 = rawDepth;
                #endif
            }
            else
            {//透视相机
                float eyeDepth = 
                LinearEyeDepth(rawDepth, _ZBufferParams);//线性转化
                float nearPlane = _ProjectionParams.y;
                float farPlane = _ProjectionParams.z;//边界

                depth01 = (eyeDepth - nearPlane) / (farPlane - nearPlane);//物体归一化
            }

            depth01 = saturate(depth01);
            return float4(depth01,depth01,depth01, 1.0);
           }

           ENDHLSL


        }
    }
}
