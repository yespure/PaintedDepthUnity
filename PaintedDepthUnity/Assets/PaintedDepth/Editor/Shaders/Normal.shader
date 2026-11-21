Shader "Custom/Normal"
{

    SubShader
    {
        Tags {  "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Normal"

            ZWrite off
            ZTest Always
            Cull off

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

           float4 Frag(Varyings input):SV_Target
           {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 normalWS = SampleSceneNormals(input.texcoord);//读取世界空间法线

            float lengthSquared = dot(normalWS, normalWS);//计算向量长度

            if(lengthSquared < 0.0001)
            {
                return float4(0,0,0,1);
            }

            normalWS *= rsqrt(lengthSquared);//归一化

            float3 encodedNormal = normalWS * 0.5 + 0.5;

            return float4(encodedNormal, 1);

           }

           ENDHLSL
        }
    }
}
