using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
public sealed class PlayerController : UnitController
{
    public enum InputMode
    {
        Orders,
        Positioning,
    }

    [SerializeField]
    GameObject TargetCursorPrefab = null;
    [SerializeField]
    float TargetCursorFloorOffset = 0.2f;
    [SerializeField]
    EventSystem SceneEventSystem = null;

    [SerializeField, Range(0f, 1f)]
    float PreviewTransparency = 0.3f;

    PointerEventData MenuPointerEventData = null;

    // Build Menu UI
    MenuController PlayerMenuController;

    // Camera
    TopCamera TopCameraRef = null;
    bool CanMoveCamera = false;
    Vector2 CameraInputPos = Vector2.zero;
    Vector2 CameraPrevInputPos = Vector2.zero;
    Vector2 CameraFrameMove = Vector2.zero;

    // Selection
    Vector3 SelectionStart = Vector3.zero;
    Vector3 SelectionEnd = Vector3.zero;
    bool SelectionStarted = false;
    float SelectionBoxHeight = 50f;
    LineRenderer SelectionLineRenderer;
    GameObject TargetCursor = null;

    // Factory build
    InputMode CurrentInputMode = InputMode.Orders;
    int WantedFactoryId = 0;
    GameObject WantedPreview = null;
    bool isFactoryWanted = false;
    [SerializeField]
    Shader PreviewShader = null;

    // Mouse events
    Action OnMouseLeftPressed = null;
    Action OnMouseLeft = null;
    Action OnMouseLeftReleased = null;
    Action OnUnitActionStart = null;
    Action OnUnitActionEnd = null;
    Action OnCameraDragMoveStart = null;
    Action OnCameraDragMoveEnd = null;

    Action<Vector3> OnFactoryPositioned = null;
    Action<Vector3> OnTurretPositionned = null;
    Action<float> OnCameraZoom = null;
    Action<float> OnCameraMoveHorizontal = null;
    Action<float> OnCameraMoveVertical = null;

    // Keyboard events
    Action OnFocusBasePressed = null;
    Action OnCancelBuildPressed = null;
    Action OnDestroyEntityPressed = null;
    Action OnCancelPositioning = null;
    Action OnSelectAllPressed = null;
    Action [] OnCategoryPressed = new Action[9];

    GameObject GetTargetCursor()
    {
        if (TargetCursor == null)
        {
            TargetCursor = Instantiate(TargetCursorPrefab);
            TargetCursor.name = TargetCursor.name.Replace("(Clone)", "");
        }
        return TargetCursor;
    }
    void SetTargetCursorPosition(Vector3 pos)
    {
        SetTargetCursorVisible(true);
        pos.y += TargetCursorFloorOffset;
        GetTargetCursor().transform.position = pos;
    }
    void SetTargetCursorVisible(bool isVisible)
    {
        GetTargetCursor().SetActive(isVisible);
    }
    void SetCameraFocusOnMainFactory()
    {
        if (FactoryList.Count > 0)
            TopCameraRef.FocusEntity(FactoryList[0]);
    }
    void CancelCurrentBuild()
    {
        foreach (Factory factory in SelectedFactoryList)
            factory?.CancelCurrentBuild();
        PlayerMenuController.HideAllFactoryBuildQueue();
    }

    #region MonoBehaviour methods
    protected override void Awake()
    {
        base.Awake();

        PlayerMenuController = GetComponent<MenuController>();
        if (PlayerMenuController == null)
            Debug.LogWarning("could not find MenuController component !");

        OnBuildPointsUpdated += PlayerMenuController.UpdateBuildPointsUI;
        OnCaptureTarget += PlayerMenuController.UpdateCapturedTargetsUI;

        TopCameraRef = Camera.main.GetComponent<TopCamera>();
        SelectionLineRenderer = GetComponent<LineRenderer>();

        PlayerMenuController = GetComponent<MenuController>();
       
        if (SceneEventSystem == null)
        {
            Debug.LogWarning("EventSystem not assigned in PlayerController, searching in current scene...");
            SceneEventSystem = FindObjectOfType<EventSystem>();
        }
        // Set up the new Pointer Event
        MenuPointerEventData = new PointerEventData(SceneEventSystem);

        SelectedSquad = new Squad(this);
    }

