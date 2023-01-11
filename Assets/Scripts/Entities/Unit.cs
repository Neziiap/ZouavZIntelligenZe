using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;
using UnityEngine.Events;

public enum E_MODE
{
    Agressive,
    Defensive,
    Flee,
    FollowInstruction
}

public class Unit : BaseEntity
{
    [SerializeField]
    UnitDataScriptable UnitData = null;

    Transform BulletSlot;
    float LastActionDate = 0f;
    TargetBuilding CaptureTarget = null;
    NavMeshAgent NavMeshAgent;
    public BaseEntity EntityTarget = null;
    public UnitDataScriptable GetUnitData { get { return UnitData; } }
    public int Cost { get { return UnitData.Cost; } }
    public int GetTypeId { get { return UnitData.TypeId; } }
    public bool needToCapture = false;
    public bool IsRepairing = false;
    
    [SerializeField] E_MODE mode = E_MODE.Defensive;
    public BaseEntity entityInRange = null;
    private float passiveFleeDistance = 25f;
    private bool isFleeing = false;
    private BaseEntity tempEntityTarget = null;
    private BaseEntity entityToKill = null;

    public Vector3 GridPosition;
    //Move speed of the squad
    public float CurrentMoveSpeed;
    public Dictionary<Tile, float> currentTilesInfluence = new Dictionary<Tile, float>();
    private float influence;
    public float Influence { get { return influence; } }

    public Squad squad;

    override public void Init(ETeam _team)
    {
        if (IsInitialized)
            return;

        base.Init(_team);

        HP = UnitData.MaxHP;
        OnDeadEvent += Unit_OnDead;
    }
    void Unit_OnDead()
    {
        if (squad != null)
            squad.RemoveUnit(this);
        
        StopCapture();

        if (GetUnitData.DeathFXPrefab)
        {
            GameObject fx = Instantiate(GetUnitData.DeathFXPrefab, transform);
            fx.transform.parent = null;
        }

        foreach (KeyValuePair<Tile, float> t in currentTilesInfluence)
            t.Key.militaryInfluence -= t.Value;

        Destroy(gameObject);
    }
    #region MonoBehaviour methods
    override protected void Awake()
    {
        base.Awake();

        NavMeshAgent = GetComponent<NavMeshAgent>();
        BulletSlot = transform.Find("BulletSlot");

        // fill NavMeshAgent parameters
        CurrentMoveSpeed = GetUnitData.Speed;
        NavMeshAgent.speed = CurrentMoveSpeed;
        NavMeshAgent.angularSpeed = GetUnitData.AngularSpeed;
        NavMeshAgent.acceleration = GetUnitData.Acceleration;
        NavMeshAgent.stoppingDistance = 1f;
        influence = UnitData.AttackDistanceMax * Mathf.Sqrt(2) / Map.Instance.squareSize;
    }
    override protected void Start()
    {
        // Needed for non factory spawned units (debug)
        if (!IsInitialized)
            Init(Team);

        base.Start();
        InvokeRepeating("CheckForEnemy", 1f, 1f);
    }
    override protected void Update()
    {
        // Attack / repair task debug test $$$ to be removed for AI implementation
        if (EntityTarget != null)
        {
            if (EntityTarget.GetTeam() != GetTeam())
                ComputeAttack();
            else
                ComputeRepairing();
        }
        if (isFleeing)
            CheckForStop();

        if (entityToKill)
            ChaseEntityToKill();
	}
    #endregion

    #region IRepairable
    override public bool NeedsRepairing()
    {
        return HP < GetUnitData.MaxHP;
    }
    override public void Repair(int amount)
    {
        HP = Mathf.Min(HP + amount, GetUnitData.MaxHP);
        base.Repair(amount);
    }
    override public void FullRepair()
    {
        Repair(GetUnitData.MaxHP);
    }
    #endregion

    #region Tasks methods : Moving, Capturing, Targeting, Attacking, Repairing ...

    // $$$ To be updated for AI implementation $$$

    // Moving Task
    public void SetTargetPos(Vector3 pos)
    {
        if (EntityTarget != null)
        {
            if (entityToKill)
            {
                entityToKill = null;
                EntityTarget.OnDeadEvent -= OnModeActionEnd;
            }
            EntityTarget.OnDeadEvent -= OnModeActionEnd;
            EntityTarget = null;
        }
        
        if (NavMeshAgent)
        {
            NavMeshAgent.speed = CurrentMoveSpeed;
            NavMeshAgent.SetDestination(pos);
        }
    }

    // Targetting Task - attack
    public void SetAttackTarget(BaseEntity target)
    {
        if (target == null)
            return;

        if (CaptureTarget != null && !needToCapture)
            StopCapture();

        if (target.GetTeam() != GetTeam())
        {
            if (!CanAttack(target))
                SetTargetPos(target.gameObject.transform.position);
            
            EntityTarget = target;
        }
    }

