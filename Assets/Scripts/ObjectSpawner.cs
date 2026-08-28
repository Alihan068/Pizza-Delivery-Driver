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

        public int maxObjectLimit = 50;
        public List<GameObject> objectList;

        public float checkRadius = 1f;
        public LayerMask obstacleLayer;

        [System.NonSerialized] public List<GameObject> spawnedInstances = new List<GameObject>();
    }

    [SerializeField] List<ObstacleGroup> obstacleGroups;

    void Start() {
        foreach (ObstacleGroup group in obstacleGroups) {
            StartCoroutine(SpawnObstacleRoutine(group));
        }
    }

    IEnumerator SpawnObstacleRoutine(ObstacleGroup group) {
        WaitForSeconds wait = new WaitForSeconds(group.spawnInterval);
        while (true) {
            yield return wait;
            SpawnObstacle(group);
        }
    }

    void SpawnObstacle(ObstacleGroup group) {
        group.spawnedInstances.RemoveAll(go => go == null);
        if (group.spawnedInstances.Count >= group.maxObjectLimit) return;

        int maxAttempts = 10;

        for (int i = 0; i < maxAttempts; i++) {

            float randomX = UnityEngine.Random.Range(-group.xPos, group.xPos);
            float randomY = UnityEngine.Random.Range(-group.yPos, group.yPos);

            Vector2 spawnCenter = (Vector2)transform.position + new Vector2(group.centerOffsetX, group.centerOffsetY);
            Vector2 randomPositionCandidate = spawnCenter + new Vector2(randomX, randomY);

            Collider2D hit = Physics2D.OverlapCircle(randomPositionCandidate, group.checkRadius, group.obstacleLayer);

            if (hit == null) {
                GameObject randomObstacle = group.objectList[UnityEngine.Random.Range(0, group.objectList.Count)];
                GameObject spawned = Instantiate(randomObstacle, randomPositionCandidate, Quaternion.identity);
                group.spawnedInstances.Add(spawned);
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
