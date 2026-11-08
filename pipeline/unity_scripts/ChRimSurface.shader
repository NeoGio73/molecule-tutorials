// ChRimSurface.shader — translucent, edge-lit surface for the hydrophobic pocket (URP). Faces seen head-on are
// nearly clear and edges glow, so the pocket reads as a glassy cup without hiding the Phe ring inside it.
// _Alpha fades the whole surface (set on the material by ChSceneBuilder.SetPocketAlpha); uv.x carries each vertex's
// distance from the pocket centre, so the cut edge of the patch fades out instead of ending in a ragged line.
Shader "ChTutorial/RimSurface"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.55, 0.85, 0.60, 0.45)
        _RimColor ("Rim color", Color) = (0.80, 1.00, 0.80, 1)
        _RimPower ("Rim power", Range(0.5, 8)) = 2.2
        _Fill ("Face-on opacity", Range(0, 1)) = 0.08
        _Alpha ("Overall opacity", Range(0, 1)) = 1
        _FadeStart ("Edge fade start (uv.x)", Float) = 0.42
        _FadeEnd ("Edge fade end (uv.x)", Float) = 0.66
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _RimPower;
                half _Fill;
                half _Alpha;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float edge : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.edge = 1.0 - smoothstep(_FadeStart, _FadeEnd, i.uv.x);   // uv.x = distance from the pocket centre
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS), v = normalize(i.viewWS);
                half rim = pow(1.0h - saturate(abs(dot(n, v))), _RimPower);
                half3 col = lerp(_BaseColor.rgb, _RimColor.rgb, rim);
                half a = saturate(_Fill + rim * _BaseColor.a * 2.0h) * _Alpha * i.edge;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
