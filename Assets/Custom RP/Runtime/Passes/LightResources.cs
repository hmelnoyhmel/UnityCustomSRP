using UnityEngine.Rendering.RenderGraphModule;

public readonly ref struct LightResources
{
    public readonly BufferHandle
        directionalLightDataBuffer, otherLightDataBuffer;
	
    public readonly ShadowResources shadowResources;

    public LightResources(
        BufferHandle directionalLightDataBuffer,
        BufferHandle otherLightDataBuffer,
        ShadowResources shadowResources)
    {
        this.directionalLightDataBuffer = directionalLightDataBuffer;
        this.otherLightDataBuffer = otherLightDataBuffer;
        this.shadowResources = shadowResources;
    }
}