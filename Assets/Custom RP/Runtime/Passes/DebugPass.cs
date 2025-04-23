using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class DebugPass
{
    static readonly ProfilingSampler sampler = new("Debug");

    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    public static void Record(
        RenderGraph renderGraph,
        CustomRenderPipelineSettings settings,
        Camera camera,
        in LightResources lightData)
    {
        if (CameraDebugger.IsActive && camera.cameraType <= CameraType.SceneView)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, 
                out DebugPass pass, 
                sampler);
            builder.ReadBuffer(lightData.tilesBuffer);
            builder.SetRenderFunc<DebugPass>(static (pass, context) => CameraDebugger.Render(context));
        }
    }
}