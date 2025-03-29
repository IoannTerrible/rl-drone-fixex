using UnityEngine;

/// <summary>
/// This script serves as an interface between our DroneRescueAgent and your existing drone asset.
/// Attach this to your drone asset's root GameObject.
/// </summary>
public class DroneAssetInterface : MonoBehaviour
{
    [Header("Components")]
    public DroneRescueAgent rescueAgent;
    public droneMovementController droneController;

    [Header("Connection Settings")]
    public bool connectRotation = true;
    public bool connectAltitude = true;
    public bool connectPositioning = true;

    [Header("Mapping")]
    [Range(0f, 1f)]
    public float pitchSensitivity = 0.5f;

    [Range(0f, 1f)]
    public float rollSensitivity = 0.5f;

    [Range(0f, 1f)]
    public float yawSensitivity = 0.5f;

    [Range(0f, 1f)]
    public float thrustSensitivity = 0.5f;

    // Internal flags
    private bool isInitialized = false;

    void Start()
    {
        // Find components if not assigned
        if (rescueAgent == null)
        {
            rescueAgent = GetComponentInChildren<DroneRescueAgent>();
            if (rescueAgent == null)
            {
                Debug.LogError("DroneRescueAgent not found! Please assign it in the inspector.");
                enabled = false;
                return;
            }
        }

        if (droneController == null)
        {
            droneController = GetComponentInChildren<droneMovementController>();
            if (droneController == null)
            {
                Debug.LogError(
                    "droneMovementController not found! Please assign it in the inspector."
                );
                enabled = false;
                return;
            }
        }

        // Connect the drone rigidbody and transform to the agent
        rescueAgent.DroneRigidbody = GetComponent<Rigidbody>();
        rescueAgent.DroneTransform = transform;

        isInitialized = true;
        Debug.Log("Drone Asset Interface initialized successfully.");
    }

    void Update()
    {
        if (!isInitialized || rescueAgent == null || droneController == null)
            return;

        // Get control values from the agent
        Vector3 controls = rescueAgent.GetMovementControls();
        float yawControl = rescueAgent.GetYawControl();

        // Connect controls to your drone movement controller
        if (connectRotation)
        {
            // Apply pitch and roll for orientation
            droneController.idealPitch = controls.x * pitchSensitivity;
            droneController.idealRoll = controls.z * rollSensitivity;
            droneController.idealYaw += yawControl * yawSensitivity;
        }

        if (connectAltitude)
        {
            // Apply thrust for altitude
            droneController.targetY += controls.y * thrustSensitivity;
        }

        // Handle targeting for positioning
        if (connectPositioning && rescueAgent.GetCurrentTarget() != null)
        {
            // Set the route position and looking point
            Vector3 targetPosition = rescueAgent.GetCurrentTarget().position;
            droneController.setRoutePos(targetPosition);
            droneController.setLookingPoint(targetPosition);

            // Determine if the drone should stay fixed
            droneController.stayOnFixedPoint = rescueAgent.IsSignaling();
        }
    }
}