    // Targetting Task - capture
    public void SetCaptureTarget(TargetBuilding target)
    {
        if (target == null)
            return;

        if (EntityTarget != null)
            EntityTarget = null;
        
        StopCapture();

        if (target.GetTeam() != GetTeam())
        {
            if (CanCapture(target))
                StartCapture(target);

            else
            {
                SetTargetPos(target.gameObject.transform.position);
                CaptureTarget = target;
                needToCapture = true;
            }
        }
    }

    public void NeedToCapture(TargetBuilding target)
    {
        CaptureTarget = target;
        needToCapture = true;
    }

    // Targetting Task - repairing
    public void SetRepairTarget(BaseEntity entity)
    {
        if (CanRepair(entity) == false)
            return;

        if (CaptureTarget != null)
            StopCapture();

        if (entity.GetTeam() == GetTeam())
            StartRepairing(entity);
    }
    public bool CanAttack(BaseEntity target)
    {
        // distance check
        if (target == null || (target.transform.position - transform.position).sqrMagnitude > GetUnitData.AttackDistanceMax * GetUnitData.AttackDistanceMax)
            return false;

        return true;
    }

    // Attack Task
    public void ComputeAttack(BaseEntity target = null)
    {
        if(target != null)
            EntityTarget = target;

        if (CanAttack(EntityTarget) == false)
            return;

        if (NavMeshAgent)
            StopMovement();

        transform.LookAt(EntityTarget.transform);
        // only keep Y axis
        Vector3 eulerRotation = transform.eulerAngles;
        eulerRotation.x = 0f;
        eulerRotation.z = 0f;
        transform.eulerAngles = eulerRotation;

        if ((Time.time - LastActionDate) > UnitData.AttackFrequency)
        {
            LastActionDate = Time.time;
            // visual only ?
            if (UnitData.BulletPrefab)
            {
                GameObject newBullet = Instantiate(UnitData.BulletPrefab, BulletSlot);
                newBullet.transform.parent = null;
                newBullet.GetComponent<Bullet>().ShootToward(EntityTarget.transform.position - transform.position, Team);
            }
            // apply damages
            int damages = Mathf.FloorToInt(UnitData.DPS * UnitData.AttackFrequency);
            EntityTarget.AddDamage(damages, this);
        }
    }
    public bool CanCapture(TargetBuilding target)
    {
        // distance check
        if (target == null || (target.transform.position - transform.position).sqrMagnitude > GetUnitData.CaptureDistanceMax * GetUnitData.CaptureDistanceMax)
            return false;

        return true;
    }

    // Capture Task
    public void StartCapture(TargetBuilding target)
    {
        CaptureTarget = target;
        CaptureTarget.StartCapture(this);
        needToCapture = false;
    }

    public void StopCapture()
    {
        if (CaptureTarget != null)
        {
            CaptureTarget.StopCapture(this);
            CaptureTarget = null;
        }
    }

    public bool IsCapturing()
    {
        return CaptureTarget != null && !needToCapture;
    }

    // Repairing Task
    public bool CanRepair(BaseEntity target)
    {
        if (GetUnitData.CanRepair == false || target == null)
            return false;

        // distance check
        if ((target.transform.position - transform.position).sqrMagnitude > GetUnitData.RepairDistanceMax * GetUnitData.RepairDistanceMax)
            return false;

        return true;
    }
    public void StartRepairing(BaseEntity entity)
    {
        if (GetUnitData.CanRepair)
        {
            EntityTarget = entity;
        }
    }

    // $$$ TODO : add repairing visual feedback
    public void ComputeRepairing()
    {
        if (CanRepair(EntityTarget) == false)
            return;

        if (NavMeshAgent)
            StopMovement();

        transform.LookAt(EntityTarget.transform);
        // only keep Y axis
        Vector3 eulerRotation = transform.eulerAngles;
        eulerRotation.x = 0f;
        eulerRotation.z = 0f;
        transform.eulerAngles = eulerRotation;

        if ((Time.time - LastActionDate) > UnitData.RepairFrequency)
        {
            LastActionDate = Time.time;

            // apply reparing
            int amount = Mathf.FloorToInt(UnitData.RPS * UnitData.RepairFrequency);
            EntityTarget.Repair(amount);
        }
    }
    #endregion

