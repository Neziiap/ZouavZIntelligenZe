using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public sealed class Factory : BaseEntity
{
    [SerializeField]
    FactoryDataScriptable FactoryData = null;

    GameObject[] UnitPrefabs = null;
    GameObject[] FactoryPrefabs = null;
    int RequestedEntityBuildIndex = -1;
    StrategicTask task;
    Image BuildGaugeImage;
    float CurrentBuildDuration = 0f;
    float EndBuildDate = 0f;
    int SpawnCount = 0;
    /* !! max available unit count in menu is set to 9, available factories count to 3 !! */
    const int MaxAvailableUnits = 9;
    const int MaxAvailableFactories = 3;

    UnitController Controller = null;

    [SerializeField]
    int MaxBuildingQueueSize = 100;
    Queue<KeyValuePair<int, StrategicTask>> BuildingQueue = new Queue<KeyValuePair<int, StrategicTask>>();
    public enum State
    {
        Available = 0,
        UnderConstruction,
        BuildingUnit,
    }
    public State CurrentState { get; private set; }
    public bool IsUnderConstruction { get { return CurrentState == State.UnderConstruction; } }
    public int Cost { get { return FactoryData.Cost; } }
    public FactoryDataScriptable GetFactoryData { get { return FactoryData; } }
    public int AvailableUnitsCount { get { return Mathf.Min(MaxAvailableUnits, FactoryData.AvailableUnits.Length); } }
    public int AvailableFactoriesCount { get { return Mathf.Min(MaxAvailableFactories, FactoryData.AvailableFactories.Length); } }
    public Action<Unit> OnUnitBuilt;
    public Action<Factory> OnFactoryBuilt;
    public Action<Turret> OnTurretBuild;
    public Action OnBuildCanceled;
    public bool IsBuildingUnit { get { return CurrentState == State.BuildingUnit; } }
    public int influence = 2;
    public GameObject TurretPrefab = null;

    #region MonoBehaviour methods
    protected override void Awake()
    {
        base.Awake();

        BuildGaugeImage = transform.Find("Canvas/BuildProgressImage").GetComponent<Image>();
        if (BuildGaugeImage)
        {
            BuildGaugeImage.fillAmount = 0f;
            BuildGaugeImage.color = GameServices.GetTeamColor(GetTeam());
        }

        if (FactoryData == null)
        {
            Debug.LogWarning("Missing FactoryData in " + gameObject.name);
        }
        HP = FactoryData.MaxHP;
        OnDeadEvent += Factory_OnDead;

        UnitPrefabs = new GameObject[FactoryData.AvailableUnits.Length];
        FactoryPrefabs = new GameObject[FactoryData.AvailableFactories.Length];

        // Load from resources actual Unit prefabs from template data
        for (int i = 0; i < FactoryData.AvailableUnits.Length; i++)
        {
            GameObject templateUnitPrefab = FactoryData.AvailableUnits[i];
            string path = "Prefabs/Units/" + templateUnitPrefab.name + "_" + Team.ToString();
            UnitPrefabs[i] = Resources.Load<GameObject>(path);
            if (UnitPrefabs[i] == null)
                Debug.LogWarning("could not find Unit Prefab at " + path);
        }

        // Load from resources actual Factory prefabs from template data
        for (int i = 0; i < FactoryData.AvailableFactories.Length; i++)
        {
            GameObject templateFactoryPrefab = FactoryData.AvailableFactories[i];
            string path = "Prefabs/Factories/" + templateFactoryPrefab.name + "_" + Team.ToString();
            FactoryPrefabs[i] = Resources.Load<GameObject>(path);
        }
        TurretPrefab = Resources.Load<GameObject>("Prefabs/Turrets/Turret_" + Team.ToString());

    }
    protected override void Start()
    {
        base.Start();
        GameServices.GetGameState().IncreaseTeamScore(Team);
        Controller = GameServices.GetControllerByTeam(Team);
        Map.Instance.AddFactory(this, Team);
    }
    override protected void Update()
    {
        switch (CurrentState)
        {
            case State.Available:
                break;

            case State.UnderConstruction:
                // $$$ TODO : improve construction progress rendering
                if (Time.time > EndBuildDate)
                {
                    CurrentState = State.Available;
                    BuildGaugeImage.fillAmount = 0f;
                }
                else if (BuildGaugeImage)
                    BuildGaugeImage.fillAmount = 1f - (EndBuildDate - Time.time) / FactoryData.BuildDuration;
                break;

            case State.BuildingUnit:
                if (Time.time > EndBuildDate)
                {
                    OnUnitBuilt?.Invoke(BuildUnit());
                    OnUnitBuilt = null; // remove registered methods
                    CurrentState = State.Available;

                    // manage build queue : chain with new unit build if necessary
                    if (BuildingQueue.Count != 0)
                    {
                        KeyValuePair<int, StrategicTask> unitInfo = BuildingQueue.Dequeue();
                        StartBuildUnit(unitInfo.Key, unitInfo.Value);
                    }
                }
                else if (BuildGaugeImage)
                    BuildGaugeImage.fillAmount = 1f - (EndBuildDate - Time.time) / CurrentBuildDuration;
                break;
        }
    }
    #endregion
    void Factory_OnDead()
    {
        if (FactoryData.DeathFXPrefab)
        {
            GameObject fx = Instantiate(FactoryData.DeathFXPrefab, transform);
            fx.transform.parent = null;
        }

        Map.Instance.RemoveFactory(this);
        GameServices.GetGameState().DecreaseTeamScore(Team);
        Destroy(gameObject);
    }
    #region IRepairable
    override public bool NeedsRepairing()
    {
        return HP < GetFactoryData.MaxHP;
    }
    override public void Repair(int amount)
    {
        HP = Mathf.Min(HP + amount, GetFactoryData.MaxHP);
        base.Repair(amount);
    }
    override public void FullRepair()
    {
        Repair(GetFactoryData.MaxHP);
    }
    #endregion

    #region Unit building methods
    bool IsUnitIndexValid(int unitIndex)
    {
        if (unitIndex < 0 || unitIndex >= UnitPrefabs.Length)
        {
            Debug.LogWarning("Wrong unitIndex " + unitIndex);
            return false;
        }
        return true;
    }
    public UnitDataScriptable GetBuildableUnitData(int unitIndex)
    {
        if (IsUnitIndexValid(unitIndex) == false)
            return null;

        return UnitPrefabs[unitIndex].GetComponent<Unit>().GetUnitData;
    }
    public int GetUnitCost(int unitIndex)
    {
        UnitDataScriptable data = GetBuildableUnitData(unitIndex);
        if (data)
            return data.Cost;

        return 0;
    }
    public int GetQueuedCount(int unitIndex)
    {
        int counter = 0;
        foreach(KeyValuePair<int, StrategicTask> id in BuildingQueue)
        {
            if (id.Key == unitIndex)
                counter++;
        }
        return counter;
    }
    public bool RequestUnitBuild(int unitMenuIndex, StrategicTask _task)
    {
        int cost = GetUnitCost(unitMenuIndex);
        if (Controller.TotalBuildPoints < cost || BuildingQueue.Count >= MaxBuildingQueueSize)
            return false;

        Controller.TotalBuildPoints -= cost;

        StartBuildUnit(unitMenuIndex, _task);

        return true;
    }
    void StartBuildUnit(int unitMenuIndex, StrategicTask _task)
    {
        if (IsUnitIndexValid(unitMenuIndex) == false)
            return;

        // Factory is being constucted
        if (CurrentState == State.UnderConstruction)
            return;

        // Build queue
        if (CurrentState == State.BuildingUnit)
        {
            if (BuildingQueue.Count < MaxBuildingQueueSize)
                BuildingQueue.Enqueue(new KeyValuePair<int, StrategicTask>(unitMenuIndex, _task));
            return;
        }

        CurrentBuildDuration = GetBuildableUnitData(unitMenuIndex).BuildDuration;
        //Debug.Log("currentBuildDuration " + CurrentBuildDuration);

        CurrentState = State.BuildingUnit;
        EndBuildDate = Time.time + CurrentBuildDuration;

        RequestedEntityBuildIndex = unitMenuIndex;
        task = _task;

        OnUnitBuilt += (Unit unit) =>
        {
            if (unit != null)
            {
                Controller.AddUnit(unit);
                (Controller as PlayerController)?.UpdateFactoryBuildQueueUI(this, RequestedEntityBuildIndex);
                if (Controller is AIController)
                    task.SetUnit(unit);
            }
        };
    }

    // Finally spawn requested unit
    Unit BuildUnit()
    {
        if (IsUnitIndexValid(RequestedEntityBuildIndex) == false)
            return null;

        CurrentState = State.Available;

        GameObject unitPrefab = UnitPrefabs[RequestedEntityBuildIndex];

        if (BuildGaugeImage)
            BuildGaugeImage.fillAmount = 0f;

        int slotIndex = SpawnCount % FactoryData.NbSpawnSlots;
        // compute simple spawn position around the factory
        float angle = 2f * Mathf.PI / FactoryData.NbSpawnSlots * slotIndex;
        int offsetIndex = Mathf.FloorToInt(SpawnCount / FactoryData.NbSpawnSlots);
        float radius = FactoryData.SpawnRadius + offsetIndex * FactoryData.RadiusOffset;
        Vector3 spawnPos = transform.position + new Vector3(radius * Mathf.Cos(angle), 0f, radius * Mathf.Sin(angle));

        // !! Flying units require a specific layer to be spawned on !!
        bool isFlyingUnit = unitPrefab.GetComponent<Unit>().GetUnitData.IsFlying;
        int layer = isFlyingUnit ? LayerMask.NameToLayer("FlyingZone") : LayerMask.NameToLayer("Floor");

        // cast position on ground
        RaycastHit raycastInfo;
        Ray ray = new Ray(spawnPos, Vector3.down);
        if (Physics.Raycast(ray, out raycastInfo, 10f, 1 << layer))
            spawnPos = raycastInfo.point;

        Transform teamRoot = GameServices.GetControllerByTeam(GetTeam())?.GetTeamRoot();
        GameObject unitInst = Instantiate(unitPrefab, spawnPos, Quaternion.identity, teamRoot);
        unitInst.name = unitInst.name.Replace("(Clone)", "_" + SpawnCount.ToString());
        Unit newUnit = unitInst.GetComponent<Unit>();
        newUnit.Init(GetTeam());

        SpawnCount++;

        // disable build cancelling callback
        OnBuildCanceled = null;

        return newUnit;
    }
    public void CancelCurrentBuild()
    {
        if (CurrentState == State.UnderConstruction || CurrentState == State.Available)
            return;

        CurrentState = State.Available;

        // refund build points
        Controller.TotalBuildPoints += GetUnitCost(RequestedEntityBuildIndex);
        foreach(KeyValuePair<int, StrategicTask> unitIndex in BuildingQueue)
        {
            Controller.TotalBuildPoints += GetUnitCost(unitIndex.Key);
        }
        BuildingQueue.Clear();

        BuildGaugeImage.fillAmount = 0f;
        CurrentBuildDuration = 0f;
        RequestedEntityBuildIndex = -1;

        OnBuildCanceled?.Invoke();
        OnBuildCanceled = null;
    }
    #endregion

    #region Factory building methods
    public GameObject GetFactoryPrefab(int factoryIndex)
    {
        return IsFactoryIndexValid(factoryIndex) ? FactoryPrefabs[factoryIndex] : null;
    }
    bool IsFactoryIndexValid(int factoryIndex)
    {
        if (factoryIndex < 0 || factoryIndex >= FactoryPrefabs.Length)
        {
            Debug.LogWarning("Wrong factoryIndex " + factoryIndex);
            return false;
        }
        return true;
    }
    public FactoryDataScriptable GetBuildableFactoryData(int factoryIndex)
    {
        if (IsFactoryIndexValid(factoryIndex) == false)
            return null;

        return FactoryPrefabs[factoryIndex].GetComponent<Factory>().GetFactoryData;
    }
    public int GetFactoryCost(int factoryIndex)
    {
        FactoryDataScriptable data = GetBuildableFactoryData(factoryIndex);
        if (data)
            return data.Cost;

        return 0;
    }
    public bool CanPositionFactory(int factoryIndex, Vector3 buildPos)
    {
        if (IsFactoryIndexValid(factoryIndex) == false)
            return false;

        if (GameServices.IsPosInPlayableBounds(buildPos) == false)
            return false;

        GameObject factoryPrefab = FactoryPrefabs[factoryIndex];

        Vector3 extent = factoryPrefab.GetComponent<BoxCollider>().size / 2f;

        float overlapYOffset = 0.1f;
        buildPos += Vector3.up * (extent.y + overlapYOffset);

        if (Physics.CheckBox(buildPos, extent))
        //foreach(Collider col in Physics.OverlapBox(buildPos, halfExtent))
        {
            //Debug.Log("Overlap");
            return false;
        }

        return true;
    }
    public Factory StartBuildFactory(int factoryIndex, Vector3 buildPos)
    {
        if (IsFactoryIndexValid(factoryIndex) == false)
            return null;

        if (CurrentState == State.BuildingUnit)
            return null;

        GameObject factoryPrefab = FactoryPrefabs[factoryIndex];
        Transform teamRoot = GameServices.GetControllerByTeam(GetTeam())?.GetTeamRoot();
        GameObject factoryInst = Instantiate(factoryPrefab, buildPos, Quaternion.identity, teamRoot);
        factoryInst.name = factoryInst.name.Replace("(Clone)", "_" + SpawnCount.ToString());
        Factory newFactory = factoryInst.GetComponent<Factory>();
        newFactory.Init(GetTeam());
        newFactory.StartSelfConstruction();

        return newFactory;
    }
    void StartSelfConstruction()
    {
        CurrentState = State.UnderConstruction;

        EndBuildDate = Time.time + FactoryData.BuildDuration;
    }

    #endregion

    #region Turret building methods
    public bool CanPositionTurret(Vector3 buildPos)
    {
        if (GameServices.IsPosInPlayableBounds(buildPos) == false)
            return false;

        Vector3 extent = TurretPrefab.GetComponent<BoxCollider>().size / 2f;

        float overlapYOffset = 0.1f;
        buildPos += Vector3.up * (extent.y + overlapYOffset);

        if (Physics.CheckBox(buildPos, extent))
        //foreach(Collider col in Physics.OverlapBox(buildPos, halfExtent))
        {
            //Debug.Log("Overlap");
            return false;
        }

        return true;
    }
    public Turret StartBuildTurret(Vector3 buildPos)
    {
        if (CurrentState == State.BuildingUnit)
            return null;

        Transform teamRoot = GameServices.GetControllerByTeam(GetTeam())?.GetTeamRoot();
        GameObject turretInst = Instantiate(TurretPrefab, buildPos, Quaternion.identity, teamRoot);
        turretInst.name = turretInst.name.Replace("(Clone)", "_" + SpawnCount.ToString());
        Turret newTurret = turretInst.GetComponent<Turret>();
        newTurret.Init(GetTeam());

        return newTurret;
    }
    #endregion
}
