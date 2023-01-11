using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;

public enum E_FORMATION_TYPE
{
    Circle,
    Square,
    Line,
    None,
    Custom
}

/*
 * Class that calculate the position of squad members base of the type of formation selected
 */
public class Formation
{
    private E_FORMATION_TYPE FormationType;

    private Squad Squad;

    private float GridDistance = 5.0f;

    public Unit FormationLeader = null;
    
    // number of second between updates
    private float updateDelay = 1f;

    public E_FORMATION_TYPE SetFormationType
    {
        set => FormationType = value;
    }

    public Formation(Squad _squad)
    {
        //For testing
        FormationType = E_FORMATION_TYPE.Line;
        Squad = _squad;
    }

    public Formation(Formation _formation, Squad _squad)
    {
        FormationType = _formation.FormationType;
        Squad = _squad;
        FormationLeader = _formation.FormationLeader;
    }

    public void UpdateFormationLeader()
    {
        if (Squad.members.Count != 0)
            FormationLeader = Squad.members[0];
    }

    public void CreateFormation(Vector3 targetPos)
    {
        if (Squad.members.Count == 0)
            return;
        
        if (!Squad.CanBreakFormation)
        {
            ChooseLeader(targetPos);
            switch (FormationType)
            {
                case E_FORMATION_TYPE.Circle:
                    CreateCircleFormation(targetPos);
                    break;
                case E_FORMATION_TYPE.Line:
                    CreateLineFormation(targetPos);
                    break;
                case E_FORMATION_TYPE.Custom:
                    break;
            }
        }
        else
        {
            //special cases when units don't move in formation
            foreach (Unit unit in Squad.members)
            {
                unit.GridPosition = targetPos;
            }
        }
        
        Squad.MoveUnitToPosition();
    }

    void CreateCircleFormation(Vector3 targetPos)
    {
        int numberOfSectors = Squad.members.Count;
        float radius = numberOfSectors * GridDistance / Mathf.PI;

        FormationLeader.GridPosition = targetPos;
        
        float rotY = FormationLeader.transform.eulerAngles.y;

        for (int i = 0; i < Squad.members.Count; i++)
        {
            float angle = i * 2 * Mathf.PI / numberOfSectors;
            Vector3 positionOffset = new Vector3(radius * Mathf.Sin(angle), 0, -radius + radius * Mathf.Cos(angle));
            Vector3 rotationOffset = Quaternion.Euler(0, rotY, 0) * positionOffset;
            
            //fix first unit of the squad so that it takes the empty slot
            if (FormationLeader.Equals(Squad.members[i]))
            {
                Squad.members[0].GridPosition = FormationLeader.GridPosition + rotationOffset;
                continue;
            }

            Squad.members[i].GridPosition = FormationLeader.GridPosition + rotationOffset;
        }
    }

    void CreateLineFormation(Vector3 targetPos)
    {
        const int unitsPerLine = 4;

        FormationLeader.GridPosition = targetPos;
        float half = unitsPerLine / 2f;
        int j = 1;
        for (int i = 0; i < Squad.members.Count; i++)
        {
            if(Squad.members[i] == FormationLeader)
                continue;
            
            int rowIndex = j / unitsPerLine;
            int columnIndex = j % unitsPerLine;
            //Debug.Log("LOG : " + i + " " + rowIndex + " " + columnIndex);
            Vector3 offset = new Vector3(columnIndex * GridDistance, 0f, rowIndex * GridDistance);

            Squad.members[i].GridPosition = FormationLeader.GridPosition + offset;
            j++;
        }
    }

    /*
     * Choose the leader when a move order is issue
     * The leader is the unit closest to the destination
     */
    public void ChooseLeader(Vector3 pos)
    {
        float distance;
        
        if(FormationLeader != null)
            distance = Vector3.Distance(FormationLeader.transform.position, pos);
        else
        {
            FormationLeader = Squad.members[0];
            distance = Vector3.Distance(FormationLeader.transform.position, pos);
        }

        foreach (Unit unit in Squad.members)
        {
            float newDistance = Vector3.Distance(unit.transform.position, pos);

            if (newDistance < distance)
            {
                distance = newDistance;
                FormationLeader = unit;
            }
        }
    }
}