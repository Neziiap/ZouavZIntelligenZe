using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
public class TargetBuilding : MonoBehaviour, ISelectable
{
    [SerializeField]
    float CaptureGaugeStart = 100f;
    [SerializeField]
    float CaptureGaugeSpeed = 1f;
    [SerializeField]
    float upgradeDuration = 20f;
    [SerializeField]
    int BuildPoints = 5;
    [SerializeField]
    int upgradeCost = 8;
    [SerializeField]
    Material BlueTeamMaterial = null;
    [SerializeField]
    Material RedTeamMaterial = null;
    
    Material NeutralMaterial = null;
    MeshRenderer BuildingMeshRenderer = null;
    Image GaugeImage;
    int[] TeamScore;
    float CaptureGaugeValue;
    float currentUpgradeDuration = 0f;
    ETeam OwningTeam = ETeam.Neutral;
    public ETeam CapturingTeam = ETeam.Neutral;
    UnitController owningController = null;
    GameObject SelectedSprite = null;

    public ETeam GetTeam() { return OwningTeam; }
    public int influence = 1;
    public UnityEvent OnBuilduingCaptured;
    public bool canProduceResources = false;
    public bool isProducingResources = false;
    public bool isUpgrading = false;

    #region MonoBehaviour methods
    void Start()
    {
        BuildingMeshRenderer = GetComponentInChildren<MeshRenderer>();
        NeutralMaterial = BuildingMeshRenderer.material;

        GaugeImage = GetComponentInChildren<Image>();
        if (GaugeImage)
            GaugeImage.fillAmount = 0f;
        CaptureGaugeValue = CaptureGaugeStart;
        TeamScore = new int[2];
        TeamScore[0] = 0;
        TeamScore[1] = 0;
        OnBuilduingCaptured = new UnityEvent();
    }
    void Update()
    {
        if (CapturingTeam != OwningTeam && CapturingTeam != ETeam.Neutral)
        {
            CaptureGaugeValue -= TeamScore[(int)CapturingTeam] * CaptureGaugeSpeed * Time.deltaTime;

            GaugeImage.fillAmount = 1f - CaptureGaugeValue / CaptureGaugeStart;

            if (CaptureGaugeValue <= 0f)
            {
                CaptureGaugeValue = 0f;
                OnCaptured(CapturingTeam);
            }
        }

        if (currentUpgradeDuration > 0f)
        {
            currentUpgradeDuration -= Time.deltaTime;
            GaugeImage.fillAmount = 1f - currentUpgradeDuration / upgradeDuration;

            if (currentUpgradeDuration <= 0f)
                StartProducingResources();
        }
    }
    #endregion

    private void Awake()
    {
        SelectedSprite = transform.Find("SelectedSprite")?.gameObject;
        SelectedSprite?.SetActive(false);
    }

    #region Capture methods
    public void StartCapture(Unit unit)
    {
        if (unit == null)
            return;

        TeamScore[(int)unit.GetTeam()] += unit.Cost;

        if (isUpgrading)
        {
            currentUpgradeDuration = 0f;
            isUpgrading = false;
            CaptureGaugeValue = CaptureGaugeStart;
            GaugeImage.fillAmount = 0f;
        }
        if (CapturingTeam == ETeam.Neutral)
        {
            CapturingTeam = unit.GetTeam();
            GaugeImage.color = GameServices.GetTeamColor(CapturingTeam);
        }
        else if (CapturingTeam != unit.GetTeam())
        {
            if (TeamScore[(int)GameServices.GetOpponent(unit.GetTeam())] > 0)
                ResetCapture();
        }
    }
    public void StopCapture(Unit unit)
    { 
        if (unit == null || (TeamScore[(int)unit.GetTeam()] == 0 && CapturingTeam == ETeam.Neutral))
            return;

        TeamScore[(int)unit.GetTeam()] -= unit.Cost;
        if (TeamScore[(int)unit.GetTeam()] == 0)
        {
            ETeam opponentTeam = GameServices.GetOpponent(unit.GetTeam());
            if (TeamScore[(int)opponentTeam] == 0)
            {
                ResetCapture();
            }
            else
            {
                CapturingTeam = opponentTeam;
                GaugeImage.color = GameServices.GetTeamColor(CapturingTeam);
            }
        }
    }
    void ResetCapture()
    {
        TeamScore[(int)(CapturingTeam)] = 0;
        CaptureGaugeValue = CaptureGaugeStart;
        CapturingTeam = ETeam.Neutral;
        GaugeImage.fillAmount = 0f;
    }
    void OnCaptured(ETeam newTeam)
    {
        OnBuilduingCaptured.Invoke();
        //Debug.Log("target captured by " + newTeam.ToString());
        if (OwningTeam != newTeam)
        {
            UnitController teamController = GameServices.GetControllerByTeam(newTeam);
            if (teamController != null)
            {
                teamController.CaptureTarget(BuildPoints, this);
                owningController = teamController;
            }

            if (OwningTeam != ETeam.Neutral)
            {
                // remove points to previously owning team
                teamController = GameServices.GetControllerByTeam(OwningTeam);
                if (teamController != null)
                {
                    teamController.LoseTarget(BuildPoints, this);
                }
                Map.Instance.RemoveTargetBuilding(this);
            }
        }

        ResetCapture();
        OwningTeam = newTeam;
        BuildingMeshRenderer.material = newTeam == ETeam.Blue ? BlueTeamMaterial : RedTeamMaterial;
        Map.Instance.AddTargetBuilding(this, newTeam);
        isProducingResources = false;
    }

    public bool CanBeUpgraded()
    {
        UnitController teamController = GameServices.GetControllerByTeam(OwningTeam);
        if (!canProduceResources || isProducingResources || isUpgrading || teamController.TotalBuildPoints < upgradeCost || (CapturingTeam != OwningTeam && CapturingTeam != ETeam.Neutral))
            return false;

        return true;
    }

    public void StartUpgrade()
    {
        if (!CanBeUpgraded())
            return;

        GameServices.GetControllerByTeam(OwningTeam).TotalBuildPoints -= upgradeCost;
        currentUpgradeDuration = upgradeDuration;
        isUpgrading = true;
    }

    private void StartProducingResources()
    {
        transform.localScale = new Vector3(1.7f, 2.5f, 1.7f);
        isProducingResources = true;
        isUpgrading = false;
        InvokeRepeating("ProduceResources", 5f, 5f);
        GaugeImage.fillAmount = 0f;
        Map.Instance.GetTile(transform.position).buildType = E_BUILDTYPE.MINER;
    }

    private void ProduceResources()
    {
        owningController.TotalBuildPoints++;
    }

    public void SetSelected(bool selected)
    {
        SelectedSprite?.SetActive(selected);
    }
    #endregion
}
