using Custom_RP.Runtime;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

[CanEditMultipleObjects]
[CustomEditor(typeof(Camera))]
[SupportedOnRenderPipeline(typeof(CustomRenderPipelineAsset))]
public class CustomCameraEditor : Editor
{
    
}