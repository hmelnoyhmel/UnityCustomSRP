using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class CopyAttachmentsPass
    {
        static readonly ProfilingSampler Sampler = new("Copy Attachments");

        bool copyColor; 
        bool copyDepth;
	
        CameraRendererCopier copier;
    
        TextureHandle colorAttachment, depthAttachment, colorCopy, depthCopy;

        private static readonly int ColorCopyID = Shader.PropertyToID("_CameraColorTexture");
        static readonly int DepthCopyID = Shader.PropertyToID("_CameraDepthTexture");

        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            if (copyColor)
            {
                copier.Copy(buffer, colorAttachment, colorCopy, false);
                buffer.SetGlobalTexture(ColorCopyID, colorCopy);
            }
            if (copyDepth)
            {
                copier.Copy(buffer, depthAttachment, depthCopy, true);
                buffer.SetGlobalTexture(DepthCopyID, depthCopy);
            }
            if (CameraRendererCopier.RequiresRenderTargetResetAfterCopy)
            {
                buffer.SetRenderTarget(
                    colorAttachment,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                    depthAttachment,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
                );
            }
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(
            RenderGraph renderGraph, 
            bool copyColor,
            bool copyDepth,
            CameraRendererCopier copier,
            in CameraRendererTextures textures)
        {
            if (copyColor || copyDepth)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    Sampler.name,
                    out CopyAttachmentsPass pass,
                    Sampler);

                pass.copyColor = copyColor;
                pass.copyDepth = copyDepth;
                pass.copier = copier;
            
                pass.colorAttachment = builder.ReadTexture(textures.ColorAttachment);
                pass.depthAttachment = builder.ReadTexture(textures.DepthAttachment);
                if (copyColor)
                {
                    pass.colorCopy = builder.WriteTexture(textures.ColorCopy);
                }
                if (copyDepth)
                {
                    pass.depthCopy = builder.WriteTexture(textures.DepthCopy);
                }
            
                builder.SetRenderFunc<CopyAttachmentsPass>(static (pass, context) => pass.Render(context));
            }
        }
    }
}