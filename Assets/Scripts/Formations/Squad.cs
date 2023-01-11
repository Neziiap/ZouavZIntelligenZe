using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UIElements;

public enum E_TASK_STATE
{
    Free,
    Ongoing, // ongoing task but can be assign to another task
    Busy       // cannot be assign another task
}

public class Squad
{
    public List<Unit> members = new List<Unit>();
    private Formation SquadFormation;
    private float MoveSpeed = 100.0f;
    public UnitController Controller;
    //use to break formation and attack
    public bool CanBreakFormation = false;
    public bool SquadCapture = false;
    public bool SquadAttack = false;
    public bool SquadRepair = false;
    private TargetBuilding targetBuilding;
    public int totalCost = 0;
    
    public E_MODE SquadMode;
    //Target unit to destroy
    private BaseEntity SquadTarget = null;

    public E_TASK_STATE State;
    public bool isTemporary = false;

    public Squad(UnitController controller)
    {
        SquadFormation = new Formation(this);
        Controller = controller;
        SquadMode = E_MODE.Defensive;
    }

    public Squad(Squad squad)
    {
        SquadFormation = new Formation(squad.SquadFormation, this);
        Controller = squad.Controller;
        SquadMode = squad.SquadMode;
        State = squad.State;
        isTemporary = true;

        for(int i = 0; i < squad.members.Count; i++)
            AddUnit(squad.members[i]);

        SquadFormation.FormationLeader = squad.SquadFormation.FormationLeader;
        targetBuilding = squad.targetBuilding;
        SquadTarget = squad.SquadTarget;

        if (squad.SquadAttack)
        {
            SquadAttack = true;
            SquadTarget.OnDeadEvent -= squad.StopAttack;
            SquadTarget.OnDeadEvent += StopAttack;
        }
        
        if (squad.SquadCapture && members.Count > 0)
        {
            targetBuilding.OnBuilduingCaptured.RemoveListener(squad.OnSquadCaptureTarget);
            targetBuilding.OnBuilduingCaptured.AddListener(OnSquadCaptureTarget);
            SquadCapture = true;
            if (CanCapture(targetBuilding))
                CanBreakFormation = true;
        }

        SquadRepair = squad.SquadRepair;
    }

    public Unit GetSquadLeader()
    {
        if (SquadFormation.FormationLeader == null)
            SquadFormation.UpdateFormationLeader();
        
        return SquadFormation.FormationLeader;
    }
    
    public void MoveSquad(Vector3 targetPos)
    {
        SquadFormation.CreateFormation(targetPos);
    }

    public void AddUnit(Unit unit)
    {
        unit.SetMode(SquadMode);
        members.Add(unit);
        if (unit.squad != null && unit.squad.isTemporary)
            unit.squad.RemoveUnit(unit);
        unit.squad = this;
        totalCost += unit.Cost;
        //assign first unit to be the leader
        SquadFormation.UpdateFormationLeader();
    }
    
    /*
     * Clear the current squad members and add the new units in the squad
     */
    public void AddUnits(List<Unit> units)
    {
        members.Clear();
        foreach(Unit unit in units)
            AddUnit(unit);
    }

    public void ClearUnits()
    {
        while (members.Count > 0)
        {
            RemoveUnit(members[0]);
        }

        totalCost = 0;
        ResetTask();
    }

    public void RemoveUnit(Unit unit)
    {
        if (!members.Remove(unit)) 
            return;

        totalCost -= unit.Cost;

        if (unit.squad == this)
            unit.squad = null;

        SquadFormation.UpdateFormationLeader();
    }

    public void UpdateSquad()
    {
        if (SquadCapture)
            CaptureTarget();

        if (SquadAttack && SquadTarget)
            AttackTarget();
        
        if(SquadRepair && SquadTarget)
            SquadStartRepair();
    }
    
    /*
     * Set target pos of NavMesh Agent of units when CanBreakFormation = true
     */
    public void MoveUnitToPosition()
    {
        if(State !=E_TASK_STATE.Busy)
            State = E_TASK_STATE.Ongoing;
        
        SetSquadSpeed();
        foreach (Unit unit in members)
        {
            unit.CurrentMoveSpeed = MoveSpeed;
            unit.SetTargetPos(unit.GridPosition);
        }
    }

    public int GetSquadValue()
    {
        return totalCost;
    }

    public BaseEntity FirstEntityInRange()
    {
        foreach (Unit unit in members)
            if (unit.entityInRange != null)
                return unit.entityInRange;

        return null;
    }

    /*
     * The move speed of the squad is the lowest move speed within the squad members
     */
    void SetSquadSpeed()
    {
        foreach (Unit unit in members)
        {
            MoveSpeed = Mathf.Min(MoveSpeed, unit.GetUnitData.Speed);
        }
    }
    
