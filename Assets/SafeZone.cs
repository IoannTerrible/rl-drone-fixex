using UnityEngine;

public class SafeZone : MonoBehaviour
{
    public float zoneRadius = 10f;
    public Material zoneMaterial;
    private static SafeZone instance;

    public static SafeZone Instance
    {
        get { return instance; }
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        Debug.Log("SafeZone Initialized on Plane");
        ApplyMaterial();
    }

    void ApplyMaterial()
    {
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null && zoneMaterial != null)
        {
            Material transparentMat = new Material(zoneMaterial);
            transparentMat.color = new Color(0f, 1f, 0f, 0.3f); // Green Transparent
            renderer.material = transparentMat;
        }
        else
        {
            Debug.LogError("MeshRenderer or Material missing on SafeZone.");
        }
    }

    public bool IsInSafeZone(Vector3 position)
    {
        float distance = Vector3.Distance(transform.position, position);
        return distance <= zoneRadius;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, zoneRadius);
    }
}
