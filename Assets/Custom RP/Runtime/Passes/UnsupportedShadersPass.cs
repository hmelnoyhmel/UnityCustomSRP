using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class UnsupportedShadersPass
    {
#if UNITY_EDITOR
        static readonly ProfilingSampler Sampler = new("Unsupported Shaders");
    
        static readonly ShaderTagId[] ShaderTagIds = {
            new("Always"),
            new("ForwardBase"),
            new("PrepassBase"),
            new("Vertex"),
            new("VertexLMRGBM"),
            new("VertexLM")
        };

        static Material errorMaterial;
    
        RendererListHandle list;

        void Render(RenderGraphContext context)
        {
            context.cmd.DrawRendererList(list);
            context.renderContext.ExecuteCommandBuffer(context.cmd);
            context.cmd.Clear();
        }
    
#endif

        [Conditional("UNITY_EDITOR")]
        public static void Record(RenderGraph renderGraph, Camera camera, CullingResults cullingResults)
        {
#if UNITY_EDITOR
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                Sampler.name, 
                out UnsupportedShadersPass pass,
                Sampler);
        
            if (errorMaterial == null)
            {
                errorMaterial = new(Shader.Find("Hidden/InternalErrorShader"));
            }

            pass.list = builder.UseRendererList(renderGraph.CreateRendererList(
                new RendererListDesc(ShaderTagIds, cullingResults, camera)
                {
                    overrideMaterial = errorMaterial,
                    renderQueueRange = RenderQueueRange.all
                }));
        
            builder.SetRenderFunc<UnsupportedShadersPass>(static (pass, context) => pass.Render(context));
#endif
        }
    }
}