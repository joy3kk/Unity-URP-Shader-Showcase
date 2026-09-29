Shader "Hidden/SpikeBall/CharacterOutlineComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Character Outline Composite"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_CharacterOutlineMask);

            float4 _MaskTexelSize;
            float _OutlineRadius;
            float _OutlineDarkness;
            float _OutlineIntensity;
            float _OutlineSoftness;
            float _InnerOutline;

            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_CharacterOutlineMask, sampler_LinearClamp, saturate(uv)).r;
            }

            float3 SampleSceneColor(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            void ConsiderNeighbour(
                float2 uv,
                float2 direction,
                float2 offset,
                inout float highestMask,
                inout float lowestMask,
                inout float2 bestDirection)
            {
                float value = SampleMask(uv + direction * offset);
                lowestMask = min(lowestMask, value);
                if (value > highestMask)
                {
                    highestMask = value;
                    bestDirection = direction;
                }
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float3 sceneColor = SampleSceneColor(uv);
                float centerMask = SampleMask(uv);
                float2 offset = _MaskTexelSize.xy * max(_OutlineRadius, 0.5);

                float highestMask = centerMask;
                float lowestMask = centerMask;
                float2 bestDirection = float2(0.0, 0.0);

                ConsiderNeighbour(uv, float2(-1.0,  0.0), offset, highestMask, lowestMask, bestDirection);
                ConsiderNeighbour(uv, float2( 1.0,  0.0), offset, highestMask, lowestMask, bestDirection);
                ConsiderNeighbour(uv, float2( 0.0, -1.0), offset, highestMask, lowestMask, bestDirection);
                ConsiderNeighbour(uv, float2( 0.0,  1.0), offset, highestMask, lowestMask, bestDirection);
                ConsiderNeighbour(uv, normalize(float2(-1.0, -1.0)), offset, highestMask, lowestMask, bestDirection);
                ConsiderNeighbour(uv, normalize(float2( 1.0, -1.0)), offset, highestMask, lowestMask, bestDirection);
                ConsiderNeighbour(uv, normalize(float2(-1.0,  1.0)), offset, highestMask, lowestMask, bestDirection);
                ConsiderNeighbour(uv, normalize(float2( 1.0,  1.0)), offset, highestMask, lowestMask, bestDirection);

                float outerDifference = saturate(highestMask - centerMask);
                float innerDifference = saturate(centerMask - lowestMask);
                float edgeThreshold = lerp(0.36, 0.04, saturate(_OutlineSoftness));
                float outerEdge = smoothstep(edgeThreshold, 1.0, outerDifference);
                float innerEdge = smoothstep(edgeThreshold, 1.0, innerDifference) * _InnerOutline;
                float outlineMask = saturate(outerEdge + innerEdge) * _OutlineIntensity;

                // Outside the silhouette, fetch a color from the nearest character-side sample.
                // This preserves the character's local hue instead of using a uniform black line.
                float2 characterUV = uv + bestDirection * offset * 1.15;
                float3 characterColor = SampleSceneColor(characterUV);
                float3 outlineSource = outerEdge > 0.0 ? characterColor : sceneColor;
                float3 darkenedOutline = outlineSource * saturate(_OutlineDarkness);

                return half4(lerp(sceneColor, darkenedOutline, saturate(outlineMask)), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
