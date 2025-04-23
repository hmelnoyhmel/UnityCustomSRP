using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
public class CustomRenderPipelineAsset : RenderPipelineAsset<CustomRenderPipeline>
{
    [SerializeField]
    CustomRenderPipelineSettings settings;
    
    protected override RenderPipeline CreatePipeline() => new CustomRenderPipeline(settings);
}