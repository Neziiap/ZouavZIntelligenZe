using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Task_Data", menuName = "RTS/TaskData", order = 2)]
public class TaskData : ScriptableObject
{
    public float id;
    public AnimationCurve Time;
    public AnimationCurve ResourcePerMinute;
    public AnimationCurve Resources;
    public AnimationCurve MilitaryPower;
    public AnimationCurve EnemyPower;
    public AnimationCurve Ratio;
    public AnimationCurve Distance;
}