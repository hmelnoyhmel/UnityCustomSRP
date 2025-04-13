using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private CameraRenderer renderer;
    private CameraBufferSettings cameraBufferSettings;

    private bool useDynamicBatching;
    private bool useGPUInstancing;
    private bool useLightsPerObject;

    private ShadowSettings shadowSettings;
    private PostFXSettings postFXSettings;
    private int colorLUTResolution;
    
    public CustomRenderPipeline(CameraBufferSettings cameraBufferSettings, bool useDynamicBatching, bool useGPUInstancing, 
        bool useSRPBatcher, bool useLightsPerObject, ShadowSettings shadowSettings, 
        PostFXSettings postFXSettings, int colorLUTResolution, Shader cameraRendererShader) 
    {
        this.colorLUTResolution = colorLUTResolution;
        this.cameraBufferSettings = cameraBufferSettings;
        this.postFXSettings = postFXSettings;
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        this.useLightsPerObject = useLightsPerObject;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        this.shadowSettings = shadowSettings;
        renderer = new CameraRenderer(cameraRendererShader);
    }
    
    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
    }
    
    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (var camera in cameras)
        {
            renderer.Render(context, camera, cameraBufferSettings,
                useDynamicBatching, useGPUInstancing, useLightsPerObject, 
                shadowSettings, postFXSettings, colorLUTResolution);
        }
    }
}
