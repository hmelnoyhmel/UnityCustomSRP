using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
public class CustomRenderPipelineAsset : RenderPipelineAsset<CustomRenderPipeline>
{
    [SerializeField]
    CustomRenderPipelineSettings settings;
    
    [Header("Deprecated Settings")]
    [SerializeField, Tooltip("Moved to settings.")] private CameraBufferSettings cameraBuffer = new()
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
    
    public enum ColorLUTResolution
    {
        _16 = 16,
        _32 = 32,
        _64 = 64
    }
    
    [SerializeField, Tooltip("Moved to settings.")] private bool useSRPBatcher = true;
    [SerializeField, Tooltip("Moved to settings.")] private bool useLightsPerObject = true;
    
    [SerializeField, Tooltip("Moved to settings.")] private ShadowSettings shadows;

    [SerializeField, Tooltip("Moved to settings.")] private PostFXSettings postFXSettings;

    [SerializeField, Tooltip("Moved to settings.")] private ColorLUTResolution colorLUTResolution = ColorLUTResolution._32;

    [SerializeField, Tooltip("Moved to settings.")] private Shader cameraRendererShader;
    
    
    
    protected override RenderPipeline CreatePipeline()
    {
        // copy old fields to the new settings
        if ((settings == null || settings.cameraRendererShader == null) &&
            cameraRendererShader != null)
        {
            settings = new CustomRenderPipelineSettings
            {
                cameraBuffer = cameraBuffer,
                useSRPBatcher = useSRPBatcher,
                useLightsPerObject = useLightsPerObject,
                shadows = shadows,
                postFXSettings = postFXSettings,
                colorLUTResolution = (CustomRenderPipelineSettings.ColorLUTResolution)colorLUTResolution,
                cameraRendererShader = cameraRendererShader
            };
        }
        
        return new CustomRenderPipeline(settings);
    }
}