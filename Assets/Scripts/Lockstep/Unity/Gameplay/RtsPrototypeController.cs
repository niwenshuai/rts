using System.Collections.Generic;
using AIRTS.Lockstep.Gameplay;
using AIRTS.Lockstep.Math;
using AIRTS.Lockstep.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AIRTS.Lockstep.Unity.Gameplay
{
    public sealed class RtsPrototypeController : MonoBehaviour
    {
        private enum PendingCommandMode
        {
            None,
            Move,
            Attack,
            BuildBarracks,
            BuildGuardTower
        }

        [SerializeField] private LockstepClientBehaviour lockstep;
        [SerializeField] private bool offlineTestMode = true;
        [SerializeField] private int offlineLocalPlayerId = 1;

        private readonly RtsSimulation _simulation = new RtsSimulation();
        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();
        private readonly List<int> _selectedIds = new List<int>();
        private readonly List<RtsEntity> _selectedUnitScratch = new List<RtsEntity>();
        private readonly List<int> _removeScratch = new List<int>();
        private readonly List<GameObject> _commandButtons = new List<GameObject>();

        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _commandPanel;
        private Text _resourceText;
        private Text _selectionText;
        private Image _dragBox;
        private Vector2 _dragStart;
        private bool _isDragging;
        private PendingCommandMode _pendingMode;
        private float _offlineAccumulator;
        private int _lastCommandNetworkFrame;
        private string _lastCommandSignature;

        private int LocalPlayerId
        {
            get
            {
                if (lockstep != null && lockstep.Client != null && lockstep.Client.PlayerId > 0)
                {
                    return lockstep.Client.PlayerId;
                }

                return offlineLocalPlayerId;
            }
        }

        private void Awake()
        {
            if (lockstep == null)
            {
                lockstep = FindObjectOfType<LockstepClientBehaviour>();
            }

            _camera = Camera.main;
            ConfigureCamera();
            CreateMapView();
            CreateUi();
            RefreshViews();
            RefreshUi();
        }

        private void Start()
        {
            if (lockstep != null)
            {
                lockstep.LogicFrameReady += OnLogicFrameReady;
                if (lockstep.Client != null)
                {
                    lockstep.Client.GameStarted += OnGameStarted;
                }
            }
        }

        private void Update()
        {
            HandleInput();
            TickOfflineMode();
            RefreshViews();
            RefreshUi();
        }

        private void OnDestroy()
        {
            if (lockstep != null)
            {
                lockstep.LogicFrameReady -= OnLogicFrameReady;
                if (lockstep.Client != null)
                {
                    lockstep.Client.GameStarted -= OnGameStarted;
                }
            }
        }

        private void OnGameStarted(int startFrame)
        {
            _simulation.Reset();
            _selectedIds.Clear();
            _pendingMode = PendingCommandMode.None;
            _lastCommandNetworkFrame = 0;
            _lastCommandSignature = null;
        }

        private void OnLogicFrameReady(LockstepFrame frame)
        {
            IReadOnlyList<PlayerCommand> commands = null;
            if (frame.FrameIndex != _lastCommandNetworkFrame)
            {
                commands = frame.Commands;
                _lastCommandNetworkFrame = frame.FrameIndex;
            }

            _simulation.TickFrame(lockstep != null ? lockstep.LogicFrame : _simulation.LogicFrame + 1, commands);
        }

        private void TickOfflineMode()
        {
            bool hasConnectedLockstep = lockstep != null && lockstep.Client != null && lockstep.Client.IsConnected;
            if (!offlineTestMode || hasConnectedLockstep)
            {
                return;
            }

            _offlineAccumulator += Time.deltaTime;
            const float interval = 1f / 30f;
            while (_offlineAccumulator >= interval)
            {
                _simulation.TickFrame(_simulation.LogicFrame + 1, null);
                _offlineAccumulator -= interval;
            }
        }

        private void HandleInput()
        {
            if (_camera == null)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                _dragStart = Input.mousePosition;
                _isDragging = true;
                SetDragBoxVisible(true);
            }

            if (_isDragging && Input.GetMouseButton(0))
            {
                UpdateDragBox(_dragStart, Input.mousePosition);
            }

            if (_isDragging && Input.GetMouseButtonUp(0))
            {
                Vector2 dragEnd = Input.mousePosition;
                _isDragging = false;
                SetDragBoxVisible(false);

                if ((dragEnd - _dragStart).sqrMagnitude > 100f)
                {
                    SelectUnitsInRect(_dragStart, dragEnd);
                }
                else
                {
                    HandleClick(dragEnd);
                }
            }

            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi())
            {
                HandleRightClick(Input.mousePosition);
            }
        }

        private void HandleClick(Vector2 screenPosition)
        {
            if (_pendingMode != PendingCommandMode.None)
            {
                ExecutePendingCommand(screenPosition);
                return;
            }

            RtsEntity clicked = FindEntityAtScreen(screenPosition, true);
            if (clicked != null && clicked.OwnerId == LocalPlayerId && clicked.IsSelectable)
            {
                _selectedIds.Clear();
                _selectedIds.Add(clicked.Id);
                return;
            }

            _selectedIds.Clear();
        }

        private void HandleRightClick(Vector2 screenPosition)
        {
            RtsEntity target = FindEntityAtScreen(screenPosition, true);
            if (target != null && target.OwnerId != 0 && target.OwnerId != LocalPlayerId)
            {
                IssueAttackCommand(target.Id);
                return;
            }

            if (TryGetMapPoint(screenPosition, out FixedVector3 point))
            {
                IssueMoveCommand(point);
            }
        }

        private void ExecutePendingCommand(Vector2 screenPosition)
        {
            PendingCommandMode mode = _pendingMode;
            _pendingMode = PendingCommandMode.None;

            if (mode == PendingCommandMode.Attack)
            {
                RtsEntity target = FindEntityAtScreen(screenPosition, true);
                if (target != null && target.OwnerId != 0 && target.OwnerId != LocalPlayerId)
                {
                    IssueAttackCommand(target.Id);
                }

                return;
            }

            if (!TryGetMapPoint(screenPosition, out FixedVector3 point))
            {
                return;
            }

            if (mode == PendingCommandMode.Move)
            {
                IssueMoveCommand(point);
            }
            else if (mode == PendingCommandMode.BuildBarracks)
            {
                IssueBuildCommand(RtsBuildingType.Barracks, point);
            }
            else if (mode == PendingCommandMode.BuildGuardTower)
            {
                IssueBuildCommand(RtsBuildingType.GuardTower, point);
            }
        }

        private async void SendCommand(PlayerCommand command)
        {
            if (lockstep != null && lockstep.Client != null && lockstep.Client.IsConnected && lockstep.Client.IsGameStarted)
            {
                await lockstep.Client.SendCommandAsync(
                    command.CommandType,
                    command.TargetId,
                    command.X,
                    command.Y,
                    command.Z,
                    command.Payload);
                return;
            }

            if (offlineTestMode)
            {
                command.PlayerId = LocalPlayerId;
                _simulation.ApplyLocalCommand(command);
            }
        }

        private void IssueMoveCommand(FixedVector3 point)
        {
            _selectedUnitScratch.Clear();
            for (int i = 0; i < _selectedIds.Count; i++)
            {
                if (_simulation.TryGetEntity(_selectedIds[i], out RtsEntity entity) &&
                    entity.OwnerId == LocalPlayerId &&
                    entity.Kind == RtsEntityKind.Unit)
                {
                    _selectedUnitScratch.Add(entity);
                }
            }

            if (_selectedUnitScratch.Count == 0)
            {
                return;
            }

            _selectedUnitScratch.Sort((a, b) => a.Id.CompareTo(b.Id));
            if (_selectedUnitScratch.Count == 1)
            {
                SendCommand(RtsCommandCodec.CreateMoveCommand(LocalPlayerId, _selectedUnitScratch[0].Id, point));
                return;
            }

            for (int i = 0; i < _selectedUnitScratch.Count; i++)
            {
                SendCommand(RtsCommandCodec.CreateMoveCommand(LocalPlayerId, _selectedUnitScratch[i].Id, point));
            }
        }

        private void IssueAttackCommand(int targetId)
        {
            for (int i = 0; i < _selectedIds.Count; i++)
            {
                if (_simulation.TryGetEntity(_selectedIds[i], out RtsEntity entity) &&
                    entity.OwnerId == LocalPlayerId &&
                    entity.Kind == RtsEntityKind.Unit)
                {
                    SendCommand(RtsCommandCodec.CreateAttackCommand(LocalPlayerId, entity.Id, targetId));
                }
            }
        }

        private void IssueBuildCommand(RtsBuildingType buildingType, FixedVector3 point)
        {
            for (int i = 0; i < _selectedIds.Count; i++)
            {
                if (_simulation.TryGetEntity(_selectedIds[i], out RtsEntity entity) &&
                    entity.OwnerId == LocalPlayerId &&
                    entity.Kind == RtsEntityKind.Unit &&
                    entity.UnitType == RtsUnitType.Worker)
                {
                    SendCommand(RtsCommandCodec.CreateBuildCommand(LocalPlayerId, entity.Id, buildingType, point));
                    return;
                }
            }
        }

        private void IssueProduceCommand(int buildingId, RtsUnitType unitType)
        {
            SendCommand(RtsCommandCodec.CreateProduceCommand(LocalPlayerId, buildingId, unitType));
        }

        private bool TryGetMapPoint(Vector2 screenPosition, out FixedVector3 point)
        {
            Ray ray = _camera.ScreenPointToRay(screenPosition);
            if (Mathf.Abs(ray.direction.y) <= 0.0001f)
            {
                point = FixedVector3.Zero;
                return false;
            }

            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f)
            {
                point = FixedVector3.Zero;
                return false;
            }

            Vector3 world = ray.origin + ray.direction * t;
            point = new FixedVector3(Fix64.FromFloat(world.x), Fix64.Zero, Fix64.FromFloat(world.z));
            return RtsTestMapFactory.IsInsideEllipse(RtsSimulation.ToPosition2(point));
        }

        private RtsEntity FindEntityAtScreen(Vector2 screenPosition, bool includeEnemies)
        {
            RtsEntity best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _simulation.Entities.Count; i++)
            {
                RtsEntity entity = _simulation.Entities[i];
                if (!entity.IsAlive || !entity.IsSelectable)
                {
                    continue;
                }

                if (!includeEnemies && entity.OwnerId != LocalPlayerId)
                {
                    continue;
                }

                Vector3 world = ToUnityPosition(entity.Position);
                Vector3 screen = _camera.WorldToScreenPoint(world);
                if (screen.z < 0f)
                {
                    continue;
                }

                float distance = Vector2.Distance(screenPosition, new Vector2(screen.x, screen.y));
                float pickRadius = entity.Kind == RtsEntityKind.Building ? 45f : 26f;
                if (distance <= pickRadius && distance < bestDistance)
                {
                    best = entity;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void SelectUnitsInRect(Vector2 start, Vector2 end)
        {
            Rect rect = MakeScreenRect(start, end);
            _selectedIds.Clear();
            for (int i = 0; i < _simulation.Entities.Count; i++)
            {
                RtsEntity entity = _simulation.Entities[i];
                if (!entity.IsAlive ||
                    entity.OwnerId != LocalPlayerId ||
                    entity.Kind != RtsEntityKind.Unit)
                {
                    continue;
                }

                Vector3 screen = _camera.WorldToScreenPoint(ToUnityPosition(entity.Position));
                if (screen.z >= 0f && rect.Contains(new Vector2(screen.x, screen.y)))
                {
                    _selectedIds.Add(entity.Id);
                }
            }
        }

        private void RefreshViews()
        {
            for (int i = 0; i < _simulation.Entities.Count; i++)
            {
                RtsEntity entity = _simulation.Entities[i];
                if (!entity.IsAlive)
                {
                    continue;
                }

                if (!_views.TryGetValue(entity.Id, out EntityView view))
                {
                    view = CreateEntityView(entity);
                    _views[entity.Id] = view;
                }

                UpdateEntityView(entity, view);
            }

            _removeScratch.Clear();
            foreach (var pair in _views)
            {
                if (!_simulation.TryGetEntity(pair.Key, out _))
                {
                    _removeScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < _removeScratch.Count; i++)
            {
                int id = _removeScratch[i];
                Destroy(_views[id].Root);
                _views.Remove(id);
                _selectedIds.Remove(id);
            }
        }

        private EntityView CreateEntityView(RtsEntity entity)
        {
            GameObject root = new GameObject("实体 " + entity.Id + " " + GetEntityDisplayName(entity));
            root.name = "实体 " + entity.Id + " " + GetEntityDisplayName(entity);

            GameObject body = GameObject.CreatePrimitive(GetEntityPrimitiveType(entity));
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            Collider collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.14f;
            label.fontSize = 48;
            label.color = Color.white;

            GameObject selectionObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            selectionObject.name = "Selection";
            selectionObject.transform.SetParent(root.transform, false);
            Collider selectionCollider = selectionObject.GetComponent<Collider>();
            if (selectionCollider != null)
            {
                Destroy(selectionCollider);
            }

            Renderer selectionRenderer = selectionObject.GetComponent<Renderer>();
            selectionRenderer.material.color = new Color(0.1f, 0.85f, 1f, 0.35f);
            selectionObject.SetActive(false);

            return new EntityView(root, body, body.GetComponent<Renderer>(), label, selectionObject);
        }

        private void UpdateEntityView(RtsEntity entity, EntityView view)
        {
            Vector3 scale = GetEntityScale(entity);
            float visualHeight = GetEntityVisualHeight(entity, scale);
            view.Root.transform.position = ToUnityPosition(entity.Position);
            view.Root.transform.localScale = Vector3.one;
            view.Body.transform.localScale = scale;
            view.Body.transform.localPosition = new Vector3(0f, visualHeight * 0.5f, 0f);
            view.Renderer.material.color = GetEntityColor(entity);

            view.Label.text = GetEntityDisplayName(entity);
            view.Label.transform.localPosition = new Vector3(0f, visualHeight + 0.22f, 0f);
            view.Label.characterSize = CalculateLabelCharacterSize(view.Label.transform.position);
            if (_camera != null)
            {
                view.Label.transform.rotation = _camera.transform.rotation;
            }

            bool selected = _selectedIds.Contains(entity.Id);
            view.Selection.SetActive(selected);
            view.Selection.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            float radius = RtsCatalog.GetEntityRadius(entity).ToFloat() * 2.4f;
            view.Selection.transform.localScale = new Vector3(radius, 0.02f, radius);
        }

        private void ConfigureCamera()
        {
            if (_camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                _camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }

            _camera.orthographic = true;
            _camera.orthographicSize = 18f;
            _camera.transform.position = new Vector3(0f, 36f, 0f);
            _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void CreateMapView()
        {
            GameObject map = new GameObject("Ellipse Test Nav Map");
            MeshFilter filter = map.AddComponent<MeshFilter>();
            MeshRenderer renderer = map.AddComponent<MeshRenderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.18f, 0.42f, 0.24f, 1f);

            const int segments = 96;
            Vector3[] vertices = new Vector3[segments + 1];
            int[] triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            float radiusX = RtsGameplayConstants.MapRadiusX.ToFloat();
            float radiusZ = RtsGameplayConstants.MapRadiusZ.ToFloat();
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radiusX, 0f, Mathf.Sin(angle) * radiusZ);
            }

            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i == segments - 1 ? 1 : i + 2;
                triangles[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh();
            mesh.name = "Runtime Ellipse Nav Map";
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            filter.sharedMesh = mesh;
        }

        private void CreateUi()
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("RTS Prototype UI");
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 800;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            _resourceText = CreateText("资源", canvasObject.transform, 18, FontStyle.Bold, new Color(0.94f, 0.9f, 0.72f, 1f));
            RectTransform resourceRect = _resourceText.GetComponent<RectTransform>();
            resourceRect.anchorMin = new Vector2(1f, 1f);
            resourceRect.anchorMax = new Vector2(1f, 1f);
            resourceRect.pivot = new Vector2(1f, 1f);
            resourceRect.anchoredPosition = new Vector2(-20f, -20f);
            resourceRect.sizeDelta = new Vector2(420f, 42f);

            GameObject panelObject = new GameObject("Command Panel");
            panelObject.transform.SetParent(canvasObject.transform, false);
            _commandPanel = panelObject.AddComponent<RectTransform>();
            _commandPanel.anchorMin = new Vector2(0.5f, 0f);
            _commandPanel.anchorMax = new Vector2(0.5f, 0f);
            _commandPanel.pivot = new Vector2(0.5f, 0f);
            _commandPanel.anchoredPosition = new Vector2(0f, 18f);
            _commandPanel.sizeDelta = new Vector2(920f, 152f);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.06f, 0.07f, 0.08f, 0.88f);

            _selectionText = CreateText("选择信息", panelObject.transform, 14, FontStyle.Bold, Color.white);
            RectTransform selectionRect = _selectionText.GetComponent<RectTransform>();
            selectionRect.anchorMin = new Vector2(0f, 1f);
            selectionRect.anchorMax = new Vector2(1f, 1f);
            selectionRect.pivot = new Vector2(0.5f, 1f);
            selectionRect.anchoredPosition = new Vector2(16f, -10f);
            selectionRect.sizeDelta = new Vector2(-32f, 52f);

            GameObject dragObject = new GameObject("Drag Box");
            dragObject.transform.SetParent(canvasObject.transform, false);
            _dragBox = dragObject.AddComponent<Image>();
            _dragBox.color = new Color(0.2f, 0.7f, 1f, 0.22f);
            _dragBox.raycastTarget = false;
            dragObject.SetActive(false);
        }

        private void RefreshUi()
        {
            RtsPlayerState player = _simulation.GetPlayer(LocalPlayerId);
            string state = _simulation.IsGameOver ? "   胜利方：玩家" + _simulation.WinnerPlayerId : string.Empty;
            _resourceText.text = "玩家" + LocalPlayerId + "   金币：" + (player != null ? player.Gold : 0) + state;

            string signature = BuildCommandSignature(player);
            if (signature == _lastCommandSignature)
            {
                return;
            }

            _lastCommandSignature = signature;
            ClearCommandButtons();
            if (_selectedIds.Count == 0)
            {
                _selectionText.text = "未选择目标";
                return;
            }

            if (_selectedIds.Count == 1 && _simulation.TryGetEntity(_selectedIds[0], out RtsEntity selected))
            {
                _selectionText.text = BuildSelectionDetails(selected);
                if (selected.OwnerId == LocalPlayerId && selected.Kind == RtsEntityKind.Building)
                {
                    AddProductionButtons(selected);
                    return;
                }

                if (selected.OwnerId == LocalPlayerId && selected.Kind == RtsEntityKind.Unit)
                {
                    AddUnitCommandButtons(selected.UnitType == RtsUnitType.Worker);
                    return;
                }
            }

            int unitCount = CountSelectedUnits(out bool hasWorker);
            _selectionText.text = BuildMultiSelectionDetails(unitCount, hasWorker);
            if (unitCount > 0)
            {
                AddUnitCommandButtons(hasWorker);
            }
        }

        private string BuildSelectionDetails(RtsEntity entity)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(96);
            builder.Append(GetEntityDisplayName(entity))
                .Append("   玩家")
                .Append(entity.OwnerId)
                .Append("   生命 ")
                .Append(entity.HitPoints)
                .Append("/")
                .Append(entity.MaxHitPoints);

            if (entity.Kind == RtsEntityKind.Unit)
            {
                builder.Append("\n命令 ")
                    .Append(GetOrderDisplayName(entity.Order))
                    .Append("   携带金币 ")
                    .Append(entity.CarriedGold);
            }
            else if (entity.Kind == RtsEntityKind.Building)
            {
                builder.Append("\n类型 ")
                    .Append(GetBuildingDisplayName(entity.BuildingType));
                if (entity.ProducingUnitType != RtsUnitType.None)
                {
                    builder.Append("   正在生产 ")
                        .Append(GetUnitDisplayName(entity.ProducingUnitType))
                        .Append(" ")
                        .Append(entity.ProductionRemaining);
                }
            }

            return builder.ToString();
        }

        private string BuildMultiSelectionDetails(int unitCount, bool hasWorker)
        {
            if (unitCount <= 0)
            {
                return "未选择可操作单位";
            }

            return "已选择 " + unitCount + " 个单位" + (hasWorker ? "   可使用农民建造命令" : string.Empty);
        }

        private void AddUnitCommandButtons(bool hasWorker)
        {
            AddCommandButton("移动", () => _pendingMode = PendingCommandMode.Move);
            AddCommandButton("攻击", () => _pendingMode = PendingCommandMode.Attack);

            if (hasWorker)
            {
                AddCommandButton("建造兵营\n" + RtsCatalog.GetBuildingCost(RtsBuildingType.Barracks), () => _pendingMode = PendingCommandMode.BuildBarracks);
                AddCommandButton("建造防御塔\n" + RtsCatalog.GetBuildingCost(RtsBuildingType.GuardTower), () => _pendingMode = PendingCommandMode.BuildGuardTower);
            }
        }

        private string BuildCommandSignature(RtsPlayerState player)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(64);
            builder.Append(LocalPlayerId)
                .Append('|')
                .Append(player != null ? player.Gold : 0)
                .Append('|')
                .Append((int)_pendingMode)
                .Append('|')
                .Append(_simulation.WinnerPlayerId);

            for (int i = 0; i < _selectedIds.Count; i++)
            {
                builder.Append('|').Append(_selectedIds[i]);
                if (_simulation.TryGetEntity(_selectedIds[i], out RtsEntity entity))
                {
                    builder.Append(':')
                        .Append((int)entity.Kind)
                        .Append(':')
                        .Append(entity.HitPoints)
                        .Append(':')
                        .Append((int)entity.ProducingUnitType)
                        .Append(':')
                        .Append(entity.ProductionRemaining.RawValue)
                        .Append(':')
                        .Append((int)entity.Order)
                        .Append(':')
                        .Append(entity.CarriedGold);
                }
            }

            return builder.ToString();
        }

        private void AddProductionButtons(RtsEntity building)
        {
            if (RtsCatalog.CanProduce(building.BuildingType, RtsUnitType.Worker))
            {
                AddCommandButton(GetUnitDisplayName(RtsUnitType.Worker) + "\n" + RtsCatalog.GetUnitCost(RtsUnitType.Worker), () => IssueProduceCommand(building.Id, RtsUnitType.Worker));
            }

            if (RtsCatalog.CanProduce(building.BuildingType, RtsUnitType.Soldier))
            {
                AddCommandButton(GetUnitDisplayName(RtsUnitType.Soldier) + "\n" + RtsCatalog.GetUnitCost(RtsUnitType.Soldier), () => IssueProduceCommand(building.Id, RtsUnitType.Soldier));
            }
        }

        private int CountSelectedUnits(out bool hasWorker)
        {
            int count = 0;
            hasWorker = false;
            for (int i = 0; i < _selectedIds.Count; i++)
            {
                if (_simulation.TryGetEntity(_selectedIds[i], out RtsEntity entity) &&
                    entity.OwnerId == LocalPlayerId &&
                    entity.Kind == RtsEntityKind.Unit)
                {
                    count++;
                    hasWorker |= entity.UnitType == RtsUnitType.Worker;
                }
            }

            return count;
        }

        private void ClearCommandButtons()
        {
            for (int i = 0; i < _commandButtons.Count; i++)
            {
                if (_commandButtons[i] != null)
                {
                    Destroy(_commandButtons[i]);
                }
            }

            _commandButtons.Clear();
        }

        private void AddCommandButton(string label, UnityEngine.Events.UnityAction action)
        {
            int buttonIndex = _commandButtons.Count;
            GameObject buttonObject = new GameObject("命令 " + label);
            buttonObject.transform.SetParent(_commandPanel, false);
            _commandButtons.Add(buttonObject);
            RectTransform rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(18f + buttonIndex * 138f, 18f);
            rect.sizeDelta = new Vector2(124f, 58f);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.14f, 0.18f, 0.22f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            Text text = CreateText("文字", buttonObject.transform, 14, FontStyle.Bold, Color.white);
            text.text = label;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.alignment = TextAnchor.MiddleCenter;
        }

        private Text CreateText(string name, Transform parent, int fontSize, FontStyle style, Color color)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.AddComponent<RectTransform>();
            Text text = textObject.AddComponent<Text>();
            text.text = name;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private void SetDragBoxVisible(bool visible)
        {
            if (_dragBox != null)
            {
                _dragBox.gameObject.SetActive(visible);
            }
        }

        private void UpdateDragBox(Vector2 start, Vector2 end)
        {
            if (_dragBox == null)
            {
                return;
            }

            RectTransform rect = _dragBox.rectTransform;
            Rect screenRect = MakeScreenRect(start, end);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = screenRect.position;
            rect.sizeDelta = screenRect.size;
        }

        private static Rect MakeScreenRect(Vector2 start, Vector2 end)
        {
            Vector2 min = Vector2.Min(start, end);
            Vector2 max = Vector2.Max(start, end);
            return new Rect(min, max - min);
        }

        private bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private static Vector3 ToUnityPosition(FixedVector3 position)
        {
            return new Vector3(position.X.ToFloat(), position.Y.ToFloat(), position.Z.ToFloat());
        }

        private static PrimitiveType GetEntityPrimitiveType(RtsEntity entity)
        {
            return entity.Kind == RtsEntityKind.Unit ? PrimitiveType.Cylinder : PrimitiveType.Cube;
        }

        private static string GetEntityDisplayName(RtsEntity entity)
        {
            if (entity.Kind == RtsEntityKind.Unit)
            {
                return GetUnitDisplayName(entity.UnitType);
            }

            if (entity.Kind == RtsEntityKind.Building)
            {
                return GetBuildingDisplayName(entity.BuildingType);
            }

            return "金矿";
        }

        private static string GetUnitDisplayName(RtsUnitType type)
        {
            switch (type)
            {
                case RtsUnitType.Worker:
                    return "农民";
                case RtsUnitType.Soldier:
                    return "士兵";
                default:
                    return "单位";
            }
        }

        private static string GetBuildingDisplayName(RtsBuildingType type)
        {
            switch (type)
            {
                case RtsBuildingType.TownHall:
                    return "主城";
                case RtsBuildingType.Barracks:
                    return "兵营";
                case RtsBuildingType.GuardTower:
                    return "防御塔";
                default:
                    return "建筑";
            }
        }

        private static string GetOrderDisplayName(RtsUnitOrder order)
        {
            switch (order)
            {
                case RtsUnitOrder.GatherGold:
                    return "采集金币";
                case RtsUnitOrder.Move:
                    return "移动";
                case RtsUnitOrder.Attack:
                    return "攻击";
                default:
                    return "待命";
            }
        }

        private static float GetEntityVisualHeight(RtsEntity entity, Vector3 scale)
        {
            return entity.Kind == RtsEntityKind.Unit ? scale.y * 2f : scale.y;
        }

        private float CalculateLabelCharacterSize(Vector3 worldPosition)
        {
            if (_camera == null)
            {
                return 0.14f;
            }

            float screenHeight = Mathf.Max(1f, Screen.height);
            float screenWidth = Mathf.Max(1f, Screen.width);
            float minScreenScale = Mathf.Clamp(Mathf.Min(screenWidth / 1920f, screenHeight / 1080f), 0.65f, 1f);
            float aspect = screenWidth / screenHeight;
            float aspectScale = Mathf.Clamp(aspect / (16f / 9f), 0.85f, 1.15f);
            float targetPixels = 9.5f * minScreenScale / aspectScale;
            float worldUnitsPerPixel;

            if (_camera.orthographic)
            {
                worldUnitsPerPixel = _camera.orthographicSize * 2f / screenHeight;
            }
            else
            {
                float distance = Mathf.Max(0.1f, Vector3.Distance(_camera.transform.position, worldPosition));
                worldUnitsPerPixel = 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / screenHeight;
            }

            return Mathf.Clamp(worldUnitsPerPixel * targetPixels * 0.45f, 0.08f, 0.18f);
        }

        private static Vector3 GetEntityScale(RtsEntity entity)
        {
            if (entity.Kind == RtsEntityKind.Resource)
            {
                return new Vector3(2.2f, 1.1f, 2.2f);
            }

            if (entity.Kind == RtsEntityKind.Building)
            {
                switch (entity.BuildingType)
                {
                    case RtsBuildingType.TownHall:
                        return new Vector3(3.2f, 2.2f, 3.2f);
                    case RtsBuildingType.Barracks:
                        return new Vector3(2.8f, 1.6f, 2.8f);
                    case RtsBuildingType.GuardTower:
                        return new Vector3(1.8f, 2.8f, 1.8f);
                }
            }

            return entity.UnitType == RtsUnitType.Worker
                ? new Vector3(0.7f, 0.45f, 0.7f)
                : new Vector3(0.9f, 0.55f, 0.9f);
        }

        private static Color GetEntityColor(RtsEntity entity)
        {
            if (entity.Kind == RtsEntityKind.Resource)
            {
                return new Color(0.95f, 0.73f, 0.18f, 1f);
            }

            Color playerColor = entity.OwnerId == 1
                ? new Color(0.15f, 0.45f, 0.95f, 1f)
                : new Color(0.92f, 0.22f, 0.18f, 1f);

            if (entity.Kind == RtsEntityKind.Building)
            {
                return Color.Lerp(playerColor, Color.gray, 0.25f);
            }

            return playerColor;
        }

        private readonly struct EntityView
        {
            public readonly GameObject Root;
            public readonly GameObject Body;
            public readonly Renderer Renderer;
            public readonly TextMesh Label;
            public readonly GameObject Selection;

            public EntityView(GameObject root, GameObject body, Renderer renderer, TextMesh label, GameObject selection)
            {
                Root = root;
                Body = body;
                Renderer = renderer;
                Label = label;
                Selection = selection;
            }
        }
    }
}
