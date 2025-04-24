using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class SkyboxPass
    {
        static readonly ProfilingSampler Sampler = new("Skybox");

        RendererListHandle list;

        void Render(RenderGraphContext context)
        {
            context.cmd.DrawRendererList(list);
            context.renderContext.ExecuteCommandBuffer(context.cmd);
            context.cmd.Clear();
        }

        public static void Record(
            RenderGraph renderGraph, 
            Camera camera,
            in CameraRendererTextures textures)
        {
            if (camera.clearFlags == CameraClearFlags.Skybox)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    Sampler.name, 
                    out SkyboxPass pass, 
                    Sampler);
            
                pass.list = builder.UseRendererList(renderGraph.CreateSkyboxRendererList(camera));
                builder.AllowPassCulling(false);
                builder.ReadWriteTexture(textures.ColorAttachment);
                builder.ReadTexture(textures.DepthAttachment);
                builder.SetRenderFunc<SkyboxPass>(static (pass, context) => pass.Render(context));
            }
        }
    }
}