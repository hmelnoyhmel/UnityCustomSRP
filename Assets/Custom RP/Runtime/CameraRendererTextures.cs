using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime
{
    public readonly ref struct CameraRendererTextures
    {
        public readonly TextureHandle
            ColorAttachment, DepthAttachment,
            ColorCopy, DepthCopy;

        public CameraRendererTextures(
            TextureHandle colorAttachment,
            TextureHandle depthAttachment,
            TextureHandle colorCopy,
            TextureHandle depthCopy)
        {
            this.ColorAttachment = colorAttachment;
            this.DepthAttachment = depthAttachment;
            this.ColorCopy = colorCopy;
            this.DepthCopy = depthCopy;
        }
    }
}