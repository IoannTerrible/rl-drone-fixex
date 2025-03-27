using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public List<Transform> locatedSurvivors = new List<Transform>();
    
    public List<Transform> GetLocatedSurvivors()
    {
        return locatedSurvivors;
    }
    
    public void SurvivorLocated(Transform survivor)
    {
        if (!locatedSurvivors.Contains(survivor))
        {
            locatedSurvivors.Add(survivor);
            Debug.Log("Survivor located and added to gameManager");
        }
    }
    
    public void SurvivorRescued(Transform survivor)
    {
        if (locatedSurvivors.Contains(survivor))
        {
            locatedSurvivors.Remove(survivor);
            Debug.Log("Survivor rescued and removed from gameManager");
        }
    }
}