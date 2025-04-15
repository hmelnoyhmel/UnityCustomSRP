using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CustomRenderPipeline : RenderPipeline
{
    private readonly CameraBufferSettings cameraBufferSettings;

    private readonly int colorLUTResolution;

    private readonly PostFXSettings postFXSettings;
    private readonly CameraRenderer renderer;

    private readonly ShadowSettings shadowSettings;

    private readonly bool useDynamicBatching, useGPUInstancing, useLightsPerObject;

    public CustomRenderPipeline(
        CameraBufferSettings cameraBufferSettings,
        bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher,
        bool useLightsPerObject, ShadowSettings shadowSettings,
        PostFXSettings postFXSettings, int colorLUTResolution,
        Shader cameraRendererShader)
    {
        this.colorLUTResolution = colorLUTResolution;
        this.cameraBufferSettings = cameraBufferSettings;
        this.postFXSettings = postFXSettings;
        this.shadowSettings = shadowSettings;
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        this.useLightsPerObject = useLightsPerObject;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        InitializeForEditor();
        renderer = new CameraRenderer(cameraRendererShader);
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
    }

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        for (var i = 0; i < cameras.Count; i++)
            renderer.Render(
                context, cameras[i], cameraBufferSettings,
                useDynamicBatching, useGPUInstancing, useLightsPerObject,
                shadowSettings, postFXSettings, colorLUTResolution
            );
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposeForEditor();
        renderer.Dispose();
    }
}