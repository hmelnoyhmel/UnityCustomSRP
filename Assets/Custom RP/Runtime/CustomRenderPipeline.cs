using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public partial class CustomRenderPipeline : RenderPipeline
{
    private readonly RenderGraph renderGraph = new("Custom SRP Render Graph");
    
    private readonly CameraBufferSettings cameraBufferSettings;

    private readonly int colorLUTResolution;

    private readonly PostFXSettings postFXSettings;
    private readonly CameraRenderer renderer;

    private readonly ShadowSettings shadowSettings;

    private readonly bool useLightsPerObject;

    public CustomRenderPipeline(
        CameraBufferSettings cameraBufferSettings,
        bool useSRPBatcher, bool useLightsPerObject, ShadowSettings shadowSettings,
        PostFXSettings postFXSettings, int colorLUTResolution,
        Shader cameraRendererShader)
    {
        this.colorLUTResolution = colorLUTResolution;
        this.cameraBufferSettings = cameraBufferSettings;
        this.postFXSettings = postFXSettings;
        this.shadowSettings = shadowSettings;
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
        foreach (var camera in cameras)
            renderer.Render(
                renderGraph, context, camera, cameraBufferSettings,
                useLightsPerObject, shadowSettings, postFXSettings, 
                colorLUTResolution
            );
        renderGraph.EndFrame();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposeForEditor();
        renderer.Dispose();
        renderGraph.Cleanup();
    }
}