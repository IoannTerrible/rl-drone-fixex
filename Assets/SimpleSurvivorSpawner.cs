using UnityEngine;

public class SimpleSurvivorSpawner : MonoBehaviour
{
    public Terrain terrain;  
    public GameObject survivorPrefab;  
    public int numberOfSurvivors = 10;
    
    void Start()
    {
        if (terrain == null)
        {
            Debug.LogError("Terrain is not assigned!");
            return;
        }
        
        SpawnSurvivors();
    }
    
    void SpawnSurvivors()
    {
        for (int i = 0; i < numberOfSurvivors; i++)
        {
            Vector3 spawnPosition = GetRandomPositionOnTerrain();
            GameObject survivor = Instantiate(survivorPrefab, spawnPosition, Quaternion.identity);
            survivor.tag = "Survivor";
            survivor.layer = LayerMask.NameToLayer("Survivor");
        }
    }
    
    Vector3 GetRandomPositionOnTerrain()
    {
        Vector3 terrainPosition = terrain.transform.position;
        float terrainWidth = terrain.terrainData.size.x;
        float terrainLength = terrain.terrainData.size.z;
        
        float randomX = Random.Range(terrainPosition.x, terrainPosition.x + terrainWidth);
        float randomZ = Random.Range(terrainPosition.z, terrainPosition.z + terrainLength);
        float terrainHeight = terrain.SampleHeight(new Vector3(randomX, 0, randomZ)) + terrainPosition.y;
        
        return new Vector3(randomX, terrainHeight + 1.0f, randomZ);
    }
}