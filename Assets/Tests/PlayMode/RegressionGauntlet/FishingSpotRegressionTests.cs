using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class FishingSpotRegressionTests
{
    [Test]
    public void RequiredChunkRectExpandsMarginOnEverySide()
    {
        RectInt rect = InvokeBuildRequiredChunkRect(new Vector2Int(0, 0), new Vector2Int(2, 3), 1);

        // min(0,0) and max(2,3) grown by 1 -> covers chunks x:[-1..3], y:[-1..4].
        Assert.AreEqual(-1, rect.xMin);
        Assert.AreEqual(-1, rect.yMin);
        Assert.AreEqual(5, rect.width, "width = (maxX-minX) + 2*margin + 1");
        Assert.AreEqual(6, rect.height, "height = (maxY-minY) + 2*margin + 1");

        // The far corner chunk (max + margin) must fall inside the rect.
        Assert.IsTrue(rect.Contains(new Vector2Int(3, 4)));
        Assert.IsTrue(rect.Contains(new Vector2Int(-1, -1)));
        Assert.IsFalse(rect.Contains(new Vector2Int(4, 4)));
    }

    [Test]
    public void RequiredChunkRectWithZeroMarginIsTheInclusiveSpan()
    {
        RectInt single = InvokeBuildRequiredChunkRect(new Vector2Int(0, 0), new Vector2Int(0, 0), 0);
        Assert.AreEqual(new RectInt(0, 0, 1, 1), single);

        RectInt span = InvokeBuildRequiredChunkRect(new Vector2Int(-2, 5), new Vector2Int(1, 7), 0);
        Assert.AreEqual(4, span.width);
        Assert.AreEqual(3, span.height);
    }

    [Test]
    public void LoadedChunkIsNeverEvictableEvenWhenFar()
    {
        bool evictable = InvokeIsChunkEvictable(
            Vector2.zero, new Vector2(10000f, 0f), 256f, isLoaded: true);
        Assert.IsFalse(evictable, "A loaded chunk must never be evicted, regardless of distance.");
    }

    [Test]
    public void UnloadedChunkIsEvictableOnlyBeyondTheRadius()
    {
        // Within the radius -> retained.
        Assert.IsFalse(InvokeIsChunkEvictable(
            Vector2.zero, new Vector2(200f, 0f), 256f, isLoaded: false));

        // Beyond the radius -> evictable.
        Assert.IsTrue(InvokeIsChunkEvictable(
            Vector2.zero, new Vector2(300f, 0f), 256f, isLoaded: false));

        // Exactly at the radius -> retained (strict greater-than).
        Assert.IsFalse(InvokeIsChunkEvictable(
            Vector2.zero, new Vector2(256f, 0f), 256f, isLoaded: false));
    }

    static bool InvokeIsChunkEvictable(Vector2 boatPosition, Vector2 chunkCenter, float radius, bool isLoaded)
    {
        MethodInfo method = ResolveType("FishingSpotSpawner")
            .GetMethod("IsChunkEvictable", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, "FishingSpotSpawner.IsChunkEvictable must exist.");
        return (bool)method.Invoke(null, new object[] { boatPosition, chunkCenter, radius, isLoaded });
    }

    static RectInt InvokeBuildRequiredChunkRect(Vector2Int minChunk, Vector2Int maxChunk, int margin)
    {
        MethodInfo method = ResolveType("FishingSpotSpawner")
            .GetMethod("BuildRequiredChunkRect", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, "FishingSpotSpawner.BuildRequiredChunkRect must exist.");
        return (RectInt)method.Invoke(null, new object[] { minChunk, maxChunk, margin });
    }

    static Type ResolveType(string typeName)
    {
        Type type = Type.GetType(typeName + ", Assembly-CSharp") ?? Type.GetType(typeName);
        Assert.NotNull(type, $"Could not resolve runtime type {typeName}.");
        return type;
    }
}
