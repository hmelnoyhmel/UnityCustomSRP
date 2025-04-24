using UnityEngine;
using UnityEngine.Rendering;

namespace Custom_RP.Runtime
{
    public readonly struct CameraRendererCopier
    {
        static readonly int SourceTextureID = Shader.PropertyToID("_SourceTexture");
        static readonly int SrcBlendID = Shader.PropertyToID("_CameraSrcBlend");
        static readonly int DstBlendID = Shader.PropertyToID("_CameraDstBlend");

        static readonly Rect FullViewRect = new(0f, 0f, 1f, 1f);
    
        static readonly bool CopyTextureSupported =
            SystemInfo.copyTextureSupport > CopyTextureSupport.None;

        public static bool RequiresRenderTargetResetAfterCopy => !CopyTextureSupported;

        public readonly Camera Camera => camera;
	
        readonly Material material;

        readonly Camera camera;

        readonly CameraSettings.FinalBlendMode finalBlendMode;
    
        public CameraRendererCopier(Material material, Camera camera, CameraSettings.FinalBlendMode finalBlendMode)
        {
            this.material = material;
            this.camera = camera;
            this.finalBlendMode = finalBlendMode;
        }

        public readonly void Copy(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            RenderTargetIdentifier to,
            bool isDepth)
        {
            if (CopyTextureSupported)
            {
                buffer.CopyTexture(from, to);
            }
            else
            {
                CopyByDrawing(buffer, from, to, isDepth);
            }
        }

        public readonly void CopyByDrawing(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            RenderTargetIdentifier to,
            bool isDepth)
        {
            buffer.SetGlobalTexture(SourceTextureID, from);
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.SetViewport(camera.pixelRect);
            buffer.DrawProcedural(
                Matrix4x4.identity, material, isDepth ? 1 : 0,
                MeshTopology.Triangles, 3);
        }
    
        public readonly void CopyToCameraTarget(
            CommandBuffer buffer,
            RenderTargetIdentifier from)
        {
            buffer.SetGlobalFloat(SrcBlendID, (float)finalBlendMode.source);
            buffer.SetGlobalFloat(DstBlendID, (float)finalBlendMode.destination);
            buffer.SetGlobalTexture(SourceTextureID, from);
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                finalBlendMode.destination == BlendMode.Zero && camera.rect == FullViewRect ?
                    RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store);
            buffer.SetViewport(camera.pixelRect);
            buffer.DrawProcedural(
                Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            buffer.SetGlobalFloat(SrcBlendID, 1f);
            buffer.SetGlobalFloat(DstBlendID, 0f);
        }
    }
}