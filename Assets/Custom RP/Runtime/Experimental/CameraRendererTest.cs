using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Experimental
{
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
        private readonly BaseRenderFunc<SkyboxPassData, RasterGraphContext> renderFunc; // possible generic conversion
        private Camera camera;
        private ScriptableRenderContext context;

        public CameraRendererTest()
        {
            renderFunc = Render;
        }

        public void Render(ScriptableRenderContext context, Camera camera, RenderGraph renderGraph)
        {
            this.context = context;
            this.camera = camera;

            DrawVisibleGeometry(renderGraph);


            //Submit();
        }

        private void DrawVisibleGeometry(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRasterRenderPass<SkyboxPassData>("SkyboxPass", out var passData))
            {
                passData.Skybox = renderGraph.CreateSkyboxRendererList(camera);
                builder.UseRendererList(passData.Skybox);

                var targetColorID = new RenderTargetIdentifier(camera.targetTexture);
                RTHandle targetTexture = null;
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref targetTexture, targetColorID);

                passData.RenderTarget = renderGraph.ImportTexture(targetTexture);
                //builder.UseTexture(passData.renderTarget);

                builder.SetRenderAttachment(passData.RenderTarget, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(renderFunc);
            }

            // context.DrawSkybox(camera); // obsolete method, replace later
        }

        private void Render(SkyboxPassData data, RasterGraphContext rendercontext)
        {
            rendercontext.cmd.DrawRendererList(data.Skybox);
        }

        private void Submit()
        {
            context.Submit();
        }

        private class SkyboxPassData
        {
            public TextureHandle RenderTarget;
            public RendererListHandle Skybox;
        }
    }
}