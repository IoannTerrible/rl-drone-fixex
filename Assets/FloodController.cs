using UnityEngine;

public class FloodController : MonoBehaviour
{
    public float riseSpeed = 0.1f; // Speed at which water rises
    public float maxWaterHeight = 10f; // Maximum flood level

    private float startHeight;

    void Start()
    {
        startHeight = transform.position.y; // Store initial height
    }

    void Update()
    {
        if (transform.position.y < maxWaterHeight)
        {
            transform.position += new Vector3(0, riseSpeed * Time.deltaTime, 0);
        }
    }
}
