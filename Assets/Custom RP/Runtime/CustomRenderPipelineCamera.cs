using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CustomRenderPipelineCamera : MonoBehaviour
{
    [SerializeField] private CameraSettings settings;

    public CameraSettings Settings => settings ??= new CameraSettings();
}