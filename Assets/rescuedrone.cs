using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;

public class RescueDrone : Agent
{


    // Add this method
    private void MoveDirectlyToTarget()
    {
        if (currentTarget == null)
        {
            Debug.Log("No target available, searching for survivors...");
            UpdateDetectedSurvivors();
            SetNextTarget();
            return;
        }
        
        Vector3 directionToTarget = (currentTarget.position - transform.position).normalized;
        Vector3 oldPosition = transform.position;
        
        // Add height adjustment to movement
        float currentHeight = transform.position.y;
        float targetHeight = currentTarget.position.y + desiredHeightAboveGround;
        float newHeight = Mathf.Lerp(currentHeight, targetHeight, heightAdjustmentSpeed * Time.deltaTime);
        
        Vector3 movement = directionToTarget * moveSpeed * Time.deltaTime;
        movement.y = newHeight - currentHeight;
        transform.position += movement;
        
        // Check if stuck
        if (Vector3.Distance(oldPosition, transform.position) < 0.01f)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer > 3f)
            {
                Debug.LogWarning("Drone stuck for too long, forcing next target");
                SetNextTarget();
                stuckTimer = 0f;
                return;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        // Update rotation
        transform.rotation = Quaternion.Slerp(transform.rotation, 
                                            Quaternion.LookRotation(directionToTarget), 
                                            rotationSpeed * Time.deltaTime);
        
        // Check interaction distance
        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);
        if (distanceToTarget < 3f)
        {
            if (!isSignaling && !isDeliveringResources)
            {
                if (resourceRecipientList.Contains(currentTarget))
                {
                    Debug.Log("Starting resource delivery");
                    StartResourceDelivery();
                }
                else
                {
                    Debug.Log("Starting signaling");
                    StartSignaling();
                }
            }
        }
    }

    void Update()
    {
        // Debug checks for all three points
        if (Time.frameCount % 300 == 0)  // Check every 60 frames to avoid spam
        {
            // 1. Check survivors in scene
            GameObject[] survivors = GameObject.FindGameObjectsWithTag("Survivor");
            Debug.Log($"Total survivors in scene: {survivors.Length}");
            foreach (var survivor in survivors)
            {
                Debug.Log($"Survivor: {survivor.name} at {survivor.transform.position}, Active: {survivor.activeInHierarchy}");
            }

            // 2. Check targeting system
            Debug.Log($"Detected survivors: {detectedSurvivors.Count}");
            Debug.Log($"Priority list count: {priorityRescueList.Count}");
            Debug.Log($"Current target: {(currentTarget != null ? currentTarget.name : "None")}");

            // 3. Check drone status
            Debug.Log($"Drone position: {transform.position}");
            Debug.Log($"Path corners: {(pathCorners != null ? pathCorners.Length : 0)}");
            Debug.Log($"Current path index: {currentPathIndex}");
        }
        if (Time.frameCount % 60==0)
        {
            UpdateDetectedSurvivors();
        }
         // Add regular survivor detection updates
        
        if (currentTarget != null)
        {
            targetReachTimer += Time.deltaTime;
            if (targetReachTimer > targetReachTimeout)
            {
                Debug.LogWarning($"Timed out trying to reach target {currentTarget.name}. Moving to next target.");
                SetNextTarget();
                targetReachTimer = 0f;
            }
            
            // Add position check
            if (Vector3.Distance(transform.position, currentTarget.position) < 0.1f)
            {
                Debug.LogWarning("Drone reached exact target position but no interaction occurred, forcing next target");
                SetNextTarget();
            }
        }
        else
        {
            if (Time.frameCount % 60==0)
            {
                Debug.Log("No current target, searching for survivors...");
                UpdateDetectedSurvivors();
                SetNextTarget();
            }
            
        }

        Debug.Log($"Current Target: {(currentTarget != null ? currentTarget.name : "None")}");
        Debug.Log($"Drone Position: {transform.position}, Target Position: {(currentTarget != null ? currentTarget.position.ToString() : Vector3.zero.ToString())}");
    }
    private float targetTimeoutDuration = 30f; // 30 seconds
    private float targetTimer = 0f;


    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 100f;
    public float heightAdjustmentSpeed = 5f;
    public float desiredHeightAboveGround = 5f;
    public float pathUpdateInterval = 3.0f;
    private float targetReachTimeout = 60f;
    private float targetReachTimer = 0f;
    public float stoppingDistance = 1f; // Adjust this value as needed
    private float stuckTimer = 0f;

    [Header("Detection Settings")]
    public float detectionRadius = 50f;
    public LayerMask groundLayer;
    public LayerMask survivorLayer;

    [Header("Action Settings")]
    public float signalDuration = 3f;
    public float resourceDropTime = 2f;

    // Navigation components
    private NavMeshPath path;
    private Vector3[] pathCorners;
    private int currentPathIndex;
    private float pathUpdateTimer;

    // State variables
    private Vector3 startPosition;
    private List<Transform> detectedSurvivors = new List<Transform>();
    private Transform currentTarget;
    private bool isSignaling = false;
    private float signalTimer = 0f;
    private bool isDeliveringResources = false;
    private float resourceDeliveryTimer = 0f;
    private Vector3 safeZonePosition;
    private List<Transform> priorityRescueList = new List<Transform>();
    private List<Transform> resourceRecipientList = new List<Transform>();
    private int rescuedSurvivors = 0;
    private int totalSurvivors = 0;
    private float episodeTimer = 0f;

    // Debug visualization
    private LineRenderer pathRenderer;
    public bool showPathDebug = true;
    

    void Start()
    {
        // Initialize NavMesh path
        path = new NavMeshPath();
        pathCorners = new Vector3[0];
        currentPathIndex = 0;
        pathUpdateTimer = 0f;

        // Store starting position
        startPosition = transform.position;

        // Find safe zone
        GameObject safeZone = GameObject.FindGameObjectWithTag("SafeZone");
        if (safeZone != null)
        {
            safeZonePosition = safeZone.transform.position;
        }
        else
        {
            Debug.LogError("Safe zone not found! Please tag a safe zone object with 'SafeZone' tag.");
        }

        // Setup path visualization
        if (showPathDebug)
        {
            pathRenderer = gameObject.AddComponent<LineRenderer>();
            pathRenderer.startWidth = 0.2f;
            pathRenderer.endWidth = 0.2f;
            pathRenderer.material = new Material(Shader.Find("Sprites/Default"));
            pathRenderer.startColor = Color.yellow;
            pathRenderer.endColor = Color.yellow;
        }
    }

    public override void OnEpisodeBegin()
    {
        // Reset drone position and state
        transform.position = startPosition;
        transform.rotation = Quaternion.identity;
        isSignaling = false;
        isDeliveringResources = false;
        signalTimer = 0f;
        resourceDeliveryTimer = 0f;
        rescuedSurvivors = 0;
        episodeTimer = 0f;
        pathUpdateTimer = 0f;
        currentPathIndex = 0;

        // Clear all lists
        detectedSurvivors.Clear();
        priorityRescueList.Clear();
        resourceRecipientList.Clear();

        // Find only survivor clones (spawned survivors)
        GameObject[] survivors = GameObject.FindGameObjectsWithTag("Survivor");
        foreach (GameObject survivor in survivors)
        {
            if (survivor.name.Contains("(Clone)") && survivor.activeInHierarchy)
            {
                detectedSurvivors.Add(survivor.transform);
                Debug.Log($"Found active survivor clone: {survivor.name}");
            }
        }
        
        totalSurvivors = detectedSurvivors.Count;
        Debug.Log($"Total active survivor clones found: {totalSurvivors}");

        if (totalSurvivors > 0)
        {
            DeterminePriorityOrder();
            SetNextTarget();
            if (currentTarget != null)
            {
                CalculatePath();
            }
        }
    }

    // Change the access modifier from private to public
    public void UpdateDetectedSurvivors()
    {
        // Clear the current target if it's the spawnsurvivor or if target is too close to spawnsurvivor
        if (currentTarget != null)
        {
            GameObject spawner = GameObject.Find("spawnsurvivor");
            if (!currentTarget.name.Contains("(Clone)") || 
                (spawner != null && Vector3.Distance(currentTarget.position, spawner.transform.position) < 5f))
            {
                Debug.Log($"Clearing invalid target or target too close to spawner: {currentTarget.name}");
                currentTarget = null;
                SetNextTarget();
                return;
            }
        }

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, detectionRadius, survivorLayer);
        bool newSurvivorFound = false;

        foreach (var hitCollider in hitColliders)
        {
            Transform survivor = hitCollider.transform;
            if (survivor.gameObject.activeInHierarchy && 
                survivor.name.Contains("(Clone)") && 
                !detectedSurvivors.Contains(survivor))
            {
                detectedSurvivors.Add(survivor);
                newSurvivorFound = true;
                Debug.Log($"Found new survivor clone: {survivor.name} at {survivor.position}");
            }
        }

        // Remove null or inactive survivors
        detectedSurvivors.RemoveAll(s => s == null || !s.gameObject.activeInHierarchy);
        priorityRescueList.RemoveAll(s => s == null || !s.gameObject.activeInHierarchy);

        if (newSurvivorFound)
        {
            DeterminePriorityOrder();
        }
    }

    private void DeterminePriorityOrder()
    {
        if (detectedSurvivors.Count == 0) return;

        List<KeyValuePair<Transform, float>> survivorDistances = new List<KeyValuePair<Transform, float>>();

        foreach (Transform survivor in detectedSurvivors)
        {
            if (survivor != null && survivor.gameObject.activeInHierarchy)
            {
                float directDistance = Vector3.Distance(transform.position, survivor.position);
                survivorDistances.Add(new KeyValuePair<Transform, float>(survivor, directDistance));
            }
        }

        // Sort by distance
        priorityRescueList = survivorDistances
            .OrderBy(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        // Last 20% of survivors (or at least 1) get resources
        int resourceCount = Mathf.Max(1, Mathf.FloorToInt(detectedSurvivors.Count * 0.2f));
        resourceRecipientList = priorityRescueList.Skip(priorityRescueList.Count - resourceCount).ToList();

        Debug.Log($"Prioritized {priorityRescueList.Count} survivors, {resourceRecipientList.Count} will receive resources");
    }

    private float GetHorizontalDistance(Vector3 position1, Vector3 position2)
    {
        Vector2 pos1 = new Vector2(position1.x, position1.z);
        Vector2 pos2 = new Vector2(position2.x, position2.z);
        return Vector2.Distance(pos1, pos2);
    }

    // Remove this duplicate SetNextTarget method (around line 378)
    private void SetNextTarget()
    {
        targetTimer = 0f;
        
        // Check if there are any survivors in the priority list
        if (priorityRescueList.Count > 0)
        {
            // Filter out any non-clone survivors
            priorityRescueList.RemoveAll(s => s == null || !s.gameObject.activeInHierarchy || !s.name.Contains("(Clone)"));
            
            if (priorityRescueList.Count > 0)
            {
                // Set the next target
                currentTarget = priorityRescueList[0];
                Debug.Log($"Set new target: {currentTarget.name} at {currentTarget.position}");
                priorityRescueList.RemoveAt(0);
                
                // Calculate the path to the new target
                CalculatePath();
                return;
            }
        }
        
        // If we get here, there are no valid targets in the priority list
        // Try to find new survivors
        UpdateDetectedSurvivors();
        
        // If we still don't have any targets, clear the current target
        if (priorityRescueList.Count == 0)
        {
            currentTarget = null;
            pathCorners = new Vector3[0];
            currentPathIndex = 0;
            
            if (showPathDebug && pathRenderer != null)
            {
                pathRenderer.positionCount = 0;
            }
        }
    }

    private void CalculatePath()
    {
        if (currentTarget == null) return;

        Vector3 sourcePoint = transform.position;
        Vector3 targetPoint = currentTarget.position;

        // Add debug info
        Debug.Log($"Attempting to calculate path from {sourcePoint} to {targetPoint}");
        
        NavMeshHit hitSource, hitTarget;
        bool sourceOnMesh = NavMesh.SamplePosition(sourcePoint, out hitSource, 100f, NavMesh.AllAreas);
        bool targetOnMesh = NavMesh.SamplePosition(targetPoint, out hitTarget, 100f, NavMesh.AllAreas);
        
        Debug.Log($"Source on NavMesh: {sourceOnMesh} at {hitSource.position}");
        Debug.Log($"Target on NavMesh: {targetOnMesh} at {hitTarget.position}");

        bool pathSuccess = NavMesh.CalculatePath(sourcePoint, targetPoint, NavMesh.AllAreas, path);

        if (!pathSuccess || path.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogWarning($"Could not find complete path to target. Using direct path. Status: {path.status}");
            pathCorners = new Vector3[] { sourcePoint, targetPoint };
        }
        else
        {
            pathCorners = path.corners;
            Debug.Log($"Path calculated with {pathCorners.Length} corners. First: {pathCorners[0]}, Last: {pathCorners[pathCorners.Length-1]}");
        }

        currentPathIndex = 0;

        // Update path visualization
        if (showPathDebug && pathRenderer != null && pathCorners != null && pathCorners.Length > 0)
        {
            pathRenderer.positionCount = pathCorners.Length;
            for (int i = 0; i < pathCorners.Length; i++)
            {
                pathRenderer.SetPosition(i, pathCorners[i]);  // Don't modify Y position
            }
        }
    }

    private Vector3 GetNavMeshPoint(Vector3 position)
    {
        // Find closest point on NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, 10f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return position;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Drone's position and rotation
        sensor.AddObservation(transform.position);
        sensor.AddObservation(transform.forward);

        // Current path information
        if (pathCorners.Length > currentPathIndex)
        {
            Vector3 nextPathPoint = pathCorners[currentPathIndex];
            sensor.AddObservation(nextPathPoint);
            sensor.AddObservation(Vector3.Distance(transform.position, nextPathPoint));
            sensor.AddObservation(Vector3.Dot(transform.forward, (nextPathPoint - transform.position).normalized));
            sensor.AddObservation(1.0f); // Has path
        }
        else
        {
            sensor.AddObservation(transform.position); // No path point, use self position
            sensor.AddObservation(0.0f); // No distance
            sensor.AddObservation(0.0f); // No direction
            sensor.AddObservation(0.0f); // No path
        }

        // Target information
        if (currentTarget != null)
        {
            sensor.AddObservation(currentTarget.position);
            sensor.AddObservation(Vector3.Distance(transform.position, currentTarget.position));
            sensor.AddObservation(1.0f); // Has target
        }
        else
        {
            sensor.AddObservation(transform.position); // No target, use self position
            sensor.AddObservation(0.0f); // No distance
            sensor.AddObservation(0.0f); // No target
        }

        // Safe zone information
        sensor.AddObservation(safeZonePosition);
        sensor.AddObservation(Vector3.Distance(transform.position, safeZonePosition));

        // State information
        sensor.AddObservation(isSignaling ? 1.0f : 0.0f);
        sensor.AddObservation(isDeliveringResources ? 1.0f : 0.0f);
        sensor.AddObservation(rescuedSurvivors);
        sensor.AddObservation(totalSurvivors);

        // Path completion information
        sensor.AddObservation(currentPathIndex);
        sensor.AddObservation(pathCorners.Length > 0 ? pathCorners.Length : 0);
    }

    public void SetTarget(Transform target)
    {
        currentTarget = target;
        CalculatePath(); // Recalculate the path to the new target
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Update path less frequently
        pathUpdateTimer += Time.deltaTime;
        if (pathUpdateTimer >= pathUpdateInterval)
        {
            pathUpdateTimer = 0f;
            if (currentTarget != null)
            {
                CalculatePath();
            }
        }

        // Movement logic
        if (currentTarget != null)
        {
            // Direct movement calculation
            Vector3 targetPosition = currentTarget.position;
            Vector3 directionToTarget = (targetPosition - transform.position).normalized;
            
            // Calculate desired height
            float targetHeight = targetPosition.y + desiredHeightAboveGround;
            float currentHeight = transform.position.y;
            float heightDifference = targetHeight - currentHeight;
            
            // Create movement vector
            Vector3 movement = new Vector3(
                directionToTarget.x * moveSpeed * Time.deltaTime,
                Mathf.Clamp(heightDifference, -heightAdjustmentSpeed, heightAdjustmentSpeed) * Time.deltaTime,
                directionToTarget.z * moveSpeed * Time.deltaTime
            );
            
            // Apply movement
            transform.position += movement;
            
            // Update rotation
            if (directionToTarget != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
            
            // Check interaction distance
            float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
            if (distanceToTarget < 3f)
            {
                if (!isSignaling && !isDeliveringResources)
                {
                    if (resourceRecipientList.Contains(currentTarget))
                    {
                        StartResourceDelivery();
                    }
                    else
                    {
                        StartSignaling();
                    }
                }
            }
        }

        // Target timeout logic
        targetTimer += Time.deltaTime;
        if (targetTimer > targetTimeoutDuration && currentTarget != null)
        {
            Debug.LogWarning("Target timeout reached. Moving to next target.");
            SetNextTarget();
            targetTimer = 0f;
        }

        // Movement logic
        if (pathCorners.Length > currentPathIndex)
        {
            Vector3 nextPoint = pathCorners[currentPathIndex];
            Vector3 directionToNextPoint = (nextPoint - transform.position).normalized;
            
            // Debug path following
            Debug.Log($"Following path: Current Index: {currentPathIndex}, Moving to point: {nextPoint}");
            
            float distanceToNextPoint = Vector3.Distance(transform.position, nextPoint);
            if (distanceToNextPoint < stoppingDistance)
            {
                currentPathIndex++;
                Debug.Log($"Reached path point, moving to next. New index: {currentPathIndex}");
            }

            // Move along path
            Vector3 movement = directionToNextPoint * moveSpeed * Time.deltaTime;
            transform.position += movement;

            // Rotation to face path direction
            if (directionToNextPoint != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToNextPoint);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }
        else if (currentTarget != null)
        {
            // Direct movement toward target if no path is available
            Vector3 directionToTarget = (currentTarget.position - transform.position).normalized;
            transform.position += directionToTarget * moveSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation, 
                                            Quaternion.LookRotation(directionToTarget), 
                                            rotationSpeed * Time.deltaTime);
                                            
            // Close enough to interact?
            float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);
            if (distanceToTarget < 3f)
            {
                if (!isSignaling && !isDeliveringResources)
                {
                    if (resourceRecipientList.Contains(currentTarget))
                    {
                        Debug.Log("Starting resource delivery");
                        StartResourceDelivery();
                    }
                    else
                    {
                        Debug.Log("Starting signaling");
                        StartSignaling();
                    }
                }
            }
        }
        else
        {
            // No target, try to get a new one
            UpdateDetectedSurvivors();
            SetNextTarget();
        }

        // Handle height adjustment
        AdjustHeight();

        // Update signaling and resource delivery states
        UpdateSignaling();
        UpdateResourceDelivery();

        // Time penalty to encourage efficiency
        AddReward(-0.001f);
        episodeTimer += Time.deltaTime;

        // End episode if all survivors handled or time runs out
        if (rescuedSurvivors >= totalSurvivors || episodeTimer > 300f)
        {
            AddReward(1.0f * (rescuedSurvivors / (float)totalSurvivors));
            EndEpisode();
        }
    }

    private void StartSignaling()
    {
        isSignaling = true;
        signalTimer = 0f;
        AddReward(0.1f); // Small reward for starting to signal
    }

    // Add this to the Update method


    private void UpdateSignaling()
    {
        if (isSignaling)
        {
            signalTimer += Time.deltaTime;
            Debug.Log($"Signaling: {signalTimer}/{signalDuration}");
            
            if (signalTimer >= signalDuration)
            {
                isSignaling = false;
                Debug.Log("Signal complete!");

                // Survivor is now set to be rescued
                if (currentTarget != null)
                {
                    Debug.Log($"Rescuing survivor at {currentTarget.position}");
                    AddReward(0.5f); // Reward for completed signal
                    rescuedSurvivors++;

                    // Mark the survivor as "to be rescued"
                    currentTarget.gameObject.SetActive(false);

                    // Move to next target
                    Debug.Log("Setting next target");
                    SetNextTarget();
                    Debug.Log($"New target: {(currentTarget != null ? currentTarget.name : "None")}");
                }
            }
        }
    }

    private void StartResourceDelivery()
    {
        isDeliveringResources = true;
        resourceDeliveryTimer = 0f;
        AddReward(0.1f); // Small reward for starting resource delivery
    }

    private void UpdateResourceDelivery()
    {
        if (isDeliveringResources)
        {
            resourceDeliveryTimer += Time.deltaTime;
            if (resourceDeliveryTimer >= resourceDropTime)
            {
                isDeliveringResources = false;

                // Resources delivered
                if (currentTarget != null)
                {
                    AddReward(0.7f); // Higher reward for resource delivery

                    // Move to next target
                    SetNextTarget();
                }
            }
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        var discreteActions = actionsOut.DiscreteActions;

        continuousActions[0] = Input.GetAxis("Horizontal");
        continuousActions[1] = Input.GetAxis("Vertical");
        continuousActions[2] = Input.GetKey(KeyCode.Space) ? 1f : (Input.GetKey(KeyCode.LeftShift) ? -1f : 0f);
        continuousActions[3] = Input.GetKey(KeyCode.Q) ? -1f : (Input.GetKey(KeyCode.E) ? 1f : 0f);

        discreteActions[0] = Input.GetKey(KeyCode.R) ? 1 : 0; // Signal with R key
        discreteActions[1] = Input.GetKey(KeyCode.F) ? 1 : 0; // Deliver resources with F key
        discreteActions[2] = Input.GetKey(KeyCode.P) ? 1 : 0; // Recalculate path with P key
    }

    private void AdjustHeight()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, 100f, groundLayer))
        {
            float targetHeight = hit.point.y + desiredHeightAboveGround;
            Vector3 newPosition = transform.position;
            newPosition.y = Mathf.Lerp(transform.position.y, targetHeight, Time.deltaTime * heightAdjustmentSpeed);
            transform.position = newPosition;
        }
    }

    private bool IsCloseEnoughToTarget()
    {
        if (currentTarget == null) return false;
        
        // Check horizontal distance first
        float horizontalDist = GetHorizontalDistance(transform.position, currentTarget.position);
        
        // If horizontally close enough, check if height difference is acceptable
        if (horizontalDist < 3f)
        {
            float heightDiff = Mathf.Abs(transform.position.y - currentTarget.position.y);
            Debug.Log($"Close to target: horizontal={horizontalDist:F2}, height diff={heightDiff:F2}");
            
            // Allow interaction if either horizontally very close or height difference is small enough
            return horizontalDist < 1.5f || heightDiff < 8f;
        }
        
        return false;
    }

    public bool IsSignaling()
    {
        return isSignaling;
    }

    public bool IsDeliveringResources()
    {
        return isDeliveringResources;
    }

    public float GetPathCompletionPercentage()
    {
        if (pathCorners.Length <= 1) return 1.0f;
        return (float)currentPathIndex / (pathCorners.Length - 1);
    }

    // Called by sensors to update the list of detected survivors
    void OnDrawGizmos()
    {
        // Draw detection radius
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Draw target connection - check if currentTarget exists
            if (currentTarget != null)
                {
                    Gizmos.color = IsCloseEnoughToTarget() ? Color.green : Color.red;
                    Gizmos.DrawWireSphere(transform.position, 3f);
                    
                    // Draw line to show height difference
                    Gizmos.color = Color.yellow;
                    Vector3 droneHorizontalPos = new Vector3(transform.position.x, currentTarget.position.y, transform.position.z);
                    Gizmos.DrawLine(transform.position, droneHorizontalPos);
                    Gizmos.DrawLine(droneHorizontalPos, currentTarget.position);
                }

        // Draw path points - check if pathCorners is initialized
        if (pathCorners != null)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < pathCorners.Length; i++)
            {
                Gizmos.DrawSphere(pathCorners[i], 0.2f);
                if (i < pathCorners.Length - 1)
                {
                    Gizmos.DrawLine(pathCorners[i], pathCorners[i + 1]);
                }
            }
        }
    }


}