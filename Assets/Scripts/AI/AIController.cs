using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public sealed class AIController : UnitController
{
    [SerializeField] public GameObject capturableTargets;

    [SerializeField] public List<TaskData> taskDatas;

    public PlayerController playerController { get; private set; }

    public StrategicTask task1 = null;
    public StrategicTask task2 = null;
    public StrategicTask task3 = null;

    Squad explorationSquad;
    Squad militarySquad1;
    Squad militarySquad2;

    [SerializeField] float timeBetweenUtilitySystemUpdate = 5.0f;
    float previousUtilitySystemTime1 = 0.0f;
    float previousUtilitySystemTime2 = 0.0f;
    float previousUtilitySystemTime3 = 0.0f;

    protected override void Awake()
    {
        base.Awake();
    }

    protected override void Start()
    {
        base.Start();

        militarySquad1 = new Squad(this);
        militarySquad2 = new Squad(this);
        explorationSquad = new Squad(this);

        previousUtilitySystemTime1 = Time.time - timeBetweenUtilitySystemUpdate;
        previousUtilitySystemTime2 = Time.time + timeBetweenUtilitySystemUpdate / 3.0f - timeBetweenUtilitySystemUpdate;
        previousUtilitySystemTime3 = Time.time + timeBetweenUtilitySystemUpdate / 3.0f * 2.0f - timeBetweenUtilitySystemUpdate;

        playerController = FindObjectOfType<PlayerController>();

        InvokeRepeating("Cheat", 4.0f, 4.0f);
    }

    protected override void Update()
    {
        base.Update();
        UpdateTasks();

        UtilitySystemUpdate(ref task1, 0.1f, ref previousUtilitySystemTime1);
        UtilitySystemUpdate(ref task2, 0.15f, ref previousUtilitySystemTime2);
        UtilitySystemUpdate(ref task3, 0.35f, ref previousUtilitySystemTime3);
    }

    void Cheat()
    {
        TotalBuildPoints += 1;
    }

    void UpdateTasks()
    {
        if (task1 != null && !task1.isComplete)
            task1.UpdateTask();
        if (task2 != null && !task2.isComplete)
            task2.UpdateTask();
        if (task3 != null && !task3.isComplete)
            task3.UpdateTask();
    }

    void UtilitySystemUpdate(ref StrategicTask task, float scoreThreshold, ref float previousTime)
    {
        if (previousTime + timeBetweenUtilitySystemUpdate > Time.time)
            return;
        previousTime = Time.time;

        float score = scoreThreshold;

        StrategicTask tempTask = new PlaceDefendUnitTask((task != null && task.squad != null) ? task.squad : GetRandomSquad());
        if (tempTask.Evaluate(this, ref score))
        {
            if (task != null)
                task.EndTask();
            task = tempTask;
            task.StartTask(this);
            return;
        }

        if (task == null || task.isComplete)
        {
            if (explorationSquad.State == E_TASK_STATE.Free && IsSquadAvailable())
            {
                tempTask = new CapturePointTask(explorationSquad.State == E_TASK_STATE.Free ? explorationSquad : GetRandomAvailableSquad());
                if (tempTask.Evaluate(this, ref score))
                    task = tempTask;
            }
            
            tempTask = new CreateFactoryTask();
            if (tempTask.Evaluate(this, ref score))
                task = tempTask;

            tempTask = new CreateMinerTask();
            if (tempTask.Evaluate(this, ref score))
                task = tempTask;

            tempTask = new PlaceTurretTask();
            if (tempTask.Evaluate(this, ref score))
                task = tempTask;

            if (IsSquadAvailable())
            {
                tempTask = new AttackTargetTask(GetRandomAvailableSquad());
                if (tempTask.Evaluate(this, ref score))
                    task = tempTask;
            }

            if (score > scoreThreshold)
                task.StartTask(this);
        }
    }

    Squad GetRandomSquad()
    {
        if (IsSquadAvailable())
            return GetRandomAvailableSquad();

        int random = Random.Range(0, 3);

        if (random == 0)
            return militarySquad1;
        else if (random == 1)
            return militarySquad2;
        else
            return explorationSquad;
    }

    Squad GetRandomAvailableSquad()
    {
        if (!IsSquadAvailable())
            return null;

        const int squadCount = 3;

        int random = Random.Range(0, squadCount);
        int i = 0;

        while(i < squadCount)
        {
            if (random == 0 && militarySquad1.State == E_TASK_STATE.Free)
                return militarySquad1;
            else if (random == 1 && militarySquad2.State == E_TASK_STATE.Free)
                return militarySquad2;
            else if (random == 2 && explorationSquad.State == E_TASK_STATE.Free)
                return explorationSquad;

            random = (random + 1) % squadCount;
            ++i;
        }

        return null;
    }

    bool IsSquadAvailable()
    {
        if (militarySquad1.State == E_TASK_STATE.Free 
        ||  militarySquad2.State == E_TASK_STATE.Free 
        ||  explorationSquad.State == E_TASK_STATE.Free)
            return true;
        return false;
    }
}
