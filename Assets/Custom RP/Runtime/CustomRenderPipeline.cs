using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private CameraRenderer rendererTest = new CameraRenderer();

    private bool useDynamicBatching;
    private bool useGPUInstancing;
    private bool useLightsPerObject;

    private ShadowSettings shadowSettings;
    
    public CustomRenderPipeline(bool useDynamicBatching, bool useGPUInstancing, 
        bool useSRPBatcher, bool useLightsPerObject, 
        ShadowSettings shadowSettings) 
    {
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
            rendererTest.Render(context, camera, 
                useDynamicBatching, useGPUInstancing, 
                useLightsPerObject, shadowSettings);
        }
    }
}
