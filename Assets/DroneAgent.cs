using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections;
using System.Collections.Generic;

public class DroneRescueAgent : Agent
{
    [Header("External Drone Controls")]
    public Transform droneTransform;
    public Rigidbody droneRigidbody;
    
    [Header("Detection Settings")]
    public float detectionRadius = 15f;
    public float rescueRadius = 3f;
    public float signalDuration = 5f;
    public LayerMask survivorLayer;
    public LayerMask obstacleLayer;
    public float minHeightForCrash = 1.0f;
    
    [Header("Visual Effects")]
    public GameObject detectionIndicatorPrefab;
    public GameObject explosionPrefab;
    public Color detectionColor = Color.yellow;
    public Color rescueColor = Color.green;
    
    [Header("Audio")]
    public AudioClip detectionSound;
    public AudioClip rescueSound;
    public AudioClip explosionSound;
    
    [Header("Movement Output")]
    public float pitchAmount = 0.2f;
    public float rollAmount = 0.2f;
    public float yawAmount = 0.2f;
    public float thrustAmount = 0.2f;
    
    [Header("Reward Settings")]
    public float rescuedSurvivorReward = 1.0f;
    public float locatedSurvivorReward = 0.3f;
    public float crashPenalty = -1.0f;
    public float timeStepPenalty = -0.001f;
    public float approachingReward = 0.01f;
    
    // Internal state variables
    private bool isSignaling = false;
    private float signalTimer = 0f;
    private Transform currentTarget;
    private AudioSource audioSource;
    private GameManagerExtension gameManager;
    private Vector3 previousPosition;
    private List<Transform> detectedSurvivors = new List<Transform>();
    private bool hasExploded = false;
    private Vector3 inputControls = Vector3.zero;
    private float yawControl = 0f;
    private bool isInitialized = false;
    
    public override void Initialize()
    {
        if (droneTransform == null)
        {
            droneTransform = transform;
            Debug.LogWarning("DroneTransform not assigned, using self transform");
        }
        
        if (droneRigidbody == null)
        {
            droneRigidbody = GetComponent<Rigidbody>();
            if (droneRigidbody == null)
            {
                droneRigidbody = droneTransform.GetComponent<Rigidbody>();
                if (droneRigidbody == null)
                {
                    Debug.LogError("No Rigidbody found on drone. Adding one.");
                    droneRigidbody = droneTransform.gameObject.AddComponent<Rigidbody>();
                }
            }
        }
        
        // Get required components
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // Find or create GameManagerExtension
        gameManager = FindObjectOfType<GameManagerExtension>();
        if (gameManager == null)
        {
            // Create basic GameManager if not found
            GameObject gmObject = new GameObject("GameManager");
            gmObject.AddComponent<GameManager>();
            gameManager = gmObject.AddComponent<GameManagerExtension>();
            Debug.LogWarning("Created GameManager and GameManagerExtension as none were found");
        }
        
        // Ensure required tags exist
        CreateRequiredTags();
        
        // Ensure spawn point exists
        EnsureSpawnPointExists();
        
        // Ensure SafeZone exists
        EnsureSafeZoneExists();
        
        // Start detection routine
        StartCoroutine(SurvivorDetectionRoutine());
        
        isInitialized = true;
        Debug.Log("DroneRescueAgent initialized successfully");
    }
    
    private void CreateRequiredTags()
    {
        // This is just a message - Unity doesn't allow runtime tag creation
        bool hasDroneSpawnTag = false;
        bool hasSafeZoneTag = false;
        bool hasSurvivorTag = false;
        
        GameObject testObj = new GameObject();
        try {
            testObj.tag = "DroneSpawn";
            hasDroneSpawnTag = true;
        } catch { }
        
        try {
            testObj.tag = "SafeZone";
            hasSafeZoneTag = true;
        } catch { }
        
        try {
            testObj.tag = "Survivor";
            hasSurvivorTag = true;
        } catch { }
        
        Destroy(testObj);
        
        string missingTags = "";
        if (!hasDroneSpawnTag) missingTags += "DroneSpawn ";
        if (!hasSafeZoneTag) missingTags += "SafeZone ";
        if (!hasSurvivorTag) missingTags += "Survivor ";
        
        if (missingTags != "")
        {
            Debug.LogError("Missing required tags: " + missingTags + ". Please add these tags in Edit > Project Settings > Tags and Layers");
        }
    }
    
