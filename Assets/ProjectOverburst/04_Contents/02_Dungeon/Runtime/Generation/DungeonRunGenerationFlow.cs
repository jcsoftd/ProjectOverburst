using System;
using DunGen;
using DunGen.Graph;
using UnityEngine;

public sealed class DungeonRunGenerationFlow : IDisposable
{
    public DungeonRunGenerationFlow(
        DungeonFlow source,
        DungeonRunParameters parameters)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        Flow = UnityEngine.Object.Instantiate(source);
        Flow.name = source.name + "_RuntimeFixedLength";
        Flow.hideFlags = HideFlags.DontSave;

        // 필수 주입은 기존 Main Path 슬롯을 Arena로 교체하며,
        // DunGen 내부의 targetLength 보정이 최종 길이를 유지한다.
        int totalTileCount = parameters.TotalMainPathTileCount;
        Flow.Length = new IntRange(totalTileCount, totalTileCount);
    }

    public DungeonFlow Flow { get; private set; }

    public void Dispose()
    {
        if (Flow == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(Flow);
        else
            UnityEngine.Object.DestroyImmediate(Flow);
        Flow = null;
    }
}
