// -----------------------------------------------------------------------------
// ref:
// https://zenn.dev/sakutaro/articles/convert_blitter
// -----------------------------------------------------------------------------

Shader "Custom/Godray"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "Renderpipeline"="UniversalPipeline"
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // TODO: fix keywords

            // URP Keywords
            // #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            // #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            // // Note, v11 changes this to :
            // // #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            // #pragma multi_compile _ _SHADOWS_SOFT
            // #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            // #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            // #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING // v10+ only, renamed from "_MIXED_LIGHTING_SUBTRACTIVE"
            // #pragma multi_compile _ SHADOWS_SHADOWMASK // v10+ only


            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            // #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY


            #if SHADER_API_GLES
                struct GodrayAttributes
                {
                    float4 positionOS : POSITION;
                    float2 uv : TEXCOORD0;
                };
            #else
            struct GodrayAttributes
            {
                uint vertexID : SV_VertexID;
            };
            #endif

            struct GodrayVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 ray : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            SAMPLER(sample_CameraDepthTexture);

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _CameraDepthTexture_ST;
            float _BlendRate;
            float _GlobalAlpha;
            half4 _FogColor;
            float4x4 _InverseViewMatrix;
            float4x4 _InverseViewProjectionMatrix;
            float4x4 _InverseProjectionMatrix;
            float4x4 _FrustumCorners;
            float _RayStep;
            float _RayNearOffset;
            float _RayBinaryStep;
            float _RayJitterSizeX;
            float _RayJitterSizeY;
            float _AttenuationBase;
            float _AttenuationPower;
            // int _MaxIterationNum;
            CBUFFER_END

            // https://stackoverflow.com/questions/4200224/random-noise-functions-for-glsl
            float noise(float2 seed)
            {
                return frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);
            }

            float3 ReconstructViewPositionFromDepth(float2 screenUV, float rawDepth)
            {
                float4 clipPos = float4(screenUV * 2.0 - 1.0, rawDepth, 1.0);
                float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
                return viewPos.xyz / viewPos.w;
            }
            
            GodrayVaryings vert(GodrayAttributes IN)
            {
                GodrayVaryings OUT;
                #if SHADER_API_GLES
                    float4 pos = input.positionOS;
                    float2 uv  = input.uv;
                #else
                float4 pos = GetFullScreenTriangleVertexPosition(IN.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                #endif

                OUT.positionHCS = pos;
                OUT.uv = uv;
                OUT.ray = _FrustumCorners[uv.x + 2 * uv.y];
                return OUT;
            }

            half4 frag(GodrayVaryings IN) : SV_Target
            {
                float eps = 0.0001;

                half4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearRepeat, IN.uv);

                int maxIterationNum = 64;

                float2 uv = IN.positionHCS.xy / _ScaledScreenParams.xy;
                
                float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_LinearRepeat, IN.uv);
                #if UNITY_REVERSED_Z
                    rawDepth = 1.0f - rawDepth;
                #endif

                float rayJitter = noise(IN.uv + _Time.x) * 2. - 1.;

                float3 rayOffset = float3(rayJitter * _RayJitterSizeX, rayJitter * _RayJitterSizeY, 0);
                float3 rayOriginInView = rayOffset;
                
                float3 rayEndPositionInView = ReconstructViewPositionFromDepth(IN.uv, rawDepth) + rayOffset;
                float3 rayDirInView = normalize(rayEndPositionInView - rayOriginInView);

                float alpha = 0.;

                float rayStep = length(rayEndPositionInView - rayOriginInView) / float(maxIterationNum);
                // rayStep = min(rayStep, _RayStep);
                rayStep = _RayStep;

                for (int i = 0; i < maxIterationNum; i++)
                {
                    float3 currentRayStep = rayDirInView * (rayStep * i + _RayNearOffset);
                    float3 currentRayInView = rayOriginInView + currentRayStep;

                    half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(mul(_InverseViewMatrix, float4(currentRayInView, 1.))));
                    // こちらでもよい
                    // half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(mul(UNITY_MATRIX_I_V, float4(currentRayInView, 1.))));

                    if (shadow >= 1.)
                    {
                        alpha += (1. / _AttenuationBase);
                    }
                }

                alpha = saturate(alpha);

                alpha = pow(alpha, _AttenuationPower);

                alpha *= _GlobalAlpha;

                half4 destColor = half4(alpha, 1, 1, 1);

                return destColor;
            }
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #if SHADER_API_GLES
                struct GodrayAttributes
                {
                    float4 positionOS : POSITION;
                    float2 uv : TEXCOORD0;
                };
            #else
            struct GodrayAttributes
            {
                uint vertexID : SV_VertexID;
            };
            #endif

            struct GodrayVaryings
            {
                float2 uv : TEXCOORD0;
                float4 positionHCS : SV_POSITION;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sample_CameraDepthTexture);
            TEXTURE2D(_GodrayTexture);
            SAMPLER(sampler_GodrayTexture);

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _GodrayTexture_ST;
            float _BlendRate;
            half4 _FogColor;
            CBUFFER_END

            GodrayVaryings vert(GodrayAttributes IN)
            {
                GodrayVaryings OUT;
                #if SHADER_API_GLES
                    float4 pos = input.positionOS;
                    float2 uv  = input.uv;
                #else
                float4 pos = GetFullScreenTriangleVertexPosition(IN.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                #endif

                OUT.positionHCS = pos;
                OUT.uv = uv;
                return OUT;
            }

            half4 frag(GodrayVaryings IN) : SV_Target
            {
                half4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearRepeat, IN.uv);
                half4 godray = SAMPLE_TEXTURE2D(_GodrayTexture, sampler_LinearRepeat, IN.uv);

                half3 blendColor = lerp(sceneColor.xyz, _FogColor.xyz, godray.x * _BlendRate);

                return half4(blendColor, 1.);
            }
            ENDHLSL
        }

    }
}
