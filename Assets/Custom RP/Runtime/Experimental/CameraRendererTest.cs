using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public abstract class CustomPixelPassTest<TData> : IRenderGraphRecorder where TData : class, new()
{
    private readonly BaseRenderFunc<TData, RasterGraphContext> renderFunc;

    protected CustomPixelPassTest()
    {
        renderFunc = Render;
    }

    public void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        using (var builder = renderGraph.AddRasterRenderPass<TData>(GetType().Name, out var passData))
        {
            Setup(passData, builder, frameData);
            builder.SetRenderFunc(renderFunc);
        }
    }

    protected abstract void Setup(TData data, IRasterRenderGraphBuilder builder, ContextContainer frameData);
    protected abstract void Render(TData data, RasterGraphContext context);
    
}

public class CameraRendererTest
{
    private ScriptableRenderContext context;
    private Camera camera;
    private readonly BaseRenderFunc<SkyboxPassData, RasterGraphContext> _renderFunc; // possible generic conversion
    
    public CameraRendererTest()
    {
        _renderFunc = Render;
    }
    
    private class SkyboxPassData
    {
        public RendererListHandle skybox;
        public TextureHandle renderTarget;
    }
    
    public void Render(ScriptableRenderContext context, Camera camera, RenderGraph renderGraph)
    {
        this.context = context;
        this.camera = camera;

        DrawVisibleGeometry(renderGraph);
        
        
        //Submit();
    }
    
    void DrawVisibleGeometry(RenderGraph renderGraph)
    {
        using (var builder = renderGraph.AddRasterRenderPass<SkyboxPassData>("SkyboxPass", out var passData))
        {
            passData.skybox = renderGraph.CreateSkyboxRendererList(camera);
            builder.UseRendererList(passData.skybox);
            
            var targetColorID = new RenderTargetIdentifier(camera.targetTexture);
            RTHandle targetTexture = null;
            RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref targetTexture, targetColorID);
            
            passData.renderTarget = renderGraph.ImportTexture(targetTexture);
            //builder.UseTexture(passData.renderTarget);
            
            builder.SetRenderAttachment(passData.renderTarget, 0, AccessFlags.ReadWrite);
            builder.SetRenderFunc(_renderFunc);
        }
        
        // context.DrawSkybox(camera); // obsolete method, replace later
    }

    private void Render(SkyboxPassData data, RasterGraphContext rendercontext)
    {
        rendercontext.cmd.DrawRendererList(data.skybox);
    }
    
    void Submit()
    {
        context.Submit();
    }
}
