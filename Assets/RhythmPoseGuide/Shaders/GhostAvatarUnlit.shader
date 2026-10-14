Shader "IVRC/Rhythm Pose Guide/Ghost Avatar Unlit"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.72, 0.92, 1.0, 0.35)
        _Intensity("Intensity", Range(0.0, 4.0)) = 1.0
        _OutlineWidth("Outline Width", Range(0.001, 0.04)) = 0.008
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "UniversalMaterialType" = "Unlit"
        }

        // First mark the filled silhouette in the stencil buffer without
        // writing any color. The second pass can then leave the interior
        // untouched and draw only the expanded outer contour.
        //
        // URP draws one pass per LightMode tag, in the order the forward
        // renderer lists them: SRPDefaultUnlit, then UniversalForward
        // (DrawObjectsPass.cs). The two passes therefore need different tags
        // or only the first would run at all, and the mask has to claim
        // SRPDefaultUnlit so it is written before the outline reads it.
        Pass
        {
            Name "SilhouetteMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZWrite Off
            ColorMask 0
            Cull Back
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings MaskVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MaskFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
                half _OutlineWidth;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS =
                    TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS =
                    TransformObjectToWorldNormal(input.normalOS);
                positionWS +=
                    normalize(normalWS) * max(_OutlineWidth, 0.001h);
                output.positionCS =
                    TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 color = _BaseColor;
                color.rgb *= max(_Intensity, 0.0h);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
