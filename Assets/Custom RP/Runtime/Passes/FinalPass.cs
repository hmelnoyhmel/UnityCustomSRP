using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class FinalPass
    {
        static readonly ProfilingSampler Sampler = new("Final");
    
        CameraRendererCopier copier;

        TextureHandle colorAttachment;

        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            copier.CopyToCameraTarget(buffer, colorAttachment);
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            CameraRendererCopier copier,
            in CameraRendererTextures textures)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                Sampler.name,
                out FinalPass pass,
                Sampler);
        
            pass.copier = copier;
            pass.colorAttachment = builder.ReadTexture(textures.ColorAttachment);
            builder.SetRenderFunc<FinalPass>(static (pass, context) => pass.Render(context));
        }
    }
}