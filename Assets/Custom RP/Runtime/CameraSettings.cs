using System;
using UnityEngine;
using UnityEngine.Rendering;

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
    
    public RenderingLayerMask newRenderingLayerMask = -1;

    public bool maskLights;

    public RenderScaleMode renderScaleMode = RenderScaleMode.Inherit;

    [Range(CameraRenderer.renderScaleMin, CameraRenderer.renderScaleMax)]
    public float renderScale = 1f;

    public bool overridePostFX;

    public PostFXSettings postFXSettings;

    public bool allowFXAA;

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