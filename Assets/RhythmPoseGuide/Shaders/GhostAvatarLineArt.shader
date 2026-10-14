// Two thin contours of the same target pose, drawn without any fill so the
// player's own body stays readable through the guide.
//
//   - Target contour:   the goal outline, held on the body, dim.
//   - Approach contour: the same outline pushed out along its own normals,
//                       shrinking onto the target contour as the beat
//                       arrives. A uniform offset is used rather than a
//                       scale: scaling runs about the object origin at the
//                       feet, which lifts the head far more than it widens
//                       the body and reads as the guide dropping from above.
//
// URP draws only ONE pass per LightMode tag, so this shader is limited to the
// two tags the forward renderer iterates: UniversalForward and
// SRPDefaultUnlit. That rules out the stencil-mask approach, which needs a
// mask pass per contour. Each contour is therefore produced in a single pass
// by a view-facing rim term instead of an inverted hull: fragments whose
// normal turns away from the camera are the silhouette, everything facing the
// camera stays transparent.
Shader "IVRC/Rhythm Pose Guide/Ghost Avatar Line Art"
{
    Properties
    {
        [MainColor] _BaseColor("Approach Contour Color", Color) = (0.72, 0.92, 1.0, 0.9)
        _TargetColor("Target Contour Color", Color) = (0.72, 0.92, 1.0, 0.3)
        _Intensity("Intensity", Range(0.0, 4.0)) = 1.0
        _ApproachOffset("Approach Offset", Range(0.0, 0.4)) = 0.12
        _ApproachLineThickness("Approach Line Thickness", Range(0.02, 1.0)) = 0.45
        _TargetLineThickness("Target Line Thickness", Range(0.02, 1.0)) = 0.3
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

        HLSLINCLUDE
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
            float3 normalWS : TEXCOORD0;
            float3 viewDirWS : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _TargetColor;
            half _Intensity;
            half _ApproachOffset;
            half _ApproachLineThickness;
            half _TargetLineThickness;
        CBUFFER_END

        // Pushes the surface out along its own normal by a world-space
        // distance, so the contour sits the same distance outside the goal
        // outline in every direction and closes in evenly.
        Varyings ContourVert(Attributes input, float offsetMeters)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 positionWS =
                TransformObjectToWorld(input.positionOS.xyz);
            float3 normalWS =
                normalize(TransformObjectToWorldNormal(input.normalOS));
            positionWS += normalWS * max(offsetMeters, 0.0);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.normalWS = normalWS;
            output.viewDirWS = GetWorldSpaceViewDir(positionWS);
            return output;
        }

        // 1 on the silhouette, falling to 0 wherever the surface faces the
        // camera. Thickness widens the band that still counts as the edge.
        half ContourAlpha(Varyings input, half thickness)
        {
            float3 normalWS = normalize(input.normalWS);
            float3 viewDirWS = normalize(input.viewDirWS);
            half facing = (half)abs(dot(normalWS, viewDirWS));
            half edge = 1.0h - facing;
            half width = max(thickness, 0.02h);
            return smoothstep(1.0h - width, 1.0h, edge);
        }
        ENDHLSL

        // The goal outline, held on the body.
        Pass
        {
            Name "TargetContour"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            Varyings Vert(Attributes input)
            {
                return ContourVert(input, 0.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 color = _TargetColor;
                color.rgb *= max(_Intensity, 0.0h);
                color.a *= ContourAlpha(input, _TargetLineThickness);
                return color;
            }
            ENDHLSL
        }

        // The shrinking line the player reads for timing.
        Pass
        {
            Name "ApproachContour"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            Varyings Vert(Attributes input)
            {
                return ContourVert(input, _ApproachOffset);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 color = _BaseColor;
                color.rgb *= max(_Intensity, 0.0h);
                color.a *= ContourAlpha(input, _ApproachLineThickness);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
