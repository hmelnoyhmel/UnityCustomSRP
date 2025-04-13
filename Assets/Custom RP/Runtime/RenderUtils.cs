using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
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
    private static ShaderTagId litShaderTagId = new ShaderTagId("CustomLit");
    
    public static ref readonly ShaderTagId UnlitShaderTagId => ref unlitShaderTagId;
    public static ref readonly ShaderTagId LitShaderTagId => ref litShaderTagId;

    public static string SampleName { get; private set; }
    
    public static void DrawUnsupportedShaders(ScriptableRenderContext context, Camera camera, CullingResults cullingResults)
    {
        if (errorMaterial == null) errorMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
        
        var drawingSettings = new DrawingSettings
        {
            sortingSettings = new SortingSettings(camera),
            overrideMaterial = errorMaterial
        };
        
        var counter = 1;
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
    
    public static void DrawGizmosBeforeFX(ScriptableRenderContext context, Camera camera) 
    {
        if (Handles.ShouldRenderGizmos()) 
        {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
        }
    }
    
    public static void DrawGizmosAfterFX(ScriptableRenderContext context, Camera camera) 
    {
        if (Handles.ShouldRenderGizmos()) 
        {
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }

    public static void PrepareForSceneWindow(Camera camera)
    {
        // draws UI in scene view
        if (camera.cameraType == CameraType.SceneView)
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
    }

    public static void PrepareBuffer(CommandBuffer buffer, Camera camera)
    {
        Profiler.BeginSample("Editor Only");
        // separates camera scopes if there's several cameras rendering
        buffer.name = SampleName = camera.name;
        Profiler.EndSample();
    }
    
#else
    public static string SampleName => bufferName;
#endif
}