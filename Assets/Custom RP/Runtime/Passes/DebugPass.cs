using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class DebugPass
    {
        static readonly ProfilingSampler Sampler = new("Debug");

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Record(
            RenderGraph renderGraph,
            CustomRenderPipelineSettings settings,
            Camera camera,
            in LightResources lightData)
        {
            if (CameraDebugger.IsActive && camera.cameraType <= CameraType.SceneView)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    Sampler.name, 
                    out DebugPass pass, 
                    Sampler);
                builder.ReadBuffer(lightData.TilesBuffer);
                builder.SetRenderFunc<DebugPass>(static (pass, context) => CameraDebugger.Render(context));
            }
        }
    }
}