    /*
     * Capture task 
     */
    public void CaptureTask(TargetBuilding target)
    {
        if (target == null || target == targetBuilding|| members.Count == 0)
            return;
        
        if (target.GetTeam() != Controller.GetTeam())
        {
            targetBuilding = target;
            SquadNeedToCapture();

            State = E_TASK_STATE.Busy;
            target.OnBuilduingCaptured.AddListener(OnSquadCaptureTarget);
            SquadFormation.ChooseLeader(target.transform.position);
            SquadCapture = true;

            CanBreakFormation = true;
            CaptureTarget();
            MoveSquad(target.transform.position);
        }
    }

    public bool IsNotCapturing(TargetBuilding target)
    {
        return target != targetBuilding;
    }

    void SquadNeedToCapture()
    {
        foreach (Unit unit in members)
        {
            unit.NeedToCapture(targetBuilding);
        }
    }

    void CaptureTarget()
    {
        foreach (Unit unit in members)
        {
            if (unit.needToCapture && unit.CanCapture(targetBuilding))
            {
                unit.StartCapture(targetBuilding);
                unit.StopMovement();
            }
        }
    }

    public void StopCapture()
    {
        SquadCapture = false;
        foreach (Unit unit in members)
        {
            unit.StopCapture();
        }
    }

    private bool CanCapture(TargetBuilding target)
    {
        if (target == null || (target.transform.position - SquadFormation.FormationLeader.gameObject.transform.position).sqrMagnitude > SquadFormation.FormationLeader.GetUnitData.CaptureDistanceMax * SquadFormation.FormationLeader.GetUnitData.CaptureDistanceMax)
            return false;

        return true;
    }

    public void StopSquadMovement()
    {
        foreach (Unit unit in members)
        {
            unit.StopMovement();
        }
    }

    public void AttackTask(BaseEntity target)
    {
        State = E_TASK_STATE.Busy;
        SetMode(E_MODE.Agressive);
        SquadTarget = target;
        SquadAttack = true;
        SetSquadTarget();
        SquadTarget.OnDeadEvent += StopAttack;
    }

    public void SwitchFormation(E_FORMATION_TYPE newFormationType)
    {
        if (SquadFormation == null || members.Count == 0)
            return;
        
        if(SquadFormation.FormationLeader == null)
            SquadFormation.UpdateFormationLeader();

        SquadFormation.SetFormationType = newFormationType;
        SquadFormation.CreateFormation(SquadFormation.FormationLeader.transform.position);
    }

    private void OnSquadCaptureTarget()
    {
        SquadCapture = false;
        State = E_TASK_STATE.Free;
    
        if(targetBuilding != null)
            targetBuilding.OnBuilduingCaptured.RemoveAllListeners();
    }

    public void SetMode(E_MODE newMode)
    {
        SquadMode = newMode;
        foreach(Unit unit in members)
        {
            unit.SetMode(SquadMode);
        }
    }

    public void StopAttack()
    {
        SquadTarget = null;
        SetSquadTarget();
        SquadAttack = false;
        State = E_TASK_STATE.Free;
        StopSquadMovement();
        CanBreakFormation = false;
    }

    private void SetSquadTarget()
    {
        foreach (Unit unit in members)
        {
            unit.EntityTarget = SquadTarget;
            unit.SetAttackTarget(SquadTarget);
        }
    }

    void AttackTarget()
    {
        if (SquadFormation.FormationLeader.CanAttack(SquadTarget))
        {
            foreach (Unit unit in members)
            {
                if (!SquadTarget)
                    continue;

                if (unit.CanAttack(SquadTarget))
                {
                    unit.ComputeAttack(SquadTarget);
                    unit.StopMovement(); 
                }
                else
                {
                    unit.SetTargetPos(SquadTarget.transform.position);
                    unit.EntityTarget = SquadTarget;
                }
            }   
        }
        else
            MoveSquad(SquadTarget.transform.position);
    }

    /*
     * Reset Squad task on new order
     */
    public void ResetTask()
    {
        SquadTarget = null;
        targetBuilding = null;
        StopCapture();
        SetSquadTarget();
        SquadStopRepair();
        SquadCapture = false;
        SquadAttack = false;
        SquadRepair = false;
        State = E_TASK_STATE.Free;
        CanBreakFormation = false;
        StopSquadMovement();
        if (isTemporary)
        {
            foreach (Unit unit in members)
            {
                RemoveUnit(unit);
            }
            Controller.TemporarySquadList.Remove(this);
        } 
    }

    #region RepairTask

    public void StartRepairTask(BaseEntity target)
    {
        ResetTask();
        
        State = E_TASK_STATE.Busy;
        SquadTarget = target;
        SquadRepair = true;
        CanBreakFormation = true;
        
        MoveSquad(target.transform.position);
    }
      
    private void SquadStartRepair()
    {
        if (!SquadTarget.NeedsRepairing())
        {
            ResetTask();
            return;
        }
        
        foreach (Unit unit in members)
        {
            if (unit.Equals(SquadTarget))
                continue;

            if (unit.IsAtDestination() && unit.CanRepair(SquadTarget) && !unit.IsRepairing)
            {
                unit.IsRepairing = true;
                unit.SetRepairTarget(SquadTarget);
            }
        }
    }

    private void SquadStopRepair()
    {
        foreach (Unit unit in members)
            unit.IsRepairing = false;
    }
    
    #endregion
}
