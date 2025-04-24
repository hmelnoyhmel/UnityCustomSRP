using System.Runtime.InteropServices;
using UnityEngine;

namespace Custom_RP.Runtime.Passes.Lighting
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct OtherShadowData
    {
        public const int Stride = 4 * 4 + 4 * 16;

        public Vector4 tileData;

        public Matrix4x4 shadowMatrix;

        public OtherShadowData(
            Vector2 offset,
            float scale,
            float bias,
            float border,
            Matrix4x4 matrix)
        {
            tileData.x = offset.x * scale + border;
            tileData.y = offset.y * scale + border;
            tileData.z = scale - border - border;
            tileData.w = bias;
            shadowMatrix = matrix;
        }
    }
}