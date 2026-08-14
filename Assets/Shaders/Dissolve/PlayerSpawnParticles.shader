Shader "SpikeBall/Effects/Player Spawn Particles"
{
    Properties
    {
        [HDR] _TintColor ("Tint", Color) = (0.03,0.25,1.6,1)
        _Softness ("Softness", Range(0.01,0.49)) = 0.22
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Particles"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _TintColor;
                float _Softness;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color * _TintColor;
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float d = length(input.uv - 0.5);
                float core = 1.0 - smoothstep(max(0.01, 0.5 - _Softness), 0.5, d);
                float hot = 1.0 - smoothstep(0.0, 0.22, d);
                half3 color = input.color.rgb * (1.0 + hot * 1.8);
                return half4(color, input.color.a * core);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
