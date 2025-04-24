using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Custom_RP.Runtime.Passes.Lighting
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct ForwardPlusTilesJob : IJobFor
    {
        [ReadOnly]
        public NativeArray<float4> LightBounds;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<int> TileData;

        public int OtherLightCount;

        public float2 TileScreenUVSize;

        public int MaxLightsPerTile;

        public int TilesPerRow;

        public int TileDataSize;

        public void Execute(int tileIndex)
        {
            int y = tileIndex / TilesPerRow;
            int x = tileIndex - y * TilesPerRow;
            var bounds = math.float4(x, y, x + 1, y + 1) * TileScreenUVSize.xyxy;

            int headerIndex = tileIndex * TileDataSize;
            int dataIndex = headerIndex;
            int lightsInTileCount = 0;

            for (int i = 0; i < OtherLightCount; i++)
            {
                float4 b = LightBounds[i];
                if (math.all(math.float4(b.xy, bounds.xy) <= math.float4(bounds.zw, b.zw)))
                {
                    TileData[++dataIndex] = i;
                    if (++lightsInTileCount >= MaxLightsPerTile)
                    {
                        break;
                    }
                }
            }
            TileData[headerIndex] = lightsInTileCount;
        }
    }
}