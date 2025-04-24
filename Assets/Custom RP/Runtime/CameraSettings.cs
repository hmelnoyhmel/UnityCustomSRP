using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Custom_RP.Runtime
{
    [Serializable]
    public class CameraSettings
    {
        public enum RenderScaleMode
        {
            Inherit,
            Multiply,
            Override
        }

        public bool copyColor = true, copyDepth = true;

        [HideInInspector, Obsolete("Use newRenderingLayerMask instead.")]
        public int renderingLayerMask = -1;
    
        public RenderingLayerMask NewRenderingLayerMask = -1;

        public bool maskLights;

        public RenderScaleMode renderScaleMode = RenderScaleMode.Inherit;

        [Range(CameraRenderer.RenderScaleMin, CameraRenderer.RenderScaleMax)]
        public float renderScale = 1f;

        public bool overridePostFX;

        public PostFXSettings postFXSettings;

        public bool allowFxaa;

        public bool keepAlpha;

        public FinalBlendMode finalBlendMode = new()
        {
            source = BlendMode.One,
            destination = BlendMode.Zero
        };

        public float GetRenderScale(float scale)
        {
            return renderScaleMode == RenderScaleMode.Inherit ? scale :
                renderScaleMode == RenderScaleMode.Override ? renderScale :
                scale * renderScale;
        }

        [Serializable]
        public struct FinalBlendMode
        {
            public BlendMode source, destination;
        }
    }
}