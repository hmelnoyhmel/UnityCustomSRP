using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditorForRenderPipeline(typeof(Light), typeof(CustomRenderPipelineAsset))]
public class CustomLightEditor : LightEditor
{
    public override void OnInspectorGUI() 
    {
        base.OnInspectorGUI();
        
        if (settings.lightType.hasMultipleDifferentValues && 
            (LightType)settings.lightType.enumValueIndex != LightType.Spot)
            return;
        
        settings.DrawInnerAndOuterSpotAngle();
        settings.ApplyModifiedProperties();
        
    }
}