    private void EnsureSpawnPointExists()
    {
        GameObject spawnPoint = GameObject.FindGameObjectWithTag("DroneSpawn");
        if (spawnPoint == null)
        {
            spawnPoint = new GameObject("DroneSpawn");
            spawnPoint.tag = "DroneSpawn";
            spawnPoint.transform.position = new Vector3(0, 10, 0);
            Debug.LogWarning("Created default DroneSpawn at (0,10,0) as none was found");
        }
    }
    
    private void EnsureSafeZoneExists()
    {
        GameObject safeZone = GameObject.FindGameObjectWithTag("SafeZone");
        if (safeZone == null)
        {
            safeZone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            safeZone.name = "SafeZone";
            safeZone.tag = "SafeZone";
            safeZone.transform.position = new Vector3(0, 0.1f, 0);
            safeZone.transform.localScale = new Vector3(10, 0.1f, 10);
            
            // Add SafeZone script if it exists in project
            if (System.Type.GetType("SafeZone") != null)
            {
                safeZone.AddComponent(System.Type.GetType("SafeZone"));
            }
            
            // Create basic material
            Renderer renderer = safeZone.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0, 1, 0, 0.3f);
                renderer.material = mat;
            }
            
            Debug.LogWarning("Created default SafeZone at origin as none was found");
        }
    }
    
    public override void OnEpisodeBegin()
    {
        Debug.Log("Starting new episode");
        
        // Reset drone state
        hasExploded = false;
        isSignaling = false;
        signalTimer = 0f;
        inputControls = Vector3.zero;
        yawControl = 0f;
        detectedSurvivors.Clear();
        
        // Reset drone position if spawn point is available
        Transform spawnPoint = GameObject.FindGameObjectWithTag("DroneSpawn")?.transform;
        if (spawnPoint != null)
        {
            if (droneTransform != null)
            {
                droneTransform.position = spawnPoint.position;
                droneTransform.rotation = spawnPoint.rotation;
            }
            else
            {
                Debug.LogError("droneTransform is null in OnEpisodeBegin");
            }
            
            if (droneRigidbody != null)
            {
                droneRigidbody.velocity = Vector3.zero;
                droneRigidbody.angularVelocity = Vector3.zero;
            }
            else
            {
                Debug.LogError("droneRigidbody is null in OnEpisodeBegin");
            }
        }
        else
        {
            Debug.LogError("No GameObject with DroneSpawn tag found");
        }
        
        // Find initial target
        UpdateCurrentTarget();
        
        // Store position for reward calculation
        previousPosition = droneTransform != null ? droneTransform.position : transform.position;
    }
    
    public override void CollectObservations(VectorSensor sensor)
    {
        if (droneTransform == null)
        {
            // Emergency fallbacks if droneTransform is null
            sensor.AddObservation(transform.localPosition);
            sensor.AddObservation(transform.forward);
            sensor.AddObservation(Vector3.zero); // No velocity data
            Debug.LogError("droneTransform is null in CollectObservations");
        }
        else
        {
            // Drone position and rotation (6 values)
            sensor.AddObservation(droneTransform.localPosition);
            sensor.AddObservation(droneTransform.forward);
            
            // Drone velocity (3 values)
            if (droneRigidbody != null)
            {
                sensor.AddObservation(droneRigidbody.velocity);
            }
            else
            {
                sensor.AddObservation(Vector3.zero);
            }
        }
        
        // Current target information (4 values)
        if (currentTarget != null)
        {
            Vector3 observationPos = droneTransform != null ? droneTransform.position : transform.position;
            
            // Direction to target in local space
            Vector3 localDirection = (droneTransform != null ? 
                droneTransform.InverseTransformPoint(currentTarget.position) : 
                transform.InverseTransformPoint(currentTarget.position)).normalized;
                
            sensor.AddObservation(localDirection);
            
            // Distance to target (normalized)
            float distance = Vector3.Distance(observationPos, currentTarget.position);
            sensor.AddObservation(distance / detectionRadius);
        }
        else
        {
            // No target
            sensor.AddObservation(Vector3.zero); // Direction
            sensor.AddObservation(1.0f);         // Max distance
        }
        
        // Signaling state (1 value)
        sensor.AddObservation(isSignaling ? 1.0f : 0.0f);
        
        // Obstacle detection (8 values)
        Vector3 raycastOrigin = droneTransform != null ? droneTransform.position : transform.position;
        Vector3 forwardDir = droneTransform != null ? droneTransform.forward : transform.forward;
        
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * forwardDir;
            
            RaycastHit hit;
            if (Physics.Raycast(raycastOrigin, direction, out hit, detectionRadius, obstacleLayer))
            {
                sensor.AddObservation(hit.distance / detectionRadius);
            }
            else
            {
                sensor.AddObservation(1.0f);
            }
        }
    }
    
    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (hasExploded)
            return;
            
        if (!isInitialized)
        {
            Debug.LogWarning("DroneRescueAgent not fully initialized but receiving actions");
            return;
        }
            
        // Extract actions
        float pitch = actionBuffers.ContinuousActions[0];   // Forward/backward
        float roll = actionBuffers.ContinuousActions[1];    // Left/right
        float thrust = actionBuffers.ContinuousActions[2];  // Up/down
        float yaw = actionBuffers.ContinuousActions[3];     // Rotation
        
        // Store input controls for your drone asset to use
        inputControls = new Vector3(pitch * pitchAmount, thrust * thrustAmount, roll * rollAmount);
        yawControl = yaw * yawAmount;
        
        // Apply small time penalty to encourage efficiency
        AddReward(timeStepPenalty);
        
        // Update signal timer
        if (isSignaling)
        {
            signalTimer -= Time.deltaTime;
            if (signalTimer <= 0)
            {
                isSignaling = false;
            }
        }
        
        // Check proximity to current target
        if (currentTarget != null)
        {
            Vector3 currentPos = droneTransform != null ? droneTransform.position : transform.position;
            float distance = Vector3.Distance(currentPos, currentTarget.position);
            
            // Reward for getting closer to target
            float previousDistance = Vector3.Distance(previousPosition, currentTarget.position);
            if (previousDistance > distance)
            {
                AddReward(approachingReward);
            }
            
            // Check if close enough for detection/rescue
            if (distance <= rescueRadius && detectedSurvivors.Contains(currentTarget))
            {
                if (!isSignaling)
                {
                    // Start rescue signal
                    isSignaling = true;
                    signalTimer = signalDuration;
                    
                    // Play rescue sound
                    if (audioSource != null && rescueSound != null)
                    {
                        audioSource.PlayOneShot(rescueSound);
                    }
                    
                    // Create visual indicator
                    CreateIndicator(currentTarget, rescueColor);
                }
                else if (signalTimer <= 0.5f)
                {
                    // Rescue complete
                    RescueSurvivor(currentTarget);
                    UpdateCurrentTarget();
                }
            }
        }
        
        // Store current position for next frame
        previousPosition = droneTransform != null ? droneTransform.position : transform.position;
        
        // Check for collisions with terrain
        CheckForCollision();
    }
    
    void CheckForCollision()
    {
        if (droneTransform == null) return;
        
        // Center raycast for terrain detection
        RaycastHit hit;
        if (Physics.Raycast(droneTransform.position, Vector3.down, out hit, minHeightForCrash, obstacleLayer))
        {
            Debug.Log("Drone hit terrain via downward raycast: " + hit.collider.name);
            TriggerExplosion();
            return;
        }
        
        // Multiple raycasts for better detection
        Vector3[] checkPoints = new Vector3[]
        {
            droneTransform.position + droneTransform.right * 0.5f,
            droneTransform.position - droneTransform.right * 0.5f,
            droneTransform.position + droneTransform.forward * 0.5f,
            droneTransform.position - droneTransform.forward * 0.5f
        };
        
        foreach (Vector3 point in checkPoints)
        {
            if (Physics.Raycast(point, Vector3.down, out hit, minHeightForCrash, obstacleLayer))
            {
                Debug.Log("Drone hit terrain via side raycast: " + hit.collider.name);
                TriggerExplosion();
                return;
            }
        }
        
        // Check for high velocity impact
        if (droneRigidbody != null && droneRigidbody.velocity.magnitude > 10)
        {
            Debug.Log("Drone crashed due to high velocity: " + droneRigidbody.velocity.magnitude);
            TriggerExplosion();
            return;
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Check if collision is with terrain
        if (((1 << collision.gameObject.layer) & obstacleLayer) != 0)
        {
            Debug.Log("Drone collided with: " + collision.gameObject.name);
            TriggerExplosion();
        }
    }
    
    private void RescueSurvivor(Transform survivor)
    {
        // Add reward
        AddReward(rescuedSurvivorReward);
        
        // Notify game manager
        if (gameManager != null)
        {
            gameManager.SurvivorRescued(survivor);
        }
        
        // Remove from detected list
        detectedSurvivors.Remove(survivor);
        
        Debug.Log("Survivor rescued at " + survivor.position);
    }
    
    IEnumerator SurvivorDetectionRoutine()
    {
        while (true)
        {
            if (droneTransform == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }
            
            // Don't scan if signaling or exploded
            if (!isSignaling && !hasExploded)
            {
                // Scan for survivors
                Collider[] hitColliders = Physics.OverlapSphere(droneTransform.position, detectionRadius, survivorLayer);
                
                foreach (var hitCollider in hitColliders)
                {
                    if (hitCollider == null) continue;
                    
                    Transform survivor = hitCollider.transform;
                    
                    // Check if survivor is already detected
                    if (!detectedSurvivors.Contains(survivor))
                    {
                        // Check line of sight
                        if (HasLineOfSightTo(survivor))
                        {
                            // Survivor found!
                            LocateSurvivor(survivor);
                        }
                    }
                }
            }
            
            // Wait before next scan
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    bool HasLineOfSightTo(Transform target)
    {
        if (droneTransform == null || target == null) return false;
        
        RaycastHit hit;
        Vector3 direction = target.position - droneTransform.position;
        
        if (Physics.Raycast(droneTransform.position, direction.normalized, out hit, detectionRadius))
        {
            // Check if direct hit to survivor
            if (hit.transform == target)
            {
                return true;
            }
        }
        
        return false;
    }
    
    void LocateSurvivor(Transform survivor)
    {
        // Add to detected list
        detectedSurvivors.Add(survivor);
        
        // Notify game manager
        if (gameManager != null)
        {
            gameManager.SurvivorLocated(survivor);
        }
        
        // Add reward
        AddReward(locatedSurvivorReward);
        
        // Play detection sound
        if (audioSource != null && detectionSound != null)
        {
            audioSource.PlayOneShot(detectionSound);
        }
        
        // Create visual indicator
        CreateIndicator(survivor, detectionColor);
        
        Debug.Log("Survivor located at " + survivor.position);
        
        // If no current target, set this as target
        if (currentTarget == null)
        {
            currentTarget = survivor;
        }
    }
    
    void CreateIndicator(Transform target, Color color)
    {
        if (detectionIndicatorPrefab != null && target != null)
        {
            GameObject indicator = Instantiate(detectionIndicatorPrefab, target.position, Quaternion.identity);
            
            // Set color
            Renderer renderer = indicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
            
            // Make temporary
            Destroy(indicator, 5f);
        }
    }
    
    void UpdateCurrentTarget()
    {
        if (gameManager == null) return;
        
        // First check for located survivors to rescue
        List<Transform> locatedSurvivors = gameManager.GetLocatedSurvivors();
        if (locatedSurvivors != null && locatedSurvivors.Count > 0)
        {
            // Find closest located survivor
            currentTarget = FindClosestTransform(locatedSurvivors);
            Debug.Log("Target updated to located survivor: " + (currentTarget != null ? currentTarget.name : "null"));
        }
        else
        {
            // Look for remaining survivors
            List<Transform> remainingSurvivors = gameManager.GetRemainingSurvivors();
            if (remainingSurvivors != null && remainingSurvivors.Count > 0)
            {
                currentTarget = FindClosestTransform(remainingSurvivors);
                Debug.Log("Target updated to remaining survivor: " + (currentTarget != null ? currentTarget.name : "null"));
            }
            else
            {
                // All survivors rescued, go to safe zone
                currentTarget = GameObject.FindGameObjectWithTag("SafeZone")?.transform;
                Debug.Log("Target updated to safe zone: " + (currentTarget != null ? currentTarget.name : "null"));
            }
        }
    }
    
    Transform FindClosestTransform(List<Transform> transforms)
    {
        if (transforms == null || transforms.Count == 0 || droneTransform == null)
            return null;
            
        Transform closest = null;
        float minDistance = float.MaxValue;
        
        foreach (Transform t in transforms)
        {
            if (t == null) continue;
            
            float distance = Vector3.Distance(droneTransform.position, t.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = t;
            }
        }
        
        return closest;
    }
    
    void TriggerExplosion()
    {
        if (hasExploded)
            return;
            
        hasExploded = true;
        
        // Visual effect
        if (explosionPrefab != null && droneTransform != null)
        {
            Instantiate(explosionPrefab, droneTransform.position, Quaternion.identity);
        }
        
        // Sound effect
        if (audioSource != null && explosionSound != null)
        {
            audioSource.PlayOneShot(explosionSound);
        }
        
        // Add negative reward
        AddReward(crashPenalty);
        
        Debug.Log("Drone crashed and exploded!");
        
        // End episode with slight delay to allow explosion effects
        StartCoroutine(DelayedEndEpisode());
    }
    
    IEnumerator DelayedEndEpisode()
    {
        // Wait a moment to see the explosion
        yield return new WaitForSeconds(0.5f);
        EndEpisode();
    }
    
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Manual control for testing
        var continuousActionsOut = actionsOut.ContinuousActions;
        
        continuousActionsOut[0] = Input.GetAxis("Vertical");     // Pitch (forward/backward)
        continuousActionsOut[1] = Input.GetAxis("Horizontal");   // Roll (left/right)
        continuousActionsOut[2] = (Input.GetKey(KeyCode.Space) ? 1.0f : 0.0f) - 
                                 (Input.GetKey(KeyCode.LeftShift) ? 1.0f : 0.0f);  // Thrust (up/down)
        continuousActionsOut[3] = (Input.GetKey(KeyCode.E) ? 1.0f : 0.0f) - 
                                 (Input.GetKey(KeyCode.Q) ? 1.0f : 0.0f);  // Yaw rotation
    }
    
    // Provide access to the agent's movement decisions for your drone asset to use
    public Vector3 GetMovementControls()
    {
        return inputControls;
    }
    
    public float GetYawControl()
    {
        return yawControl;
    }
    
    public bool IsSignaling()
    {
        return isSignaling;
    }
    
    public Transform GetCurrentTarget()
    {
        return currentTarget;
    }
    
    void OnDrawGizmosSelected()
    {
        // Only draw if we have a valid transform reference
        Vector3 position = droneTransform != null ? droneTransform.position : transform.position;
        
        // Draw detection radius
        Gizmos.color = new Color(1, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(position, detectionRadius);
        
        // Draw rescue radius
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(position, rescueRadius);
        
        // Draw line to current target
        if (currentTarget != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(position, currentTarget.position);
        }
        
        // Draw minimum height for crash
        Gizmos.color = Color.red;
        Gizmos.DrawLine(position, position + Vector3.down * minHeightForCrash);
    }
}