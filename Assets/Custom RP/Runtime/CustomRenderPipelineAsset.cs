using UnityEngine;
using UnityEngine.Rendering;

namespace Custom_RP.Runtime
{
    [CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
    public class CustomRenderPipelineAsset : RenderPipelineAsset<CustomRenderPipeline>
    {
        [SerializeField]
        CustomRenderPipelineSettings settings;
    
        protected override RenderPipeline CreatePipeline() => new CustomRenderPipeline(settings);
    }
}