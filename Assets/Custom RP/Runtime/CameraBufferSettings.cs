using System;
using UnityEngine;

[Serializable]
public struct CameraBufferSettings
{
    public enum BicubicRescalingMode
    {
        Off,
        UpOnly,
        UpAndDown
    }

    public bool allowHDR;

    public bool copyColor, copyColorReflection, copyDepth, copyDepthReflection;

    [Range(CameraRenderer.renderScaleMin, CameraRenderer.renderScaleMax)]
    public float renderScale;

    public BicubicRescalingMode bicubicRescaling;

    public FXAA fxaa;

    [Serializable]
    public struct FXAA
    {
        public enum Quality
        {
            Low,
            Medium,
            High
        }

        public bool enabled;

        [Range(0.0312f, 0.0833f)] public float fixedThreshold;

        [Range(0.063f, 0.333f)] public float relativeThreshold;

        [Range(0f, 1f)] public float subpixelBlending;

        public Quality quality;
    }
}