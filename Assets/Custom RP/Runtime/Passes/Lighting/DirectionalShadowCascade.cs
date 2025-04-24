using System.Runtime.InteropServices;
using UnityEngine;

namespace Custom_RP.Runtime.Passes.Lighting
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DirectionalShadowCascade
    {
        public const int Stride = 4 * 4 * 2;

        public Vector4 cullingSphere, data;

        public DirectionalShadowCascade(
            Vector4 cullingSphere,
            float tileSize,
            float filterSize)
        {
            float texelSize = 2f * cullingSphere.w / tileSize;
            filterSize *= texelSize;
            cullingSphere.w -= filterSize;
            cullingSphere.w *= cullingSphere.w;
            this.cullingSphere = cullingSphere;
            data = new Vector4(1f / cullingSphere.w, filterSize * 1.4142136f);
        }
    }
}