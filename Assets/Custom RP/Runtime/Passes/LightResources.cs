using UnityEngine.Rendering.RenderGraphModule;

public readonly ref struct LightResources
{
    public readonly BufferHandle directionalLightDataBuffer;
    public readonly BufferHandle otherLightDataBuffer;
    public readonly BufferHandle tilesBuffer;
	
    public readonly ShadowResources shadowResources;

    public LightResources(
        BufferHandle directionalLightDataBuffer,
        BufferHandle otherLightDataBuffer,
        BufferHandle tilesBuffer,
        ShadowResources shadowResources)
    {
        this.directionalLightDataBuffer = directionalLightDataBuffer;
        this.otherLightDataBuffer = otherLightDataBuffer;
        this.tilesBuffer = tilesBuffer;
        this.shadowResources = shadowResources;
    }
}