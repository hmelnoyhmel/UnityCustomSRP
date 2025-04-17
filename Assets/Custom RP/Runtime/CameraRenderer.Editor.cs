using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

partial class CameraRenderer
{
    partial void PrepareForSceneWindow();
    

#if UNITY_EDITOR

    partial void PrepareForSceneWindow()
    {
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            useScaledRendering = false;
        }
    }

#else
	const string SampleName = bufferName;

#endif
}