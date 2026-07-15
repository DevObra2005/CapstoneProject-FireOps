// -------------------------------------------------------
// WHAT THIS DOES:
// Draws a solid-colored, slightly-enlarged, INSIDE-OUT copy of
// an object. Because it's flipped inside-out ("Cull Front") and
// pushed slightly outward, only its edges peek out from behind
// the real object — creating a glowing outline effect.
//
// You don't need to understand every line here — just know:
// _OutlineColor = what color the outline is
// _OutlineWidth = how thick/far the outline sticks out
// -------------------------------------------------------

Shader "Custom/OutlineUnlit"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.2, 0.6, 1, 1)
        _OutlineWidth ("Outline Width", Range(0.0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }

        Pass
        {
            Name "Outline"

            // "Cull Front" = the key trick. Normally Unity hides the
            // INSIDE of a shape and only draws the outside. This flips
            // that — hiding the outside and drawing the inside instead.
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            float4 _OutlineColor;
            float _OutlineWidth;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Push each point of the shape slightly outward,
                // along its own surface direction (normal). This is
                // what makes the duplicate slightly BIGGER than the
                // original, so its edges show past it.
                float3 posOS = IN.positionOS.xyz + IN.normalOS * _OutlineWidth;

                OUT.positionHCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Every pixel just gets painted the outline color —
                // flat and simple, no lighting calculations needed.
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
