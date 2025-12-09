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
        public float centerOffsetX;
        public float centerOffsetY;

        public float maxObjectLimit = 50;
        public List<GameObject> objectList;

        public float checkRadius = 1f;
        public LayerMask obstacleLayer;
    }

    [SerializeField] List<ObstacleGroup> obstacleGroups;

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

            Vector2 spawnCenter = (Vector2)transform.position + new Vector2(group.centerOffsetX, group.centerOffsetY);
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
        if (obstacleGroups == null) return;

        foreach (var group in obstacleGroups) {
            Gizmos.color = Color.yellow;
            Vector3 centerPos = transform.position + new Vector3(group.centerOffsetX, group.centerOffsetY, 0);
            Gizmos.DrawWireCube(centerPos, new Vector3(group.xPos * 2, group.yPos * 2, 0));
        }
    }
}