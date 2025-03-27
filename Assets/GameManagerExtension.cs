using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// This script extends the existing GameManager class with methods needed by the DroneRescueAgent.
/// Place this script in the same GameObject as your GameManager.
/// </summary>
public class GameManagerExtension : MonoBehaviour
{
    // Reference to your existing GameManager
    private MonoBehaviour gameManagerComponent;
    
    // Lists to track survivor status
    private List<Transform> remainingSurvivors = new List<Transform>();
    
    void Awake()
    {
        // Find the existing GameManager component (without directly referencing the type)
        gameManagerComponent = GetComponent<MonoBehaviour>();
        if (gameManagerComponent == null)
        {
            Debug.LogError("No GameManager component found on the same GameObject!");
            enabled = false;
            return;
        }
        
        // Initialize the lists
        InitializeSurvivorLists();
    }
    
    private void InitializeSurvivorLists()
    {
        // Find all survivors in the scene
        GameObject[] survivorObjects = GameObject.FindGameObjectsWithTag("Survivor");
        
        // Add them to the remaining survivors list
        foreach (GameObject survivor in survivorObjects)
        {
            remainingSurvivors.Add(survivor.transform);
        }
        
        Debug.Log($"GameManagerExtension initialized with {remainingSurvivors.Count} survivors.");
    }
    
    /// <summary>
    /// Returns the list of survivors that haven't been located yet.
    /// </summary>
    public List<Transform> GetRemainingSurvivors()
    {
        return remainingSurvivors;
    }
    
    /// <summary>
    /// Returns the list of survivors that have been located but not yet rescued.
    /// </summary>
    public List<Transform> GetLocatedSurvivors()
    {
        // Use reflection to call the existing method on GameManager
        System.Reflection.MethodInfo method = gameManagerComponent.GetType().GetMethod("GetLocatedSurvivors");
        if (method != null)
        {
            return method.Invoke(gameManagerComponent, null) as List<Transform>;
        }
        
        // Fallback - return an empty list
        Debug.LogWarning("GetLocatedSurvivors method not found on GameManager");
        return new List<Transform>();
    }
    
    /// <summary>
    /// Call this when a survivor has been located by the drone.
    /// </summary>
    public void SurvivorLocated(Transform survivor)
    {
        if (remainingSurvivors.Contains(survivor))
        {
            // Remove from remaining list
            remainingSurvivors.Remove(survivor);
            
            // Use reflection to call the existing method on GameManager
            System.Reflection.MethodInfo method = gameManagerComponent.GetType().GetMethod("SurvivorLocated");
            if (method != null)
            {
                method.Invoke(gameManagerComponent, new object[] { survivor });
            }
            else
            {
                Debug.LogWarning("SurvivorLocated method not found on GameManager");
            }
            
            Debug.Log($"Survivor located. {remainingSurvivors.Count} survivors remaining.");
        }
    }
    
    /// <summary>
    /// Call this when a survivor has been rescued.
    /// </summary>
    public void SurvivorRescued(Transform survivor)
    {
        // Use reflection to call the existing method on GameManager
        System.Reflection.MethodInfo method = gameManagerComponent.GetType().GetMethod("SurvivorRescued");
        if (method != null)
        {
            method.Invoke(gameManagerComponent, new object[] { survivor });
        }
        else
        {
            Debug.LogWarning("SurvivorRescued method not found on GameManager");
        }
        
        Debug.Log("Survivor rescued.");
    }
}