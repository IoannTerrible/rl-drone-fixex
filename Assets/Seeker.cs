using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class Seeker : MonoBehaviour
{
    [Header("Navigation Settings")]
    public Transform target;
    public float rotationSpeed = 20f;
    public float reachedDistance = 1f;
    public float pathUpdateInterval = 0.5f;

    [Header("Height Adjustment")]
    public float heightAboveGround = 5f;
    public float heightAdjustmentSpeed = 5f;
    public LayerMask groundLayer;
    public float maxRaycastDistance = 100f;

    private NavMeshAgent agent;
    private float lastPathUpdateTime;
    private Vector3 lastTargetPos;
    private bool isHeightAdjusting = false;

    void Start()
    {
        InitializeAgent();
    }

    void InitializeAgent()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError("NavMeshAgent component is missing on " + gameObject.name);
            enabled = false;
            return;
        }

        agent.autoBraking = false;
        agent.updateUpAxis = true;
        agent.updateRotation = false; // We'll handle rotation manually
        agent.baseOffset = heightAboveGround;
    }

    void Update()
    {
        if (target == null) return;

        AdjustHeight();
        UpdatePath();
        RotateTowardsTarget();
        UpdateMovement();
    }

    void UpdatePath()
    {
        // Update path if enough time has passed or target has moved significantly
        if (Time.time - lastPathUpdateTime >= pathUpdateInterval ||
            Vector3.Distance(target.position, lastTargetPos) > 0.5f)
        {
            Vector3 targetPos = target.position;
            targetPos.y = transform.position.y; // Keep target at current height
            agent.SetDestination(targetPos);
            
            lastPathUpdateTime = Time.time;
            lastTargetPos = target.position;
        }
    }

    void AdjustHeight()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, maxRaycastDistance, groundLayer))
        {
            float targetHeight = hit.point.y + heightAboveGround;
            float currentHeight = transform.position.y;
            
            if (Mathf.Abs(targetHeight - currentHeight) > 0.1f)
            {
                isHeightAdjusting = true;
                Vector3 newPos = transform.position;
                newPos.y = Mathf.Lerp(currentHeight, targetHeight, Time.deltaTime * heightAdjustmentSpeed);
                transform.position = newPos;
                
                // Update agent's base offset
                agent.baseOffset = heightAboveGround;
            }
            else
            {
                isHeightAdjusting = false;
            }
        }
    }

    void UpdateMovement()
    {
        if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            RecalculatePath();
            return;
        }

        // Adjust agent speed based on height adjustment
        agent.speed = isHeightAdjusting ? agent.speed * 0.5f : agent.speed;
    }

    void RotateTowardsTarget()
    {
        if (agent.velocity.magnitude < 0.1f) return;

        Vector3 direction = (target.position - transform.position).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 
                Time.deltaTime * rotationSpeed);
        }
    }

    void RecalculatePath()
    {
        NavMeshPath newPath = new NavMeshPath();
        Vector3 targetPos = target.position;
        targetPos.y = transform.position.y;

        if (NavMesh.CalculatePath(transform.position, targetPos, agent.areaMask, newPath))
        {
            agent.SetPath(newPath);
        }
    }

    public bool HasReachedTarget()
    {
        if (target == null) return false;
        
        return !agent.pathPending && 
               agent.remainingDistance <= reachedDistance &&
               !isHeightAdjusting;
    }

    public void SetNewTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            Vector3 targetPos = target.position;
            targetPos.y = transform.position.y;
            agent.SetDestination(targetPos);
            lastTargetPos = targetPos;
        }
    }

    void OnDrawGizmos()
    {
        // Draw height adjustment ray
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * maxRaycastDistance);

        // Draw target reached radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, reachedDistance);

        // Draw path to target
        if (target != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, target.position);
        }
    }
}