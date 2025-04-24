using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class GeometryPass
    {
        private static readonly ProfilingSampler SamplerOpaque = new("Opaque Geometry");
        static readonly ProfilingSampler SamplerTransparent = new("Transparent Geometry");

        static readonly ShaderTagId[] ShaderTagIds = 
        {
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
            ProfilingSampler sampler = opaque ? SamplerOpaque : SamplerTransparent;
        
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, 
                out GeometryPass pass, 
                sampler);
		
            var rednderlist = renderGraph.CreateRendererList(
                new RendererListDesc(ShaderTagIds, cullingResults, camera)
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
        
            builder.ReadWriteTexture(textures.ColorAttachment);
            builder.ReadWriteTexture(textures.DepthAttachment);
        
            if (!opaque)
            {
                if (textures.ColorCopy.IsValid())
                {
                    builder.ReadTexture(textures.ColorCopy);
                }
                if (textures.DepthCopy.IsValid())
                {
                    builder.ReadTexture(textures.DepthCopy);
                }
            }
        
        
            builder.ReadBuffer(lightData.DirectionalLightDataBuffer);
            builder.ReadBuffer(lightData.OtherLightDataBuffer);
            if (lightData.TilesBuffer.IsValid())
            {
                builder.ReadBuffer(lightData.TilesBuffer);
            }
            builder.ReadTexture(lightData.ShadowResources.DirectionalAtlas);
        
            builder.ReadTexture(lightData.ShadowResources.OtherAtlas);
            builder.ReadBuffer(lightData.ShadowResources.DirectionalShadowCascadesBuffer);
            builder.ReadBuffer(lightData.ShadowResources.DirectionalShadowMatricesBuffer);
            builder.ReadBuffer(lightData.ShadowResources.OtherShadowDataBuffer);
        
            builder.SetRenderFunc<GeometryPass>(static (pass, context) => pass.Render(context));
        }
    }
}