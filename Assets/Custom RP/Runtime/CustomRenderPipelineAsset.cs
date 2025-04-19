using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
public class CustomRenderPipelineAsset : RenderPipelineAsset<CustomRenderPipeline>
{
    
    public enum ColorLUTResolution
    {
        _16 = 16,
        _32 = 32,
        _64 = 64
    }

    [SerializeField] private CameraBufferSettings cameraBuffer = new()
    {
        allowHDR = true,
        renderScale = 1f,
        fxaa = new CameraBufferSettings.FXAA
        {
            fixedThreshold = 0.0833f,
            relativeThreshold = 0.166f,
            subpixelBlending = 0.75f
        }
    };

    [SerializeField] private bool
        useSRPBatcher = true,
        useLightsPerObject = true;

    [SerializeField] private ShadowSettings shadows;

    [SerializeField] private PostFXSettings postFXSettings;

    [SerializeField] private ColorLUTResolution colorLUTResolution = ColorLUTResolution._32;

    [SerializeField] private Shader cameraRendererShader;

    [Header("Deprecated Settings")]
    [SerializeField, Tooltip("Dynamic batching is no longer used.")]
    bool useDynamicBatching;

    [SerializeField, Tooltip("GPU instancing is always enabled.")]
    bool useGPUInstancing;
    
    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline(
            cameraBuffer, useSRPBatcher, useLightsPerObject, 
            shadows, postFXSettings, (int)colorLUTResolution,
            cameraRendererShader);
    }
}