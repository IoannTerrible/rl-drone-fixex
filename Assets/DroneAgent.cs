using System;
using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class DroneRescueAgent : Agent
{
    [Header("External Drone Controls")]
    public Transform DroneTransform;
    public Rigidbody DroneRigidbody;

    [Header("Detection Settings")]
    public float DetectionRadius = 15f;
    public float RescueRadius = 3f;
    public float SignalDuration = 5f;
    public LayerMask SurvivorLayer;
    public LayerMask ObstacleLayer;
    public float MinCrashHeight = 1.0f;

    [Header("Visual Effects")]
    public GameObject DetectionIndicatorPrefab;
    public GameObject ExplosionPrefab;
    public Color DetectionColor = Color.yellow;
    public Color RescueColor = Color.green;

    [Header("Audio")]
    public AudioClip DetectionSound;
    public AudioClip RescueSound;
    public AudioClip ExplosionSound;

    [Header("Movement Output")]
    public float PitchScale = 0.2f;
    public float RollScale = 0.2f;
    public float YawScale = 0.2f;
    public float ThrustScale = 0.2f;

    [Header("Reward Settings")]
    public float RescuedSurvivorReward = 1.0f;
    public float LocatedSurvivorReward = 0.3f;
    public float CrashPenalty = -1.0f;
    public float TimeStepPenalty = -0.001f;
    public float ApproachingReward = 0.01f;

    private bool _isSignaling = false;
    private float _signalTimer = 0f;
    private Transform _currentTarget;
    private AudioSource _audioSource;
    private GameManagerExtension _gameManager;
    private Vector3 _previousPosition;
    private List<Transform> _detectedSurvivors = new List<Transform>();
    private bool _hasExploded = false;
    private Vector3 _inputControls = Vector3.zero;
    private float _yawControl = 0f;
    private bool _isInitialized = false;

    public override void Initialize()
    {
        if (DroneTransform == null)
        {
            DroneTransform = transform;
            Debug.LogWarning("DroneTransform not assigned, using self transform");
        }

        if (DroneRigidbody == null)
        {
            DroneRigidbody = GetComponent<Rigidbody>();
            if (DroneRigidbody == null)
            {
                DroneRigidbody = DroneTransform.GetComponent<Rigidbody>();
                if (DroneRigidbody == null)
                {
                    Debug.LogError("No Rigidbody found on drone. Adding one.");
                    DroneRigidbody = DroneTransform.gameObject.AddComponent<Rigidbody>();
                }
            }
        }

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }

        _gameManager = FindObjectOfType<GameManagerExtension>();
        if (_gameManager == null)
        {
            GameObject gmObject = new GameObject("GameManager");
            gmObject.AddComponent<GameManager>();
            _gameManager = gmObject.AddComponent<GameManagerExtension>();
            Debug.LogWarning("Created GameManager and GameManagerExtension as none were found");
        }

        ValidateRequiredTags();
        EnsureSpawnPointExists();
        EnsureSafeZoneExists();
        StartCoroutine(DetectSurvivorsRoutine());

        _isInitialized = true;
        Debug.Log("DroneRescueAgent initialized successfully");
    }

    private void ValidateRequiredTags()
    {
        bool hasDroneSpawnTag = false;
        bool hasSafeZoneTag = false;
        bool hasSurvivorTag = false;

        GameObject testObj = new();
        try
        {
            testObj.tag = "DroneSpawn";
            hasDroneSpawnTag = true;

            testObj.tag = "SafeZone";
            hasSafeZoneTag = true;

            testObj.tag = "Survivor";
            hasSurvivorTag = true;
        }
        catch(Exception ex) { }

        Destroy(testObj);

        string missingTags = "";
        if (!hasDroneSpawnTag) missingTags += "DroneSpawn ";
        if (!hasSafeZoneTag)  missingTags += "SafeZone ";
        if (!hasSurvivorTag) missingTags += "Survivor ";

        if (missingTags != "")
        {
            Debug.LogError(
                "Missing required tags: "
                    + missingTags
                    + ". Please add these tags in Edit > Project Settings > Tags and Layers"
            );
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
            if (System.Type.GetType("SafeZone") != null)
            {
                safeZone.AddComponent(System.Type.GetType("SafeZone"));
            }
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
    /// <summary>You can write comments like this.</summary>
    public override void OnEpisodeBegin()
    {
        Debug.Log("Starting new episode");
        _hasExploded = false;
        _isSignaling = false;
        _signalTimer = 0f;
        _inputControls = Vector3.zero;
        _yawControl = 0f;
        _detectedSurvivors.Clear();

        Transform spawnPoint = GameObject.FindGameObjectWithTag("DroneSpawn")?.transform;
        if (spawnPoint != null)
        {
            if (DroneTransform != null)
            {
                //You can do this.
                DroneTransform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            }
            else
            {
                Debug.LogError("DroneTransform is null in OnEpisodeBegin");
            }

            if (DroneRigidbody != null)
            {
                DroneRigidbody.velocity = Vector3.zero;
                DroneRigidbody.angularVelocity = Vector3.zero;
            }
            else
            {
                Debug.LogError("DroneRigidbody is null in OnEpisodeBegin");
            }
        }
        else
        {
            Debug.LogError("No GameObject with DroneSpawn tag found");
        }

        UpdateCurrentTarget();
        _previousPosition = DroneTransform != null ? DroneTransform.position : transform.position;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (DroneTransform == null)
        {
            sensor.AddObservation(transform.localPosition);
            sensor.AddObservation(transform.forward);
            sensor.AddObservation(Vector3.zero);
            Debug.LogError("DroneTransform is null in CollectObservations");
        }
        else
        {
            sensor.AddObservation(DroneTransform.localPosition);
            sensor.AddObservation(DroneTransform.forward);
            if (DroneRigidbody != null)
            {
                sensor.AddObservation(DroneRigidbody.velocity);
            }
            else
            {
                sensor.AddObservation(Vector3.zero);
            }
        }

        if (_currentTarget != null)
        {
            Vector3 observationPos =
                DroneTransform != null ? DroneTransform.position : transform.position;
            Vector3 localDirection = (
                DroneTransform != null
                    ? DroneTransform.InverseTransformPoint(_currentTarget.position)
                    : transform.InverseTransformPoint(_currentTarget.position)
            ).normalized;
            sensor.AddObservation(localDirection);
            float distance = Vector3.Distance(observationPos, _currentTarget.position);
            sensor.AddObservation(distance / DetectionRadius);
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(1.0f);
        }

        sensor.AddObservation(_isSignaling ? 1.0f : 0.0f);

        Vector3 rayOrigin = DroneTransform != null ? DroneTransform.position : transform.position;
        Vector3 forwardDir = DroneTransform != null ? DroneTransform.forward : transform.forward;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * forwardDir;
            RaycastHit hit;
            if (Physics.Raycast(rayOrigin, direction, out hit, DetectionRadius, ObstacleLayer))
            {
                sensor.AddObservation(hit.distance / DetectionRadius);
            }
            else
            {
                sensor.AddObservation(1.0f);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (_hasExploded)
            return;

        if (!_isInitialized)
        {
            Debug.LogWarning("DroneRescueAgent not fully initialized but receiving actions");
            return;
        }

        float pitch = actionBuffers.ContinuousActions[0];
        float roll = actionBuffers.ContinuousActions[1];
        float thrust = actionBuffers.ContinuousActions[2];
        float yaw = actionBuffers.ContinuousActions[3];

        _inputControls = new Vector3(pitch * PitchScale, thrust * ThrustScale, roll * RollScale);
        _yawControl = yaw * YawScale;

        AddReward(TimeStepPenalty);

        if (_isSignaling)
        {
            _signalTimer -= Time.deltaTime;
            if (_signalTimer <= 0)
            {
                _isSignaling = false;
            }
        }

        if (_currentTarget != null)
        {
            Vector3 currentPos =
                DroneTransform != null ? DroneTransform.position : transform.position;
            float distance = Vector3.Distance(currentPos, _currentTarget.position);
            float previousDistance = Vector3.Distance(_previousPosition, _currentTarget.position);
            if (previousDistance > distance)
            {
                AddReward(ApproachingReward);
            }

            if (distance <= RescueRadius && _detectedSurvivors.Contains(_currentTarget))
            {
                if (!_isSignaling)
                {
                    _isSignaling = true;
                    _signalTimer = SignalDuration;
                    if (_audioSource != null && RescueSound != null)
                    {
                        _audioSource.PlayOneShot(RescueSound);
                    }
                    CreateIndicator(_currentTarget, RescueColor);
                }
                else if (_signalTimer <= 0.5f)
                {
                    RescueSurvivor(_currentTarget);
                    UpdateCurrentTarget();
                }
            }
        }

        _previousPosition = DroneTransform != null ? DroneTransform.position : transform.position;
        CheckForCollision();
    }

    void CheckForCollision()
    {
        if (DroneTransform == null)
            return;

        RaycastHit hit;
        if (
            Physics.Raycast(
                DroneTransform.position,
                Vector3.down,
                out hit,
                MinCrashHeight,
                ObstacleLayer
            )
        )
        {
            Debug.Log("Drone hit terrain via downward raycast: " + hit.collider.name);
            TriggerExplosion();
            return;
        }

        Vector3[] checkPoints = new Vector3[]
        {
            DroneTransform.position + DroneTransform.right * 0.5f,
            DroneTransform.position - DroneTransform.right * 0.5f,
            DroneTransform.position + DroneTransform.forward * 0.5f,
            DroneTransform.position - DroneTransform.forward * 0.5f
        };

        foreach (Vector3 point in checkPoints)
        {
            if (Physics.Raycast(point, Vector3.down, out hit, MinCrashHeight, ObstacleLayer))
            {
                Debug.Log("Drone hit terrain via side raycast: " + hit.collider.name);
                TriggerExplosion();
                return;
            }
        }

        if (DroneRigidbody != null && DroneRigidbody.velocity.magnitude > 10)
        {
            Debug.Log("Drone crashed due to high velocity: " + DroneRigidbody.velocity.magnitude);
            TriggerExplosion();
            return;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (((1 << collision.gameObject.layer) & ObstacleLayer) != 0)
        {
            Debug.Log("Drone collided with: " + collision.gameObject.name);
            TriggerExplosion();
        }
    }

    void RescueSurvivor(Transform survivor)
    {
        AddReward(RescuedSurvivorReward);
        if (_gameManager != null)
        {
            _gameManager.SurvivorRescued(survivor);
        }
        _detectedSurvivors.Remove(survivor);
        Debug.Log("Survivor rescued at " + survivor.position);
    }

    IEnumerator DetectSurvivorsRoutine()
    {
        while (true)
        {
            if (DroneTransform == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            if (!_isSignaling && !_hasExploded)
            {
                Collider[] hitColliders = Physics.OverlapSphere(
                    DroneTransform.position,
                    DetectionRadius,
                    SurvivorLayer
                );
                foreach (var hitCollider in hitColliders)
                {
                    if (hitCollider == null)
                        continue;

                    Transform survivor = hitCollider.transform;
                    if (!_detectedSurvivors.Contains(survivor))
                    {
                        if (HasLineOfSightTo(survivor))
                        {
                            LocateSurvivor(survivor);
                        }
                    }
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    bool HasLineOfSightTo(Transform target)
    {
        if (DroneTransform == null || target == null)
            return false;

        Vector3 direction = target.position - DroneTransform.position;
        if (
            Physics.Raycast(
                DroneTransform.position,
                direction.normalized,
                out RaycastHit hit,
                DetectionRadius
            )
        )
        {
            if (hit.transform == target)
            {
                return true;
            }
        }
        return false;
    }

    void LocateSurvivor(Transform survivor)
    {
        _detectedSurvivors.Add(survivor);
        if (_gameManager != null)
        {
            _gameManager.SurvivorLocated(survivor);
        }
        AddReward(LocatedSurvivorReward);
        if (_audioSource != null && DetectionSound != null)
        {
            _audioSource.PlayOneShot(DetectionSound);
        }
        //Check position there.
        CreateIndicator(survivor, DetectionColor);
        Debug.Log("Survivor located at " + survivor.position);
        if (_currentTarget == null)
        {
            _currentTarget = survivor;
        }
    }

    void CreateIndicator(Transform target, Color color)
    {
        if (DetectionIndicatorPrefab != null && target != null)
        {
            GameObject indicator = Instantiate(
                DetectionIndicatorPrefab,
                target.position,
                Quaternion.identity
            );
            Renderer renderer = indicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
            Destroy(indicator, 5f);
        }
    }

    void UpdateCurrentTarget()
    {
        if (_gameManager == null)
            return;

        List<Transform> locatedSurvivors = _gameManager.GetLocatedSurvivors();
        if (locatedSurvivors != null && locatedSurvivors.Count > 0)
        {
            _currentTarget = FindClosestTransform(locatedSurvivors);
            Debug.Log(
                "Target updated to located survivor: "
                    + (_currentTarget != null ? _currentTarget.name : "null")
            );
        }
        else
        {
            List<Transform> remainingSurvivors = _gameManager.GetRemainingSurvivors();
            if (remainingSurvivors != null && remainingSurvivors.Count > 0)
            {
                _currentTarget = FindClosestTransform(remainingSurvivors);
                Debug.Log(
                    "Target updated to remaining survivor: "
                        + (_currentTarget != null ? _currentTarget.name : "null")
                );
            }
            else
            {
                _currentTarget = GameObject.FindGameObjectWithTag("SafeZone")?.transform;
                Debug.Log(
                    "Target updated to safe zone: "
                        + (_currentTarget != null ? _currentTarget.name : "null")
                );
            }
        }
    }

    Transform FindClosestTransform(List<Transform> transforms)
    {
        if (transforms == null || transforms.Count == 0 || DroneTransform == null)
            return null;

        Transform closest = null;
        float minDistance = float.MaxValue;

        foreach (Transform t in transforms)
        {
            if (t == null)
                continue;

            float distance = Vector3.Distance(DroneTransform.position, t.position);
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
        if (_hasExploded)
            return;

        _hasExploded = true;

        if (ExplosionPrefab != null && DroneTransform != null)
        {
            Instantiate(ExplosionPrefab, DroneTransform.position, Quaternion.identity);
        }

        if (_audioSource != null && ExplosionSound != null)
        {
            _audioSource.PlayOneShot(ExplosionSound);
        }

        AddReward(CrashPenalty);
        Debug.Log("Drone crashed and exploded!");
        StartCoroutine(DelayedEndEpisode());
    }

    IEnumerator DelayedEndEpisode()
    {
        yield return new WaitForSeconds(0.5f);
        EndEpisode();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        continuousActionsOut[0] = Input.GetAxis("Vertical");
        continuousActionsOut[1] = Input.GetAxis("Horizontal");
        continuousActionsOut[2] =
            (Input.GetKey(KeyCode.Space) ? 1.0f : 0.0f)
            - (Input.GetKey(KeyCode.LeftShift) ? 1.0f : 0.0f);
        continuousActionsOut[3] =
            (Input.GetKey(KeyCode.E) ? 1.0f : 0.0f) - (Input.GetKey(KeyCode.Q) ? 1.0f : 0.0f);
    }

    public Vector3 GetMovementControls()
    {
        return _inputControls;
    }

    public float GetYawControl()
    {
        return _yawControl;
    }

    public bool IsSignaling()
    {
        return _isSignaling;
    }

    public Transform GetCurrentTarget()
    {
        return _currentTarget;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 position = DroneTransform != null ? DroneTransform.position : transform.position;
        Gizmos.color = new Color(1, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(position, DetectionRadius);
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(position, RescueRadius);

        if (_currentTarget != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(position, _currentTarget.position);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawLine(position, position + Vector3.down * MinCrashHeight);
    }
}
