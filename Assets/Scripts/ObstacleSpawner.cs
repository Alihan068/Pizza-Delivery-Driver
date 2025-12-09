using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

public class ObstacleSpawner : MonoBehaviour {
    [SerializeField] float spawnInterval = 2f;
    [SerializeField] float xPos;
    [SerializeField] float yPos;
    [SerializeField] List<GameObject> objectList;

    [SerializeField] float checkRadius = 1f;
    [SerializeField] LayerMask obstacleLayer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() {
        InvokeRepeating(nameof(SpawnObstacle), spawnInterval, spawnInterval);

    }

    // Update is called once per frame
    void Update() {

    }


    void SpawnObstacle() {

        int maxAttempts = 10;

        for (int i = 0; i < maxAttempts; i++) {

            float randomX = Random.Range(-xPos, xPos);
            float randomY = Random.Range(-yPos, yPos);
            Vector2 spawnCenter = transform.position;
            Vector2 randomPositionCandidate = spawnCenter + new Vector2(randomX, randomY);

            Collider2D hit = Physics2D.OverlapCircle(randomPositionCandidate, checkRadius, obstacleLayer);

            if (hit == null) {
            GameObject randomObstacle = objectList[Random.Range(0, objectList.Count)];
                Instantiate(randomObstacle, randomPositionCandidate, Quaternion.identity);
                return;

            }
        }
    }

    void OnDrawGizmos() {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(xPos * 2, yPos * 2, 0));
    }
}
