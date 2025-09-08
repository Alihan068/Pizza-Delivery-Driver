using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

public class ObstacleSpawner : MonoBehaviour
{
    [SerializeField] float spawnInterval = 2f;
    [SerializeField] float xPos;
    [SerializeField] float yPos;
    [SerializeField] List<GameObject> objectList;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InvokeRepeating(nameof(SpawnObstacle), spawnInterval, spawnInterval);
        
    }

    // Update is called once per frame
    void Update()
    {
         
    }
   

    void SpawnObstacle() {
        float randomX = Random.Range(-xPos, xPos);
        float randomY = Random.Range(-yPos, yPos);
        GameObject randomObstacle = objectList[Random.Range(0, objectList.Count)];

        Vector2 randomPosition = new Vector2(randomX, randomY);
        

        Instantiate(randomObstacle, randomPosition, Quaternion.identity, transform);
    }
}
