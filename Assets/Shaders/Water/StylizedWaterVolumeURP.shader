Shader "SpikeBall/URP Stylized Water Volume"
{
    Properties
    {
        [NoScaleOffset] _BumpMap ("Wave Normal", 2D) = "bump" {}
        [NoScaleOffset] _Fresnel ("WaterPro Fresnel", 2D) = "gray" {}
        [NoScaleOffset] _ReflectiveColor ("WaterPro Daytime Gradient", 2D) = "white" {}
        _TilesPerMeter ("Tiles Per Meter", Range(0.05, 3.0)) = 0.32
        _WaveScale ("WaterPro Wave Scale", Range(0.02, 0.15)) = 0.145
        _LegacyWaveSpeed ("WaterPro Wave Speed", Vector) = (8.5, 4.2, -7.4, -3.1)
        _ShallowColor ("Shallow Color", Color) = (0.18, 0.72, 0.86, 0.72)
        _DeepColor ("Deep Color", Color) = (0.025, 0.20, 0.34, 0.88)
        _HorizonColor ("Horizon Color", Color) = (0.42, 0.88, 0.95, 1)
        _DepthDistance ("Depth Distance", Range(0.1, 20)) = 4.0
        _RefractionStrength ("Refraction Strength", Range(0, 0.10)) = 0.042
        _RefractionColor ("Refraction Color", Color) = (0.72, 0.94, 1, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FoamWidth ("Edge Foam Width", Range(0, 2)) = 0.28
        _FoamIntensity ("Edge Foam Intensity", Range(0, 2)) = 0.65
        _WaveHighlight ("Wave Highlight", Range(0, 1)) = 0.02
        [HideInInspector] _UseRefraction ("Use Refraction", Float) = 0
        [HideInInspector] _WaveTiling ("Computed Wave Tiling", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "StylizedWaterVolume"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_Fresnel);
            SAMPLER(sampler_Fresnel);
            TEXTURE2D(_ReflectiveColor);
            SAMPLER(sampler_ReflectiveColor);

            CBUFFER_START(UnityPerMaterial)
                float4 _LegacyWaveSpeed;
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _HorizonColor;
                float4 _RefractionColor;
                float4 _WaveTiling;
                float _TilesPerMeter;
                float _WaveScale;
                float _DepthDistance;
                float _RefractionStrength;
                float _FresnelPower;
                float _FoamWidth;
                float _FoamIntensity;
                float _WaveHighlight;
                float _UseRefraction;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 localPlane : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                float3 tangentWS : TEXCOORD5;
                float3 bitangentWS : TEXCOORD6;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.localPlane = input.positionOS.xz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(float3(1, 0, 0));
                output.bitangentWS = TransformObjectToWorldDir(float3(0, 0, 1));
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Match Unity 5 WaterProDaytime: two differently scaled copies of
                // WaterBump, including its original swapped second UV pair.
                float2 tiledLocalPos = input.localPlane * _WaveTiling.xy;
                float4 waveScale4 = float4(_WaveScale, _WaveScale,
                    _WaveScale * 0.4, _WaveScale * 0.45);
                float4 waveOffset = frac(_LegacyWaveSpeed * waveScale4 * (_Time.y / 20.0));
                float2 uv1 = tiledLocalPos * waveScale4.xy + waveOffset.xy;
                float2 uv2 = float2(
                    tiledLocalPos.y * waveScale4.w + waveOffset.w,
                    tiledLocalPos.x * waveScale4.z + waveOffset.z);
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv1));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv2));
                half3 waveNormal = normalize((n1 + n2) * 0.5h);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float3 perturbedNormalWS = normalize(
                    normalWS * waveNormal.z +
                    normalize(input.tangentWS) * waveNormal.x +
                    normalize(input.bitangentWS) * waveNormal.y);
                half facing = saturate(abs(dot(viewDirWS, perturbedNormalWS)));
                half fresnel = SAMPLE_TEXTURE2D(_Fresnel, sampler_Fresnel,
                    half2(facing, facing)).a;
                fresnel = saturate(lerp(fresnel, pow(1.0h - facing, _FresnelPower), 0.2h));
                half waveDetail = saturate(length(waveNormal.xy) * 1.4h);

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEyeDepth = input.screenPos.w;
                float thickness = max(0.0, sceneEyeDepth - surfaceEyeDepth);
                half depth01 = saturate(thickness / max(0.001, _DepthDistance));

                half4 waterGradient = SAMPLE_TEXTURE2D(_ReflectiveColor,
                    sampler_ReflectiveColor, half2(facing, facing));
                half3 simpleColor = lerp(waterGradient.rgb, _HorizonColor.rgb, waterGradient.a);
                half3 waterColor = lerp(simpleColor, _DeepColor.rgb, depth01 * 0.30h);

                if (_UseRefraction > 0.5)
                {
                    // URP equivalent of WaterPro's refraction render texture.
                    // Opposite sign deliberately matches the original shader.
                    float2 distortedUV = saturate(screenUV - waveNormal.xy * _RefractionStrength);
                    half3 refractedScene = SampleSceneColor(distortedUV) * _RefractionColor.rgb;
                    half3 reflectedApprox = lerp(waterGradient.rgb, _HorizonColor.rgb,
                        waterGradient.a);
                    waterColor = lerp(refractedScene, reflectedApprox, fresnel);
                    waterColor = lerp(waterColor, _DeepColor.rgb, depth01 * 0.12h);
                }

                waterColor += waveDetail * _WaveHighlight * _HorizonColor.rgb;

                half foam = 0;
                if (_FoamWidth > 0.0001)
                    foam = saturate(1.0h - thickness / _FoamWidth) * _FoamIntensity;
                waterColor = lerp(waterColor, _HorizonColor.rgb, saturate(foam));

                // WaterPro's refractive pass is opaque after it has sampled the
                // background. Near-opaque output avoids blending that background twice.
                half alpha = (_UseRefraction > 0.5) ? 0.96h :
                    max(lerp(_ShallowColor.a, _DeepColor.a, depth01), 0.82h);

                waterColor = MixFog(waterColor, input.fogFactor);
                return half4(waterColor, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
