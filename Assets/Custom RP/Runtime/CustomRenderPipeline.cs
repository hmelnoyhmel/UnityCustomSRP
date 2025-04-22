using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public partial class CustomRenderPipeline : RenderPipeline
{
    private readonly RenderGraph renderGraph = new("Custom SRP Render Graph");
    
    private readonly CameraRenderer renderer;

    private readonly CustomRenderPipelineSettings settings;
    
    public CustomRenderPipeline(CustomRenderPipelineSettings settings)
    {
        this.settings = settings;
        GraphicsSettings.useScriptableRenderPipelineBatching = settings.useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        InitializeForEditor();
        renderer = new CameraRenderer(settings.cameraRendererShader, settings.cameraDebuggerShader);
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
    }

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (var camera in cameras)
            renderer.Render(renderGraph, context, camera, settings);
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