    override protected void Start()
    {
        base.Start();

        // left click : selection
        OnMouseLeftPressed += StartSelection;
        OnMouseLeft += UpdateSelection;
        OnMouseLeftReleased += EndSelection;

        // right click : Unit actions (move / attack / capture ...)
        OnUnitActionEnd += ComputeUnitsAction;

        // Camera movement
        // middle click : camera movement
        OnCameraDragMoveStart += StartMoveCamera;
        OnCameraDragMoveEnd += StopMoveCamera;

        OnCameraZoom += TopCameraRef.Zoom;
        OnCameraMoveHorizontal += TopCameraRef.KeyboardMoveHorizontal;
        OnCameraMoveVertical += TopCameraRef.KeyboardMoveVertical;

        // Gameplay shortcuts
        OnFocusBasePressed += SetCameraFocusOnMainFactory;
        OnCancelBuildPressed += CancelCurrentBuild;

        OnCancelPositioning += ExitFactoryBuildMode;

        OnFactoryPositioned += (floorPos) =>
        {
            if (RequestFactoryBuild(WantedFactoryId, floorPos) != null)
            {
                ExitFactoryBuildMode();
            }
        };
        OnTurretPositionned += (floorPos) =>
        {
            if (RequestTurretBuild(floorPos) != null)
            {
                ExitFactoryBuildMode();
            }
        };

        // Destroy selected unit command
        OnDestroyEntityPressed += () =>
        {
            Unit[] unitsToBeDestroyed = SelectedUnitList.ToArray();
            foreach (Unit unit in unitsToBeDestroyed)
            {
                (unit as IDamageable).Destroy();
            }

            foreach (Factory factory in SelectedFactoryList)
            {
                Factory factoryRef = factory;
                UnselectCurrentFactory();
                factoryRef.Destroy();
            }
        };

        // Selection shortcuts
        OnSelectAllPressed += SelectAllUnits;

        for(int i = 0; i < OnCategoryPressed.Length; i++)
        {
            // store typeId value for event closure
            int typeId = i;
            OnCategoryPressed[i] += () =>
            {
                SelectAllUnitsByTypeId(typeId);
            };
        }
        PlayerMenuController.SetSquadText(SelectedSquad.SquadMode);
    }
    override protected void Update()
    {
        switch (CurrentInputMode)
        {
            case InputMode.Positioning:
                UpdatePositioningInput();
                break;
            case InputMode.Orders:
                UpdateSelectionInput();
                UpdateActionInput();
                break;
        }

        CreateSelectedSquad();
        UpdateCameraInput();

        // Apply camera movement
        UpdateMoveCamera();

        if (SelectedSquad.members.Count > 0)
            SelectedSquad.UpdateSquad();

        foreach (KeyValuePair<int, Squad> squad in Squads)
            squad.Value.UpdateSquad();

        for (int i = 0; i < TemporarySquadList.Count;)
        {
            if (TemporarySquadList[i].members.Count == 0)
                TemporarySquadList[i].ResetTask();
            else
            {
                TemporarySquadList[i].UpdateSquad();
                i++;
            }
        }
    }
    #endregion

