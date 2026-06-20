using UnityEngine;

// Marks something in the water as a local blocker/dampener for the shared
// water field. Obstacles do not emit disturbances themselves; instead they
// weaken nearby field strength and gently bend flow away from their center.
public class WaterObstacle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Collider2D sourceCollider;

    [Header("Influence")]
    [SerializeField] bool deriveRadiusFromCollider = true;
    [SerializeField] float influenceRadius = 1f;
    [SerializeField] [Range(0f, 1f)] float disturbanceDamping = 0.75f;
    [SerializeField] [Range(0f, 1f)] float flowDeflection = 0.35f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] float debugEffectiveRadius = 1f;

    public float DisturbanceDamping => disturbanceDamping;
    public float FlowDeflection => flowDeflection;
    public Vector2 Position => sourceCollider != null ? sourceCollider.bounds.center : transform.position;

    public float EffectiveRadius
    {
        get
        {
            float radius = influenceRadius;
            if (deriveRadiusFromCollider && sourceCollider != null)
            {
                Vector3 extents = sourceCollider.bounds.extents;
                radius = Mathf.Max(extents.x, extents.y);
            }

            return Mathf.Max(radius, 0.05f);
        }
    }

    void Reset()
    {
        sourceCollider = GetComponent<Collider2D>();
    }

    void OnEnable()
    {
        debugEffectiveRadius = EffectiveRadius;

        if (WaveController.ActiveController != null)
            WaveController.ActiveController.RegisterObstacle(this);
    }

    void OnDisable()
    {
        if (WaveController.ActiveController != null)
            WaveController.ActiveController.UnregisterObstacle(this);
    }

    void OnValidate()
    {
        influenceRadius = Mathf.Max(0.05f, influenceRadius);
        disturbanceDamping = Mathf.Clamp01(disturbanceDamping);
        flowDeflection = Mathf.Clamp01(flowDeflection);
        debugEffectiveRadius = EffectiveRadius;
    }
}
