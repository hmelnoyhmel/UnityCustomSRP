using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class SetupPass
{
    static readonly ProfilingSampler sampler = new("Setup");
    
    static readonly int attachmentSizeID = Shader.PropertyToID("_CameraBufferSize");
    
    bool useIntermediateAttachments;
	
    TextureHandle colorAttachment, depthAttachment;

    Vector2Int attachmentSize;
	
    Camera camera;
	
    CameraClearFlags clearFlags;
    
    void Render(RenderGraphContext context) 
    {
        context.renderContext.SetupCameraProperties(camera);
        CommandBuffer cmd = context.cmd;
        cmd.SetRenderTarget(
            colorAttachment,
            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
            depthAttachment, 
            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        
        cmd.ClearRenderTarget(
            clearFlags <= CameraClearFlags.Depth,
            clearFlags <= CameraClearFlags.Color,
            clearFlags == CameraClearFlags.Color ?
                camera.backgroundColor.linear : Color.clear
        );
        cmd.SetGlobalVector(attachmentSizeID, new Vector4(
            1f / attachmentSize.x, 1f / attachmentSize.y,
            attachmentSize.x, attachmentSize.y
        ));
        context.renderContext.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }

    public static CameraRendererTextures Record(
        RenderGraph renderGraph,
        bool copyColor,
        bool copyDepth,
        bool useHDR,
        Vector2Int attachmentSize,
        Camera camera)
    {
        using RenderGraphBuilder builder = renderGraph.AddRenderPass(
            sampler.name, 
            out SetupPass pass,
            sampler);
        
        pass.attachmentSize = attachmentSize;
        pass.camera = camera;
        pass.clearFlags = camera.clearFlags;
        
        TextureHandle colorCopy = default;
        TextureHandle depthCopy = default;
        
        
            if (pass.clearFlags > CameraClearFlags.Color)
            {
                pass.clearFlags = CameraClearFlags.Color;
            }
            
            // reusable variable for texture setup
            var desc = new TextureDesc(attachmentSize.x, attachmentSize.y)
            {
                colorFormat = SystemInfo.GetGraphicsFormat(useHDR ? DefaultFormat.HDR : DefaultFormat.LDR),
                name = "Color Attachment"
            };
            // colorAttachment is a TextureHandle
            pass.colorAttachment = builder.WriteTexture(renderGraph.CreateTexture(desc)); 
            if (copyColor)
            {
                desc.name = "Color Copy";
                colorCopy = renderGraph.CreateTexture(desc);
            }
            
            desc.depthBufferBits = DepthBits.Depth32;
            desc.name = "Depth Attachment";
            pass.depthAttachment = builder.WriteTexture(renderGraph.CreateTexture(desc));
            if (copyDepth)
            {
                desc.name = "Depth Copy";
                depthCopy = renderGraph.CreateTexture(desc);
            }
        
        builder.AllowPassCulling(false);
        builder.SetRenderFunc<SetupPass>(static (pass, context) => pass.Render(context));
        
        return new CameraRendererTextures(
            pass.colorAttachment, pass.depthAttachment, colorCopy, depthCopy);
    }
}