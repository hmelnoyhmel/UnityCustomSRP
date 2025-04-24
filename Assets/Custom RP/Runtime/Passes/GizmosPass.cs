using System.Diagnostics;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class GizmosPass
    {
#if UNITY_EDITOR
    
        static readonly ProfilingSampler Sampler = new("Gizmos");

        CameraRendererCopier copier;

        TextureHandle depthAttachment;

        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            ScriptableRenderContext renderContext = context.renderContext;
        
            copier.CopyByDrawing(
                buffer, depthAttachment, BuiltinRenderTextureType.CameraTarget, true);
            renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        
            renderContext.DrawGizmos(copier.Camera, GizmoSubset.PreImageEffects);
            renderContext.DrawGizmos(copier.Camera, GizmoSubset.PostImageEffects);
        }
#endif
    
  
        [Conditional("UNITY_EDITOR")]
        public static void Record(
            RenderGraph renderGraph,
            CameraRendererCopier copier,
            in CameraRendererTextures textures)
        {
#if UNITY_EDITOR
            if (Handles.ShouldRenderGizmos())
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    Sampler.name, 
                    out GizmosPass pass,
                    Sampler);
            
            
                pass.copier = copier;
                pass.depthAttachment = builder.ReadTexture(textures.DepthAttachment);
                
                builder.SetRenderFunc<GizmosPass>(static (pass, context) => pass.Render(context));
            }
#endif
        }  
    }
}