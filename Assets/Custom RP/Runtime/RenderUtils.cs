using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RenderUtils
{
#if UNITY_EDITOR
    
    private static readonly ShaderTagId[] legacyShaderTagIds = {
        new ShaderTagId("Always"),
        new ShaderTagId("ForwardBase"),
        new ShaderTagId("PrepassBase"),
        new ShaderTagId("Vertex"),
        new ShaderTagId("VertexLMRGBM"),
        new ShaderTagId("VertexLM")
    };
    
    private static Material errorMaterial;
    
    private static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");
    public static ref readonly ShaderTagId UnlitShaderTagId => ref unlitShaderTagId;
    
    public static void DrawUnsupportedShaders(ScriptableRenderContext context, Camera camera, CullingResults cullingResults)
    {
        if (errorMaterial == null) errorMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
        
        var drawingSettings = new DrawingSettings
        {
            sortingSettings = new SortingSettings(camera),
            overrideMaterial = errorMaterial
        };


        int counter = 1;
        foreach (var tagId in RenderUtils.legacyShaderTagIds)
        {
            drawingSettings.SetShaderPassName(counter, tagId);
            counter++;
        }
        
        var filteringSettings = FilteringSettings.defaultValue;
        
        context.DrawRenderers(
            cullingResults, ref drawingSettings, ref filteringSettings
        );
    }
    
    public static void DrawGizmos(ScriptableRenderContext context, Camera camera) 
    {
        if (Handles.ShouldRenderGizmos()) {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }
    
#endif
}