    void CheckForEnemy()
    {
        int focusLayer = (1 << LayerMask.NameToLayer("Unit")) | (1 << LayerMask.NameToLayer("Turret")) | (1 << LayerMask.NameToLayer("Factory"));
        Collider[] inRangeColliders = Physics.OverlapSphere(transform.position, UnitData.AttackDistanceMax, focusLayer);

        entityInRange = null;

        foreach (Collider inRangeCollider in inRangeColliders)
        {
            if (inRangeCollider.GetComponent<BaseEntity>().GetTeam() != Team && inRangeCollider.GetComponent<BaseEntity>().GetTeam() != ETeam.Neutral)
            {
                entityInRange = inRangeCollider.GetComponent<BaseEntity>();
                break;
            }
        }

        if (EntityTarget != null || CaptureTarget != null || !IsAtDestination())
            return;

        BaseEntity tempFactoryTarget = null;
        foreach(Collider inRangeCollider in inRangeColliders)
        {
            if (inRangeCollider.GetComponent<BaseEntity>().GetTeam() != Team && !EntityTarget)
            {
                switch (mode)
                {
                    case E_MODE.Agressive:
                        if (inRangeCollider.GetComponent<Factory>() != null)
                        {
                            if (tempFactoryTarget == null)
                                tempFactoryTarget = EntityTarget;
                            continue;
                        }
                        AgressiveBehavior(inRangeCollider.GetComponent<BaseEntity>());
                        return;

                    case E_MODE.Defensive:
                        if (inRangeCollider.GetComponent<Factory>() != null)
                        {
                            if (tempFactoryTarget == null)
                                tempFactoryTarget = EntityTarget;
                            continue;
                        }
                        DefensiveBehavior(inRangeCollider.GetComponent<BaseEntity>());
                        return;

                    case E_MODE.Flee:
                        if (inRangeCollider.GetComponent<Factory>() != null)
                            continue;
                        FleeBehavior(inRangeCollider.GetComponent<BaseEntity>());
                        return;
                }
            }
        }

        if (tempFactoryTarget != null && tempEntityTarget == null)
        {
            if (mode == E_MODE.Agressive)
                AgressiveBehavior(tempFactoryTarget);
            else if (mode == E_MODE.Defensive)
                DefensiveBehavior(tempFactoryTarget);
        }
    }

    void AgressiveBehavior(BaseEntity inRangeEntity)
    {
        tempEntityTarget = EntityTarget;
        entityToKill = EntityTarget = inRangeEntity;
        EntityTarget.OnDeadEvent += OnModeActionEnd;
    }

    void DefensiveBehavior(BaseEntity inRangeEntity)
    {
        tempEntityTarget = EntityTarget;
        EntityTarget = inRangeEntity;
        EntityTarget.OnDeadEvent += OnModeActionEnd;
    }

    void FleeBehavior(BaseEntity inRangeEntity)
    {
        tempEntityTarget = EntityTarget;
        RaycastHit hit;
        Vector3 direction = Vector3.up + (transform.position - inRangeEntity.transform.position).normalized * passiveFleeDistance;
        int layerMask = (1 << LayerMask.NameToLayer("Floor")) | (1 << LayerMask.NameToLayer("Factory")) | (1 << LayerMask.NameToLayer("Target"));

        if (Physics.Raycast(transform.position + Vector3.up, direction.normalized, out hit, direction.magnitude, layerMask))
            direction = hit.point - transform.position;

        SetTargetPos(direction + transform.position);
        isFleeing = true;
    }

    void OnModeActionEnd()
    {
        if ((CaptureTarget || needToCapture) && squad != null)
        {
            TargetBuilding temp = CaptureTarget;
            CaptureTarget = null;
            squad.CaptureTask(temp);
        }

        else if (tempEntityTarget != null && squad != null)
            squad.AttackTask(tempEntityTarget);

        tempEntityTarget = null;
    }

    void CheckForStop()
    {
        if (NavMeshAgent.remainingDistance < NavMeshAgent.stoppingDistance && NavMeshAgent.remainingDistance > 0f)
        {
            isFleeing = false;
            OnModeActionEnd();
        }
    }

    void ChaseEntityToKill()
    {
        if ((entityToKill.transform.position - transform.position).magnitude > GetUnitData.AttackDistanceMax)
            SetAttackTarget(entityToKill);
    }

    public void StopMovement()
    {
        NavMeshAgent.SetDestination(transform.position);
    }

    public void SetMode(E_MODE newMode)
    {
        mode = newMode;
    }

    public bool IsAtDestination()
    {
        return NavMeshAgent.remainingDistance < NavMeshAgent.stoppingDistance;
    }

    public void UpdateTile(Vector3 centerTilePos, List<Tile> toUpdate, float currentInfluence)
    {
        List<Tile> nextUpdate = new List<Tile>();
        foreach (Tile tile in toUpdate)
        {
            if (currentInfluence < 0f || (centerTilePos - tile.position).magnitude > UnitData.AttackDistanceMax || currentTilesInfluence.ContainsKey(tile))
                continue;

            currentTilesInfluence.Add(tile, currentInfluence);

            tile.militaryInfluence += currentInfluence;

            foreach (Tile t in Map.Instance.GetNeighbours(tile))
                if (!currentTilesInfluence.ContainsKey(t) && !nextUpdate.Contains(t))
                    nextUpdate.Add(t);
        }

        if (nextUpdate.Count > 0)
            UpdateTile(centerTilePos, nextUpdate, currentInfluence - 1f);
    }
}
