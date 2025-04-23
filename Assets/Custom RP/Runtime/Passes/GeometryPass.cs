using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

public class GeometryPass
{
    private static readonly ProfilingSampler samplerOpaque = new("Opaque Geometry");
    static readonly ProfilingSampler samplerTransparent = new("Transparent Geometry");

    static readonly ShaderTagId[] shaderTagIds = {
        new("SRPDefaultUnlit"),
        new("CustomLit")
    };

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
        CullingResults cullingResults,
        uint renderingLayerMask, 
        bool opaque, 
        in CameraRendererTextures textures, 
        in LightResources lightData)
    {
        ProfilingSampler sampler = opaque ? samplerOpaque : samplerTransparent;
        
        using RenderGraphBuilder builder = renderGraph.AddRenderPass(
            sampler.name, 
            out GeometryPass pass, 
            sampler);
		
        var rednderlist = renderGraph.CreateRendererList(
            new RendererListDesc(shaderTagIds, cullingResults, camera)
            {
                sortingCriteria = opaque ? 
                    SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent,
                
                rendererConfiguration =
                    PerObjectData.ReflectionProbes |
                    PerObjectData.Lightmaps |
                    PerObjectData.ShadowMask |
                    PerObjectData.LightProbe |
                    PerObjectData.OcclusionProbe |
                    PerObjectData.LightProbeProxyVolume |
                    PerObjectData.OcclusionProbeProxyVolume,
                
                renderQueueRange = opaque ?
                    RenderQueueRange.opaque : RenderQueueRange.transparent,
                renderingLayerMask = (uint)renderingLayerMask
            });

        pass.list = builder.UseRendererList(rednderlist);
        
        builder.ReadWriteTexture(textures.colorAttachment);
        builder.ReadWriteTexture(textures.depthAttachment);
        
        if (!opaque)
        {
            if (textures.colorCopy.IsValid())
            {
                builder.ReadTexture(textures.colorCopy);
            }
            if (textures.depthCopy.IsValid())
            {
                builder.ReadTexture(textures.depthCopy);
            }
        }
        
        
        builder.ReadBuffer(lightData.directionalLightDataBuffer);
        builder.ReadBuffer(lightData.otherLightDataBuffer);
        if (lightData.tilesBuffer.IsValid())
        {
            builder.ReadBuffer(lightData.tilesBuffer);
        }
        builder.ReadTexture(lightData.shadowResources.directionalAtlas);
        
        builder.ReadTexture(lightData.shadowResources.otherAtlas);
        builder.ReadBuffer(lightData.shadowResources.directionalShadowCascadesBuffer);
        builder.ReadBuffer(lightData.shadowResources.directionalShadowMatricesBuffer);
        builder.ReadBuffer(lightData.shadowResources.otherShadowDataBuffer);
        
        builder.SetRenderFunc<GeometryPass>(static (pass, context) => pass.Render(context));
    }
}