using UnityEngine;

public class FollowCamera : MonoBehaviour

{
    [SerializeField] GameObject target;

    void FixedUpdate()
    {
        transform.position = target.transform.position + new Vector3(0,0,-10);
    }
}
