Shader "SpikeBall/Effects/Player Spawn Dissolve"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _DissolveProgress ("Dissolve Progress", Range(0,1)) = 1
        _DissolveMinY ("Dissolve Min Y", Float) = 0
        _DissolveMaxY ("Dissolve Max Y", Float) = 1
        _DissolveNoiseScale ("Noise Scale", Float) = 5
        _DissolveNoiseStrength ("Noise Strength", Range(0,0.5)) = 0.18
        _DissolveEdgeWidth ("Edge Width", Range(0.001,0.3)) = 0.085
        [HDR] _EdgeColor ("Hot Edge Color", Color) = (12,0.35,2.2,1)
        [HDR] _EdgeColor2 ("Cool Edge Color", Color) = (0.35,1.5,12,1)
        _EdgeIntensity ("Edge Intensity", Range(0,5)) = 1.35
        [HDR] _HologramTint ("Hologram Tint", Color) = (0.08,0.25,1.4,1)
        _FresnelStrength ("Fresnel Strength", Range(0,2)) = 0.42
        _NoiseOffset ("Noise Offset", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                float _DissolveProgress;
                float _DissolveMinY;
                float _DissolveMaxY;
                float _DissolveNoiseScale;
                float _DissolveNoiseStrength;
                float _DissolveEdgeWidth;
                half4 _EdgeColor;
                half4 _EdgeColor2;
                float _EdgeIntensity;
                half4 _HologramTint;
                float _FresnelStrength;
                float4 _NoiseOffset;
            CBUFFER_END

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i + float3(0,0,0));
                float n100 = Hash31(i + float3(1,0,0));
                float n010 = Hash31(i + float3(0,1,0));
                float n110 = Hash31(i + float3(1,1,0));
                float n001 = Hash31(i + float3(0,0,1));
                float n101 = Hash31(i + float3(1,0,1));
                float n011 = Hash31(i + float3(0,1,1));
                float n111 = Hash31(i + float3(1,1,1));
                float n00 = lerp(n000, n100, f.x);
                float n10 = lerp(n010, n110, f.x);
                float n01 = lerp(n001, n101, f.x);
                float n11 = lerp(n011, n111, f.x);
                return lerp(lerp(n00, n10, f.y), lerp(n01, n11, f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.55;
                value += ValueNoise3D(p) * amplitude;
                p = p * 2.03 + 17.17;
                amplitude *= 0.5;
                value += ValueNoise3D(p) * amplitude;
                p = p * 2.11 + 9.31;
                amplitude *= 0.5;
                value += ValueNoise3D(p) * amplitude;
                return value / 0.9625;
            }

            float DissolveDistance(float3 positionWS, out float noiseValue)
            {
                float heightRange = max(_DissolveMaxY - _DissolveMinY, 0.001);
                float height01 = saturate((positionWS.y - _DissolveMinY) / heightRange);
                float3 noisePos = positionWS * _DissolveNoiseScale + _NoiseOffset.xyz;
                noisePos.y += _Time.y * 0.22;
                noiseValue = Fbm(noisePos);
                float band = max(_DissolveNoiseStrength, 0.001);
                float front = saturate(_DissolveProgress) * (1.0 + 2.0 * band) - band;
                return front - height01 + (noiseValue - 0.5) * band;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = normal.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(pos);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half3 EvaluateLight(half3 baseColor, half3 normalWS, half3 viewDirWS, Light light)
            {
                half ndl = saturate(dot(normalWS, light.direction));
                half3 diffuse = baseColor * light.color * ndl * light.distanceAttenuation * light.shadowAttenuation;
                half3 halfDir = SafeNormalize(light.direction + viewDirWS);
                half specPower = exp2(4.0h + _Smoothness * 7.0h);
                half specTerm = pow(saturate(dot(normalWS, halfDir)), specPower) * (0.15h + _Smoothness * 0.85h);
                half3 f0 = lerp(0.04h.xxx, baseColor, _Metallic);
                return diffuse * (1.0h - _Metallic * 0.65h) + f0 * specTerm * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float noiseValue;
                float dissolveDistance = DissolveDistance(input.positionWS, noiseValue);
                clip(dissolveDistance);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 color = baseSample.rgb * SampleSH(normalWS);

                Light mainLight = GetMainLight(input.shadowCoord);
                color += EvaluateLight(baseSample.rgb, normalWS, viewDirWS, mainLight);

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    color += EvaluateLight(baseSample.rgb, normalWS, viewDirWS, light);
                }
                #endif

                float edgeWidth = max(_DissolveEdgeWidth, 0.001);
                float edge = 1.0 - smoothstep(0.0, edgeWidth, dissolveDistance);
                float filament = pow(saturate(ValueNoise3D(input.positionWS * (_DissolveNoiseScale * 2.7) + _NoiseOffset.xyz + _Time.y * float3(0.07,0.35,0.03))), 5.0);
                half3 edgeColor = lerp(_EdgeColor2.rgb, _EdgeColor.rgb, saturate(noiseValue * 1.35));
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), 3.2h);
                color += edgeColor * edge * _EdgeIntensity * (1.0 + filament * 1.8);
                color += _HologramTint.rgb * fresnel * _FresnelStrength * saturate(1.0 - dissolveDistance * 1.5);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                float _DissolveProgress;
                float _DissolveMinY;
                float _DissolveMaxY;
                float _DissolveNoiseScale;
                float _DissolveNoiseStrength;
                float _DissolveEdgeWidth;
                half4 _EdgeColor;
                half4 _EdgeColor2;
                float _EdgeIntensity;
                half4 _HologramTint;
                float _FresnelStrength;
                float4 _NoiseOffset;
            CBUFFER_END

            float Hash31Shadow(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3DShadow(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31Shadow(i + float3(0,0,0));
                float n100 = Hash31Shadow(i + float3(1,0,0));
                float n010 = Hash31Shadow(i + float3(0,1,0));
                float n110 = Hash31Shadow(i + float3(1,1,0));
                float n001 = Hash31Shadow(i + float3(0,0,1));
                float n101 = Hash31Shadow(i + float3(1,0,1));
                float n011 = Hash31Shadow(i + float3(0,1,1));
                float n111 = Hash31Shadow(i + float3(1,1,1));
                float n00 = lerp(n000, n100, f.x);
                float n10 = lerp(n010, n110, f.x);
                float n01 = lerp(n001, n101, f.x);
                float n11 = lerp(n011, n111, f.x);
                return lerp(lerp(n00, n10, f.y), lerp(n01, n11, f.y), f.z);
            }

            float FbmShadow(float3 p)
            {
                float value = ValueNoise3DShadow(p) * 0.55;
                p = p * 2.03 + 17.17;
                value += ValueNoise3DShadow(p) * 0.275;
                p = p * 2.11 + 9.31;
                value += ValueNoise3DShadow(p) * 0.1375;
                return value / 0.9625;
            }

            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionWS = positionWS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                float rangeY = max(_DissolveMaxY - _DissolveMinY, 0.001);
                float height01 = saturate((input.positionWS.y - _DissolveMinY) / rangeY);
                float3 noisePos = input.positionWS * _DissolveNoiseScale + _NoiseOffset.xyz;
                noisePos.y += _Time.y * 0.22;
                float noiseValue = FbmShadow(noisePos);
                float band = max(_DissolveNoiseStrength, 0.001);
                float front = saturate(_DissolveProgress) * (1.0 + 2.0 * band) - band;
                clip(front - height01 + (noiseValue - 0.5) * band);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
