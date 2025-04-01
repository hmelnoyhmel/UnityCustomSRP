using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private CameraRenderer rendererTest = new CameraRenderer();

    private bool useDynamicBatching;
    private bool useGPUInstancing;
    private bool useLightsPerObject;
    private bool allowHDR;

    private ShadowSettings shadowSettings;
    private PostFXSettings postFXSettings;
    
    public CustomRenderPipeline(bool allowHDR, bool useDynamicBatching, bool useGPUInstancing, 
        bool useSRPBatcher, bool useLightsPerObject, 
        ShadowSettings shadowSettings, PostFXSettings postFXSettings) 
    {
        this.allowHDR = allowHDR;
        this.postFXSettings = postFXSettings;
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        this.useLightsPerObject = useLightsPerObject;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        this.shadowSettings = shadowSettings;
    }
    
    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
    }
    
    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (var camera in cameras)
        {
            rendererTest.Render(context, camera, allowHDR,
                useDynamicBatching, useGPUInstancing, 
                useLightsPerObject, shadowSettings, postFXSettings);
        }
    }
}
