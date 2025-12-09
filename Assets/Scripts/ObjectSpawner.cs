using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections;

public class ObjectSpawner : MonoBehaviour {

    [System.Serializable]
    public class ObstacleGroup {
        public float spawnInterval = 2f;
        public float xPos;
        public float yPos;

        public float maxObjectLimit = 50;
        public List<GameObject> objectList;

        public float checkRadius = 1f;
        public LayerMask obstacleLayer;
    }

    [SerializeField] List<ObstacleGroup> obstacleGroups;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() {
        foreach (ObstacleGroup group in obstacleGroups) {
            StartCoroutine(SpawnObstacleRoutine(group));
        }
    }
    IEnumerator SpawnObstacleRoutine(ObstacleGroup group) {
        while (true) {
            yield return new WaitForSeconds(group.spawnInterval);
            SpawnObstacle(group);
        }
    }

    void SpawnObstacle(ObstacleGroup group) {
     
        int maxAttempts = 10;

        for (int i = 0; i < maxAttempts; i++) {

            float randomX = UnityEngine.Random.Range(-group.xPos, group.xPos);
            float randomY = UnityEngine.Random.Range(-group.yPos, group.yPos);
            Vector2 spawnCenter = transform.position;
            Vector2 randomPositionCandidate = spawnCenter + new Vector2(randomX, randomY);

            Collider2D hit = Physics2D.OverlapCircle(randomPositionCandidate, group.checkRadius, group.obstacleLayer);

            if (hit == null) {
                GameObject randomObstacle = group.objectList[UnityEngine.Random.Range(0, group.objectList.Count)];
                Instantiate(randomObstacle, randomPositionCandidate, Quaternion.identity);
                return;

            }

        }

    }

    void OnDrawGizmosSelected() {

        foreach (var group in obstacleGroups) {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(group.xPos * 2, group.yPos * 2, 0));
        }
    }
}
