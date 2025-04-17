using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public class FinalPass
{
    static readonly ProfilingSampler sampler = new("Final");
    
    CameraRenderer renderer;

    CameraSettings.FinalBlendMode finalBlendMode;

    void Render(RenderGraphContext context)
    {
        renderer.DrawFinal(finalBlendMode);
        renderer.ExecuteBuffer();
    }

    public static void Record(
        RenderGraph renderGraph,
        CameraRenderer renderer,
        CameraSettings.FinalBlendMode finalBlendMode)
    {
        using RenderGraphBuilder builder = renderGraph.AddRenderPass(
            sampler.name,
            out FinalPass pass,
            sampler);
        
        pass.renderer = renderer;
        pass.finalBlendMode = finalBlendMode;
        builder.SetRenderFunc<FinalPass>((pass, context) => pass.Render(context));
    }
}