using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField]
    float LifeTime = 0.5f;
    [SerializeField]
    float MoveForce = 2000f;

    float ShootDate = 0f;
    ETeam TeamOwner;

    public void ShootToward(Vector3 direction, ETeam ownerTeam)
    {
        ShootDate = Time.time;
        GetComponent<Rigidbody>().AddForce(direction.normalized * MoveForce);
        TeamOwner = ownerTeam;
    }

    #region MonoBehaviour methods
    void Update()
    {
        if ((Time.time - ShootDate) > LifeTime)
        {
            Destroy(gameObject);
        }
    }
    void OnCollisionEnter(Collision col)
    {
        if (col.gameObject.GetComponent<Unit>()?.GetTeam() == TeamOwner)
            return;

        Destroy(gameObject);
    }
    #endregion
}
