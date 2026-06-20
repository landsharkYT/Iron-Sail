using UnityEngine;

[DisallowMultipleComponent]
public class FishingSpotController : MonoBehaviour
{
    public FishingSpotSpawner Owner => owner;
    public Vector2Int ChunkCoord => chunkCoord;
    public int SpotIndex => spotIndex;
    public bool IsCoastal => isCoastal;
    public bool IsAvailable => isAvailable;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] Vector2Int debugChunkCoord;
    [SerializeField] int debugSpotIndex = -1;
    [SerializeField] bool debugIsCoastal;
    [SerializeField] bool debugIsAvailable;

    FishingSpotSpawner owner;
    Vector2Int chunkCoord;
    int spotIndex = -1;
    bool isCoastal;
    bool isAvailable;

    public void Initialize(FishingSpotSpawner ownerSpawner, Vector2Int sourceChunkCoord, int sourceSpotIndex, bool coastal)
    {
        owner = ownerSpawner;
        chunkCoord = sourceChunkCoord;
        spotIndex = sourceSpotIndex;
        isCoastal = coastal;
        isAvailable = true;

        debugChunkCoord = chunkCoord;
        debugSpotIndex = spotIndex;
        debugIsCoastal = isCoastal;
        debugIsAvailable = isAvailable;
    }

    public void MarkConsumed()
    {
        isAvailable = false;
        debugIsAvailable = false;
    }
}
