using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Turret : BaseEntity
{
    static public int cost = 7;
    static public int influence = 1;

    [SerializeField]
    int damage = 40;
    [SerializeField]
    int maxHp = 350;
    [SerializeField]
    float buildDuration = 20f;
    [SerializeField]
    float range = 15f;
    [SerializeField]
    float focusRefreshRate = 0.5f;
    [SerializeField]
    float attackSpeed = 1f;
    [SerializeField]
    GameObject bulletPrefab = null;
    [SerializeField]
    Transform bulletSlot;

    float currentDuration = 0f;

    float cooldown = 0f;
    public bool isUnderConstruction = false;
    Unit currentFocus = null;
    Image BuildGaugeImage;
    GameObject toRotate;

    protected override void Awake()
    {
        base.Awake();
        BuildGaugeImage = transform.Find("Canvas/BuildProgressImage").GetComponent<Image>();
        if (BuildGaugeImage)
        {
            BuildGaugeImage.fillAmount = 0f;
            BuildGaugeImage.color = GameServices.GetTeamColor(GetTeam());
        }
    }

    // Start is called before the first frame update
    override protected void Start()
    {
        base.Start();
        currentDuration = buildDuration;
        toRotate = transform.Find("ToRotate").gameObject;
        Map.Instance.AddTurret(this, Team);
    }

    override public void Init(ETeam _team)
    {
        base.Init(_team);
        isUnderConstruction = true;
        HP = maxHp;
        OnDeadEvent += Turret_OnDead;
    }

    // Update is called once per frame
    override protected void Update()
    {
        if (isUnderConstruction)
        {
            currentDuration -= Time.deltaTime;
            if (currentDuration <= 0f)
            {
                isUnderConstruction = false;
                InvokeRepeating("UpdateFocus", 0f, focusRefreshRate);
                BuildGaugeImage.fillAmount = 0f;
            }
            else if (BuildGaugeImage)
                BuildGaugeImage.fillAmount = 1f - currentDuration / buildDuration;
        }
        else
            ComputeAttack();
    }

    void UpdateFocus()
    {
        if (currentFocus == null || Vector3.Distance(currentFocus.transform.position, transform.position) > range)
        {
            Collider[] unitsCollider = Physics.OverlapSphere(transform.position, range, 1 << LayerMask.NameToLayer("Unit"));
            foreach (Collider unitCollider in unitsCollider)
            {
                Unit unit = unitCollider.GetComponent<Unit>();
                if (unit.GetTeam() != Team)
                {
                    currentFocus = unit;
                    return;
                }
            }
        }
    }

    void ComputeAttack()
    {
        if (cooldown > 0f)
            cooldown -= Time.deltaTime;

        if (cooldown <= 0f && currentFocus)
        {
            toRotate.transform.LookAt(currentFocus.transform.position);
            
            cooldown = attackSpeed;

            if (bulletPrefab)
            {
                GameObject newBullet = Instantiate(bulletPrefab, bulletSlot);
                newBullet.transform.parent = null;
                newBullet.GetComponent<Bullet>().ShootToward(currentFocus.transform.position - transform.position, Team);
            }

            currentFocus.AddDamage(damage, this);
        }
    }

    void Turret_OnDead()
    {
        Map.Instance.RemoveTurret(this);
        Destroy(gameObject);
    }
}