    #region Update methods
    void UpdatePositioningInput()
    {
        Vector3 floorPos = ProjectPreviewOnFloor();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            OnCancelPositioning?.Invoke();
        }
        if (Input.GetMouseButtonDown(0))
        {
            if (isFactoryWanted)
                OnFactoryPositioned?.Invoke(floorPos);
            else
                OnTurretPositionned?.Invoke(floorPos);
        }
    }
    void UpdateSelectionInput()
    {
        // Update keyboard inputs

        if (Input.GetKeyDown(KeyCode.A))
            OnSelectAllPressed?.Invoke();

        for (int i = 0; i < OnCategoryPressed.Length; i++)
        {
            if (Input.GetKeyDown(KeyCode.Keypad1 + i) || Input.GetKeyDown(KeyCode.Alpha7 + i))
            {
                OnCategoryPressed[i]?.Invoke();
                break;
            }
        }

        // Update mouse inputs
#if UNITY_EDITOR
        if (EditorWindow.focusedWindow != EditorWindow.mouseOverWindow)
            return;
#endif
        if (Input.GetMouseButtonDown(0))
            OnMouseLeftPressed?.Invoke();
        if (Input.GetMouseButton(0))
            OnMouseLeft?.Invoke();
        if (Input.GetMouseButtonUp(0))
            OnMouseLeftReleased?.Invoke();

    }
    void UpdateActionInput()
    {
        if (Input.GetKeyDown(KeyCode.Delete))
            OnDestroyEntityPressed?.Invoke();

        // cancel build
        if (Input.GetKeyDown(KeyCode.C))
            OnCancelBuildPressed?.Invoke();

        // Contextual unit actions (attack / capture ...)
        if (Input.GetMouseButtonDown(1))
            OnUnitActionStart?.Invoke();
        if (Input.GetMouseButtonUp(1))
            OnUnitActionEnd?.Invoke();
    }
    void UpdateCameraInput()
    {
        // Camera focus

        if (Input.GetKeyDown(KeyCode.F))
            OnFocusBasePressed?.Invoke();

        // Camera movement inputs

        // keyboard move (arrows)
        float hValue = Input.GetAxis("Horizontal");
        if (hValue != 0)
            OnCameraMoveHorizontal?.Invoke(hValue);
        float vValue = Input.GetAxis("Vertical");
        if (vValue != 0)
            OnCameraMoveVertical?.Invoke(vValue);

        // zoom in / out (ScrollWheel)
        float scrollValue = Input.GetAxis("Mouse ScrollWheel");
        if (scrollValue != 0)
            OnCameraZoom?.Invoke(scrollValue);

        // drag move (mouse button)
        if (Input.GetMouseButtonDown(2))
            OnCameraDragMoveStart?.Invoke();
        if (Input.GetMouseButtonUp(2))
            OnCameraDragMoveEnd?.Invoke();
    }
    #endregion

    #region Unit selection methods
    void StartSelection()
    {
        // Hide target cursor
        SetTargetCursorVisible(false);

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        int factoryMask = 1 << LayerMask.NameToLayer("Factory");
        int unitMask = 1 << LayerMask.NameToLayer("Unit");
        int floorMask = 1 << LayerMask.NameToLayer("Floor");

        // *** Ignore Unit selection when clicking on UI ***
        // Set the Pointer Event Position to that of the mouse position
        MenuPointerEventData.position = Input.mousePosition;

        //Create a list of Raycast Results
        List<RaycastResult> buildResults = new List<RaycastResult>();
        List<RaycastResult> squadResults = new List<RaycastResult>();
        List<RaycastResult> resourcesResults = new List<RaycastResult>();
        PlayerMenuController.BuildMenuRaycaster.Raycast(MenuPointerEventData, buildResults);
        PlayerMenuController.SquadMenuRaycaster.Raycast(MenuPointerEventData, squadResults);
        PlayerMenuController.ProduceResourcesMenuRaycaster.Raycast(MenuPointerEventData, resourcesResults);
        if (buildResults.Count > 0 || squadResults.Count > 0 || resourcesResults.Count > 0)
            return;

        RaycastHit raycastInfo;
        // factory selection
        if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, factoryMask))
        {
            Factory factory = raycastInfo.transform.GetComponent<Factory>();
            if (factory != null)
            {
                if (factory.GetTeam() == Team && !SelectedFactoryList.Contains(factory))
                {
                    UnselectCurrentFactory();
                    SelectFactory(factory);
                }
            }
        }
        // unit selection / unselection
        else if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, unitMask))
        {
            bool isShiftBtPressed = Input.GetKey(KeyCode.J);
            bool isCtrlBtPressed = Input.GetKey(KeyCode.H);

            UnselectCurrentFactory();
            UnselectTarget();

            Unit selectedUnit = raycastInfo.transform.GetComponent<Unit>();
            bool hasSelectedEnemy = false;
            if (selectedUnit != null && selectedUnit.GetTeam() == Team)
            {
                if (isShiftBtPressed)
                {
                    UnselectUnit(selectedUnit);
                }
                else if (isCtrlBtPressed)
                {
                    SelectUnit(selectedUnit);
                    SelectedSquad.AddUnit(selectedUnit);
                    hasSelectedEnemy = true;
                }
                else
                {
                    UnselectAllUnits();
                    SelectUnit(selectedUnit);
                    SelectedSquad.AddUnit(selectedUnit);
                    hasSelectedEnemy = true;
                }
            }
            if (hasSelectedEnemy)
                PlayerMenuController.UpdateSquadMenu(SelectedSquad);
        }
        else if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, floorMask))
        {
            UnselectCurrentFactory();
            UnselectTarget();

            SelectionLineRenderer.enabled = true;
            SelectionStarted = true;
            SelectionStart.x = raycastInfo.point.x;
            SelectionStart.y = 0.0f;//raycastInfo.point.y + 1f;
            SelectionStart.z = raycastInfo.point.z;

        }
    }

    /*
     * Multi selection methods
     */
    void UpdateSelection()
    {
        if (SelectionStarted == false)
            return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int floorMask = 1 << LayerMask.NameToLayer("Floor");

        RaycastHit raycastInfo;
        if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, floorMask))
        {
            SelectionEnd = raycastInfo.point;
        }

        SelectionLineRenderer.SetPosition(0, new Vector3(SelectionStart.x, SelectionStart.y, SelectionStart.z));
        SelectionLineRenderer.SetPosition(1, new Vector3(SelectionStart.x, SelectionStart.y, SelectionEnd.z));
        SelectionLineRenderer.SetPosition(2, new Vector3(SelectionEnd.x, SelectionStart.y, SelectionEnd.z));
        SelectionLineRenderer.SetPosition(3, new Vector3(SelectionEnd.x, SelectionStart.y, SelectionStart.z));
    }
    void EndSelection()
    {
        if (SelectionStarted == false)
            return;

        UpdateSelection();
        SelectionLineRenderer.enabled = false;
        Vector3 center = (SelectionStart + SelectionEnd) / 2f;
        Vector3 size = Vector3.up * SelectionBoxHeight + SelectionEnd - SelectionStart;
        size.x = Mathf.Abs(size.x);
        size.y = Mathf.Abs(size.y);
        size.z = Mathf.Abs(size.z);

        UnselectAllUnits();
        UnselectCurrentFactory();
        UnselectTarget();

        int unitLayerMask = 1 << LayerMask.NameToLayer("Unit");
        int factoryLayerMask = 1 << LayerMask.NameToLayer("Factory");
        int targetLayerMask = 1 << LayerMask.NameToLayer("Target");
        Collider[] colliders = Physics.OverlapBox(center, size / 2f, Quaternion.identity, unitLayerMask | factoryLayerMask | targetLayerMask, QueryTriggerInteraction.Ignore);
        bool hasSelectedEnemy = false;
        foreach (Collider col in colliders)
        {
            //Debug.Log("collider name = " + col.gameObject.name);
            ISelectable selectedEntity = col.transform.GetComponent<ISelectable>();
            if (selectedEntity.GetTeam() == GetTeam())
            {
                if (selectedEntity is Unit)
                {
                    SelectUnit((selectedEntity as Unit));
                    SelectedSquad.AddUnit(selectedEntity as Unit);
                    hasSelectedEnemy = true;
                }
                else if (selectedEntity is Factory)
                {
                    SelectFactory(selectedEntity as Factory);
                }
                else if (selectedEntity is TargetBuilding)
                {
                    SelectTarget(selectedEntity as TargetBuilding);
                }
            }
        }

        if (hasSelectedEnemy)
            PlayerMenuController.UpdateSquadMenu(SelectedSquad);

        SelectionStarted = false;
        SelectionStart = Vector3.zero;
        SelectionEnd = Vector3.zero;
    }

    protected override void UnselectAllUnits()
    {
        base.UnselectAllUnits();

        if (SelectedSquad.State == E_TASK_STATE.Busy)
            TemporarySquadList.Add(new Squad(SelectedSquad));

        SelectedSquad.ClearUnits();
        PlayerMenuController.HideSquadMenu();
    }

    #endregion

    #region Squad creation methods

    /*
     * create a squad with selected unit
     * or select a squad with alpha numeric
     */
    public void CreateSelectedSquad()
    {
        int index = 0;

        //TODO better way of doing this ?
        if (Input.GetKeyDown(KeyCode.Alpha1))
            index = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            index = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            index = 3;

        if(Input.GetKeyDown(KeyCode.K))
            SelectedSquad?.SwitchFormation(E_FORMATION_TYPE.Circle);
        else if(Input.GetKeyDown(KeyCode.L))
            SelectedSquad?.SwitchFormation(E_FORMATION_TYPE.Line);

        //No selection use
        if (index == 0) 
            return;
        
        if (Squads.Count == 0 || !Squads.ContainsKey(index))
            CreateSquad(index);
        else
        {
            UnselectAllUnits();
            SelectedSquad.members = GetSquad(index).members;
            //unit selection UI only
            SelectedUnitList.AddRange(SelectedSquad.members);
            foreach (Unit unit in SelectedUnitList)
            {
                unit.SetSelected(true);
            }
        }
        
    }

    #endregion

    #region Factory / build methods
    public void UpdateFactoryBuildQueueUI(Factory factory, int entityIndex)
    {
        PlayerMenuController.UpdateFactoryBuildQueueUI(entityIndex, factory);
    }
    public override void SelectFactory(Factory factory)
    {
        if (factory == null || factory.IsUnderConstruction
        || (SelectedFactoryList.Count > 0 && SelectedFactoryList[0].GetFactoryData.TypeId != factory.GetFactoryData.TypeId))
            return;

        base.SelectFactory(factory);

        PlayerMenuController.UpdateFactoryMenu(factory, SelectedFactoryList.Count, RequestUnitBuild, EnterFactoryBuildMode, EnterTurretBuildMode);
    }
    public override void UnselectCurrentFactory()
    {
        //Debug.Log("UnselectCurrentFactory");

        foreach (Factory factory in SelectedFactoryList)
        {
            PlayerMenuController.UnregisterBuildButtons(factory.AvailableUnitsCount, factory.AvailableFactoriesCount);
        }

        PlayerMenuController.HideFactoryMenu();

        base.UnselectCurrentFactory();
    }
    public override void SelectTarget(TargetBuilding target)
    {
        if (target == null || target.GetTeam() == ETeam.Neutral || !target.canProduceResources)
            return;

        base.SelectTarget(target);
        PlayerMenuController.UpdateProduceResourcesMenu(target);
    }
    public override void UnselectTarget()
    {
        PlayerMenuController.HideProduceResourcesMenu();
        base.UnselectTarget();
    }
    void EnterFactoryBuildMode(int factoryId)
    {
        if (SelectedFactoryList[0].GetFactoryCost(factoryId) > TotalBuildPoints)
            return;

        CurrentInputMode = InputMode.Positioning;

        WantedFactoryId = factoryId;

        // Create factory preview

        // Load factory prefab for preview
        GameObject factoryPrefab = SelectedFactoryList[0].GetFactoryPrefab(factoryId);
        if (factoryPrefab == null)
        {
            Debug.LogWarning("Invalid factory prefab for factoryId " + factoryId);
        }
        isFactoryWanted = true;
        WantedPreview = Instantiate(factoryPrefab.transform.GetChild(0).gameObject); // Quick and dirty access to mesh GameObject
        WantedPreview.name = WantedPreview.name.Replace("(Clone)", "_Preview");
        // Set transparency on materials
        foreach (Renderer rend in WantedPreview.GetComponentsInChildren<MeshRenderer>())
        {
            Material mat = rend.material;
            mat.shader = PreviewShader;
            Color col = mat.color;
            col.a = PreviewTransparency;
            mat.color = col;
        }

        // Project mouse position on ground to position factory preview
        ProjectPreviewOnFloor();
    }
    void EnterTurretBuildMode()
    {
        if (Turret.cost > TotalBuildPoints)
            return;

        CurrentInputMode = InputMode.Positioning;

        // Create turret preview

        // Load turret prefab for preview
        GameObject turretPrefab = SelectedFactoryList[0].TurretPrefab;
        if (turretPrefab == null)
        {
            Debug.LogWarning("Invalid turret prefab for turret");
        }
        isFactoryWanted = false;
        WantedPreview = Instantiate(turretPrefab.transform.GetChild(0).gameObject); // Quick and dirty access to mesh GameObject
        WantedPreview.name = WantedPreview.name.Replace("(Clone)", "_Preview");
        // Set transparency on materials
        foreach (Renderer rend in WantedPreview.GetComponentsInChildren<MeshRenderer>())
        {
            Material mat = rend.material;
            mat.shader = PreviewShader;
            Color col = mat.color;
            col.a = PreviewTransparency;
            mat.color = col;
        }

        // Project mouse position on ground to position factory preview
        ProjectPreviewOnFloor();
    }
    void ExitFactoryBuildMode()
    {
        CurrentInputMode = InputMode.Orders;
        Destroy(WantedPreview);
    }
    Vector3 ProjectPreviewOnFloor()
    {
        if (CurrentInputMode == InputMode.Orders)
        {
            Debug.LogWarning("Wrong call to ProjectFactoryPreviewOnFloor : CurrentInputMode = " + CurrentInputMode.ToString());
            return Vector3.zero;
        }

        Vector3 floorPos = Vector3.zero;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int floorMask = 1 << LayerMask.NameToLayer("Floor");
        RaycastHit raycastInfo;
        if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, floorMask))
        {
            floorPos = raycastInfo.point;
            WantedPreview.transform.position = floorPos;
        }
        return floorPos;
    }
    #endregion

    #region Entity targetting (attack / capture) and movement methods
    void ComputeUnitsAction()
    {
        if (SelectedSquad == null)
            return;

        int damageableMask = (1 << LayerMask.NameToLayer("Unit")) | (1 << LayerMask.NameToLayer("Factory"));
        int targetMask = 1 << LayerMask.NameToLayer("Target");
        int floorMask = 1 << LayerMask.NameToLayer("Floor");
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit raycastInfo;

        // Set unit / factory attack target
        if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, damageableMask))
        {
            BaseEntity other = raycastInfo.transform.GetComponent<BaseEntity>();
            if (other != null)
            {
                if (other.GetTeam() != GetTeam())
                    SelectedSquad.AttackTask(other);
                else if (other.NeedsRepairing())
                {
                    // Direct call to reparing task $$$ to be improved by AI behaviour
                    //TODO Repair taks
                    foreach (Unit unit in SelectedUnitList)
                    {
                        unit.SetRepairTarget(other);
                        unit.needToCapture = false;
                    }
                }
            }
        }
        // Set capturing target
        else if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, targetMask))
        {
            TargetBuilding target = raycastInfo.transform.GetComponent<TargetBuilding>();
            if (target != null && target.GetTeam() != GetTeam())
            {
                // Direct call to capturing task $$$ to be improved by AI behaviour
                // foreach (Unit unit in SelectedUnitList)
                //     unit.SetCaptureTarget(target);
                if (SelectedSquad.IsNotCapturing(target))
                {
                    SelectedSquad.ResetTask();
                    SelectedSquad.CaptureTask(target);
                }
            }
        }
        // Set unit move target
        else if (Physics.Raycast(ray, out raycastInfo, Mathf.Infinity, floorMask))
        {
            Vector3 newPos = raycastInfo.point;
            SetTargetCursorPosition(newPos);

            // Direct call to moving task $$$ to be improved by AI behaviour
            // foreach (Unit unit in SelectedUnitList)
            //     unit.SetTargetPos(newPos);
            foreach (Unit unit in SelectedUnitList)
                unit.needToCapture = false;

            SelectedSquad.ResetTask();
            SelectedSquad.MoveSquad(newPos);
        }
    }

    public void SetSquadMode(int mode)
    {
        if (SelectedSquad != null)
        {
            SelectedSquad.SetMode((E_MODE)mode);
            PlayerMenuController.SetSquadText((E_MODE)mode);
        }
    }
    #endregion

    #region Camera methods
    void StartMoveCamera()
    {
        CanMoveCamera = true;
        CameraInputPos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
        CameraPrevInputPos = CameraInputPos;
    }
    void StopMoveCamera()
    {
        CanMoveCamera = false;
    }
    void UpdateMoveCamera()
    {
        if (CanMoveCamera)
        {
            CameraInputPos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            CameraFrameMove = CameraPrevInputPos - CameraInputPos;
            TopCameraRef.MouseMove(CameraFrameMove);
            CameraPrevInputPos = CameraInputPos;
        }
    }
    #endregion
}
