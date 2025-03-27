using UnityEngine;
using System.Collections;

/// <summary>
/// Controls the visual and audio effects of a drone explosion.
/// Attach this to your explosion prefab.
/// </summary>
public class DroneExplosionEffect : MonoBehaviour
{
    [Header("Visual Effects")]
    public ParticleSystem explosionParticles;
    public ParticleSystem fireParticles;
    public ParticleSystem smokeParticles;
    public Light explosionLight;
    
    [Header("Debris")]
    public GameObject[] debrisPrefabs;
    public int minDebrisCount = 5;
    public int maxDebrisCount = 10;
    public float debrisForce = 10f;
    public float torqueForce = 5f;
    
    [Header("Audio")]
    public AudioClip explosionSound;
    public AudioClip[] debrisSounds;
    public float volumeScale = 1.0f;
    
    [Header("Camera Effects")]
    public float cameraShakeDuration = 0.5f;
    public float cameraShakeIntensity = 0.3f;
    
    private AudioSource audioSource;
    
    void Start()
    {
        // Get or add audio source
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // Play explosion sound
        if (audioSource != null && explosionSound != null)
        {
            audioSource.PlayOneShot(explosionSound, volumeScale);
        }
        
        // Start explosion particles
        if (explosionParticles != null)
        {
            explosionParticles.Play();
        }
        
        // Start fire particles with slight delay
        if (fireParticles != null)
        {
            fireParticles.Play();
        }
        
        // Start smoke particles
        if (smokeParticles != null)
        {
            smokeParticles.Play();
        }
        
        // Flash explosion light
        if (explosionLight != null)
        {
            StartCoroutine(FlashLight());
        }
        
        // Spawn debris
        SpawnDebris();
        
        // Shake camera
        ShakeCamera();
        
        // Self-destruct after effects are complete
        float longestDuration = 0f;
        
        if (explosionParticles != null)
            longestDuration = Mathf.Max(longestDuration, explosionParticles.main.duration);
            
        if (fireParticles != null)
            longestDuration = Mathf.Max(longestDuration, fireParticles.main.duration);
            
        if (smokeParticles != null)
            longestDuration = Mathf.Max(longestDuration, smokeParticles.main.duration);
            
        Destroy(gameObject, longestDuration + 2f);
    }
    
    void SpawnDebris()
    {
        if (debrisPrefabs == null || debrisPrefabs.Length == 0)
            return;
            
        int debrisCount = Random.Range(minDebrisCount, maxDebrisCount + 1);
        
        for (int i = 0; i < debrisCount; i++)
        {
            // Select random debris prefab
            GameObject debrisPrefab = debrisPrefabs[Random.Range(0, debrisPrefabs.Length)];
            
            // Instantiate debris
            GameObject debris = Instantiate(debrisPrefab, transform.position, Random.rotation);
            
            // Add rigidbody if it doesn't have one
            Rigidbody rb = debris.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = debris.AddComponent<Rigidbody>();
            }
            
            // Apply random force and torque
            Vector3 randomDirection = Random.insideUnitSphere.normalized;
            rb.AddForce(randomDirection * debrisForce, ForceMode.Impulse);
            rb.AddTorque(randomDirection * torqueForce, ForceMode.Impulse);
            
            // Add audio if available
            if (debrisSounds != null && debrisSounds.Length > 0)
            {
                AudioSource debrisAudio = debris.GetComponent<AudioSource>();
                if (debrisAudio == null)
                {
                    debrisAudio = debris.AddComponent<AudioSource>();
                }
                
                // Play random debris sound
                AudioClip randomClip = debrisSounds[Random.Range(0, debrisSounds.Length)];
                if (randomClip != null)
                {
                    debrisAudio.pitch = Random.Range(0.8f, 1.2f);
                    debrisAudio.PlayOneShot(randomClip, Random.Range(0.3f, 0.7f));
                }
            }
            
            // Destroy debris after a few seconds
            Destroy(debris, Random.Range(3f, 6f));
        }
    }
    
    IEnumerator FlashLight()
    {
        float startIntensity = explosionLight.intensity;
        float duration = 0.2f;
        float timer = 0f;
        
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;
            explosionLight.intensity = Mathf.Lerp(startIntensity, 0, t);
            yield return null;
        }
        
        explosionLight.intensity = 0;
    }
    
    void ShakeCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            CameraShake shaker = mainCamera.GetComponent<CameraShake>();
            if (shaker != null)
            {
                shaker.Shake(cameraShakeDuration, cameraShakeIntensity);
            }
            else
            {
                // Add a simple camera shake effect
                shaker = mainCamera.gameObject.AddComponent<CameraShake>();
                shaker.Shake(cameraShakeDuration, cameraShakeIntensity);
            }
        }
    }
}

/// <summary>
/// Simple camera shake effect
/// </summary>
public class CameraShake : MonoBehaviour
{
    private Vector3 originalPosition;
    private float shakeTimeRemaining;
    private float shakePower;
    
    void Start()
    {
        originalPosition = transform.localPosition;
    }
    
    void Update()
    {
        if (shakeTimeRemaining > 0)
        {
            shakeTimeRemaining -= Time.deltaTime;
            
            float xShake = Random.Range(-1f, 1f) * shakePower;
            float yShake = Random.Range(-1f, 1f) * shakePower;
            
            transform.localPosition = originalPosition + new Vector3(xShake, yShake, 0);
            
            if (shakeTimeRemaining <= 0)
            {
                transform.localPosition = originalPosition;
            }
        }
    }
    
    public void Shake(float duration, float power)
    {
        originalPosition = transform.localPosition;
        shakeTimeRemaining = duration;
        shakePower = power;
    }
}