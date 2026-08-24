using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using HarmonyLib;
using SFS;
using SFS.UI;
using SFS.Input;
using SFS.World;
using SFS.Parts;
using SFS.Cameras;
using SFS.Parts.Modules;
using static SFS.Builds.BuildGrid;
using WorldBuild.Mod.Managers;
using WorldBuild.Mod.UI;
using System.Collections;
using System.Globalization;
using System.Net.Sockets;
using System.Threading.Tasks;
using SFS;
using WorldBuild.Mod.Modules;

namespace WorldBuild.Mod.Build
{
    public class WorldBuildManager : WorldManager<WorldBuildManager>
    {
        public bool worldBuildActive;
        public bool draggingPart;

        public Part heldPart { get; set; }
        private Part lastStagedPart;
        public Part DisplayPart => heldPart != null ? heldPart : lastStagedPart;
        public bool HasDisplayPart => DisplayPart != null;
        private readonly List<Part> stagedParts = new List<Part>();

        private List<PartCollider> heldPartColliders;

        Rocket closestRocket;
        Vector2 partTargetPos;
        private const float SnapDistance = 0.45f;
        private const float SnapAngle = 5f;
        private const float MinimumSurfaceOverlap = 0.05f;
        private const float BuildGridStep = 0.125f;
        private const byte FairingTransparencyAlpha = 72;
        private bool fairingTransparencyEnabled = true;

        // 选中草稿时屏蔽世界操作，建筑界面仍可点击。
        public bool BlocksWorldInteraction => worldBuildActive && heldPart != null;
        public bool FairingTransparencyEnabled => fairingTransparencyEnabled;
        private bool dragDiagnosticWorldPointerCaptured;
        private bool draggingStagedPart;
        private VariantRef pendingVariant;
        private bool awaitingWorldPlacement;
        private bool dragExitedPartsPanel;
        private int dragExitFrames;
        private int dragDiagnosticSession;
        private int lastMovementDiagnosticFrame = -999;
        private Vector2 lastMovementDiagnosticPosition;
        List<Collider2D> disabledColliders = new List<Collider2D>();
        Dictionary<Mesh, List<Color32>> defaultMeshColors = new Dictionary<Mesh, List<Color32>>();
        private readonly Dictionary<MeshRenderer, Material[]> fairingOriginalMaterials = new Dictionary<MeshRenderer, Material[]>();
        private Material fairingTransparentMaterial;
        private LineRenderer selectedPartOutline;

        float rotOffset = 0;

        Color originalPartColor;

        PartPlacementState _partState;

        Vector2 lastAstronautPosition;
        
        Vector2 GetPlayerPosition
        {
            get
            {
                var player = PlayerController.main?.player?.Value;
                if (player == null) return Vector2.zero;

                var holder = player.GetComponentInChildren<PartHolder>();
                return holder != null ? (Vector2)holder.transform.position : (Vector2)player.transform.position;
            }
        }

        public static int PlacedFrames = int.MaxValue;

        private Astronaut ActiveAstronaut => PlayerController.main?.player?.Value is Astronaut_EVA eva
            ? eva.GetComponent<Astronaut>()
            : null;
        
        PartPlacementState PartPlacementState
        {
            get
            {
                return _partState;
            }
            set
            {
                _partState = value;
                SetPartColor(_partState == PartPlacementState.Allowed ? new Color(0, 1, 0, 0.5f) : new Color(1, 0, 0, 0.5f));
            }
        }

        // 拖放调试日志已关闭。
        private void DragDiagnostic(string message) { }
        private void DragDiagnosticMovement(string phase, Vector2 position) { }

        private bool PointerOverPartsPanel => PartPickerUI.IsPointerOverPartsWindow();

        private Vector2 WorldPointFromGameCamera(Vector2 pixel)
        {
            // 建筑位置始终使用世界相机。
            var worldCamera = GameCamerasManager.main?.world_Camera?.camera ?? ActiveCamera.Camera.camera;
            var depth = -worldCamera.transform.position.z;
            return worldCamera.ScreenToWorldPoint((Vector3)pixel + Vector3.forward * depth);
        }

        private static Vector2 RoundToBuildGrid(Vector2 position)
        {
            return new Vector2(
                Mathf.Round(position.x / BuildGridStep) * BuildGridStep,
                Mathf.Round(position.y / BuildGridStep) * BuildGridStep);
        }

        private IEnumerator InitialDragCoro()
        {
            // 鼠标离开零件栏后才显示新零件。
            while (heldPart != null && Input.GetMouseButton(0))
            {
                if (!PartPickerUI.IsPointerOverPartsWindow())
                {
                    // 等待坐标稳定，避免零件跳到错误位置。
                    if (!dragExitedPartsPanel)
                    {
                        dragExitedPartsPanel = true;
                        dragExitFrames = 0;
                        DragDiagnostic("Drag generation confirmed pointer left Parts; stabilizing world coordinate before activation.");
                    }

                    dragExitFrames++;
                    if (dragExitFrames < 4)
                    {
                        DragDiagnostic($"Drag generation waiting for stable world coordinate: exitFrame={dragExitFrames}/4");
                        yield return null;
                        continue;
                    }

                    var worldPosition = WorldPointFromGameCamera(Input.mousePosition);
                    partTargetPos = RoundToBuildGrid(worldPosition - heldPart.centerOfMass.Value);
                    heldPart.transform.position = partTargetPos;
                    if (!heldPart.gameObject.activeSelf)
                    {
                        heldPart.gameObject.SetActive(true);
                        draggingPart = true;
                        GUIManager.main.GetUI<PartControlsGUI>().NewGUI();
                        DragDiagnostic($"Drag generation visible after confirmed exit: world={worldPosition}; target={partTargetPos}; transform={heldPart.transform.position}");
                    }
                    DragDiagnosticMovement("Drag generation entered world", partTargetPos);
                }
                else
                {
                    dragExitedPartsPanel = false;
                    dragExitFrames = 0;
                }
                yield return null;
            }
        }

        public void ReselectLastStagedPart()
        {
            if (heldPart != null || lastStagedPart == null || !stagedParts.Contains(lastStagedPart)) return;
            BeginDraggingStagedPart(lastStagedPart, lastStagedPart.transform.position);
        }

        private void BeginDraggingStagedPart(Part part, Vector2 worldPosition)
        {
            if (part == null || !stagedParts.Remove(part))
            {
                DragDiagnostic($"Native long press rejected: part={(part == null ? "null" : part.name)}; staged={stagedParts.Count}");
                return;
            }

            heldPart = part;
            SetDraftPhysics(heldPart, false);
            draggingPart = true;
            draggingStagedPart = true;
            // 从当前位置继续拖动。
            partTargetPos = heldPart.transform.position;
            RefreshPartColliders();
            DragDiagnostic($"Native long press activated: part={part.name}; world={worldPosition}; target={partTargetPos}; stagedAfter={stagedParts.Count}");
            GUIManager.main.GetUI<PartControlsGUI>().NewGUI();
        }

        private bool TryFindStagedPart(Vector2 worldPosition, out Part selectedPart)
        {
            var candidates = stagedParts.Where(part => part != null).ToArray();
            if (Part_Utility.RaycastParts(candidates, worldPosition, 5f, out var hit))
            {
                selectedPart = hit.part;
                DragDiagnostic($"Draft hit via polygon: part={selectedPart.name}; world={worldPosition}");
                return true;
            }

            // 草稿无碰撞体，点击范围过小时使用外框辅助选择。
            var boundsHit = candidates
                .Select(part => new { part, ok = Part_Utility.GetFramingBounds_WorldSpace(out var bounds, part), bounds })
                .Where(entry => entry.ok && new Rect(entry.bounds.xMin - 1.25f, entry.bounds.yMin - 1.25f,
                    entry.bounds.width + 2.5f, entry.bounds.height + 2.5f).Contains(worldPosition))
                .OrderBy(entry => ((Vector2)entry.bounds.center - worldPosition).sqrMagnitude)
                .FirstOrDefault();
            if (boundsHit != null)
            {
                selectedPart = boundsHit.part;
                DragDiagnostic($"Draft hit via expanded bounds: part={selectedPart.name}; world={worldPosition}; bounds={boundsHit.bounds}");
                return true;
            }

            selectedPart = candidates
                .OrderBy(part => ((Vector2)part.transform.position - worldPosition).sqrMagnitude)
                .FirstOrDefault();
            var centerHit = selectedPart != null && ((Vector2)selectedPart.transform.position - worldPosition).sqrMagnitude <= 9f;
            DragDiagnostic($"Draft hit fallback: part={(selectedPart == null ? "none" : selectedPart.name)}; world={worldPosition}; accepted={centerHit}");
            return centerHit;
        }

        private void OnWorldInputStart(OnInputStartData data)
        {
            if (!worldBuildActive || data.inputType != InputType.MouseLeft) return;
            var worldPosition = data.position.World(0f);

            if (!PartPickerUI.IsPointOverPartsWindow(data.position.pixel)
                && TryFindStagedPart(worldPosition, out var stagedPart))
            {
                // 先保存当前预览，再拖动已预放置草稿。
                if (heldPart != null && heldPart != stagedPart)
                {
                    var currentPreview = heldPart;
                    if (!StageHeldPart())
                    {
                        DragDiagnostic($"Draft transfer blocked: current preview={currentPreview.name} could not be pre-placed.");
                        return;
                    }
                    DragDiagnostic($"Draft transfer: staged current preview={currentPreview.name}; selecting={stagedPart.name}");
                }

                if (heldPart == null)
                {
                    DragDiagnostic($"Draft selection: pixel={data.position.pixel}; world={worldPosition}; part={stagedPart.name}");
                    BeginDraggingStagedPart(stagedPart, worldPosition);
                    return;
                }
            }

            if (heldPart != null)
            {
                draggingPart = Part_Utility.RaycastParts(new[] { heldPart }, worldPosition, 0.3f, out var _);
                DragDiagnostic($"Input start: pixel={data.position.pixel}; world={worldPosition}; hitHeld={draggingPart}; held={heldPart.name}");
            }
        }

        private void OnWorldLongClick(OnTouchLongClickData data)
        {
            if (!worldBuildActive || heldPart != null || awaitingWorldPlacement) return;
            var worldPosition = data.position.World(0f);
            if (TryFindStagedPart(worldPosition, out var part))
                BeginDraggingStagedPart(part, worldPosition);
            else
                DragDiagnostic($"Native long press missed: pixel={data.position.pixel}; world={worldPosition}; staged={stagedParts.Count}");
        }

        private void OnWorldDrag(DragData data)
        {
            if (!worldBuildActive || !draggingPart || heldPart == null || !heldPart.gameObject.activeSelf) return;
            partTargetPos = RoundToBuildGrid(partTargetPos - data.DeltaWorld(0f));
            heldPart.transform.position = partTargetPos;
            DragDiagnosticMovement("Reference world drag", partTargetPos);
        }

        private void OnWorldInputEnd(OnInputEndData data)
        {
            if (!worldBuildActive || heldPart == null || data.inputType != InputType.MouseLeft) return;
            var wasStagedDrag = draggingStagedPart;
            var wasDragging = draggingPart;
            var releasedOverParts = PartPickerUI.IsPointOverPartsWindow(data.position.pixel);
            var releaseWorld = WorldPointFromGameCamera(data.position.pixel);
            draggingPart = false;
            DragDiagnostic($"Input end: pixel={data.position.pixel}; releaseWorld={releaseWorld}; oldTarget={partTargetPos}; stagedDrag={wasStagedDrag}; wasDragging={wasDragging}; overParts={releasedOverParts}");

            if (releasedOverParts)
            {
                DragDiagnostic($"Parts recycle: destroying part={heldPart.name}; wasStagedDrag={wasStagedDrag}");
                DestroyHeldPart();
                return;
            }

            if (wasStagedDrag)
            {
                draggingStagedPart = false;
                StageHeldPart();
                return;
            }

            // 新零件松开时使用世界坐标确定位置。
            if (wasDragging && heldPart != null && heldPart.gameObject.activeSelf)
            {
                partTargetPos = RoundToBuildGrid(releaseWorld - heldPart.centerOfMass.Value);
                heldPart.transform.position = partTargetPos;
                DragDiagnostic($"Release point applied to new preview: releaseWorld={releaseWorld}; finalTarget={partTargetPos}; transform={heldPart.transform.position}");
            }
        }

        Rocket GetBestRocket(Rocket[] rockets, float limiter = 1.5f)
        {
            var bestDistance = limiter * limiter;
            Rocket bestRocket = null;
            var heldPosition = (Vector2)heldPart.transform.position;

            foreach (var rocket in rockets)
            {
                if (rocket == null || !rocket.physics.loader.Loaded) continue;

                foreach (var rocketPart in rocket.partHolder.partsSet)
                {
                    var distance = ((Vector2)rocketPart.transform.position - heldPosition).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    bestRocket = rocket;
                }
            }
            return bestRocket;
        }

        private IEnumerable<Part> GetSnapTargets()
        {
            foreach (var part in stagedParts)
            {
                if (part != null && part != heldPart)
                    yield return part;
            }

            foreach (var rocket in GameManager.main.rockets)
            {
                if (rocket == null || !rocket.physics.loader.Loaded) continue;
                foreach (var part in rocket.partHolder.partsSet)
                {
                    if (part != null && part != heldPart)
                        yield return part;
                }
            }
        }

        private bool TrySnapHeldPart(out Vector2 snappedPosition)
        {
            snappedPosition = heldPart.transform.position;
            var heldSurfaces = heldPart.GetAttachmentSurfacesWorld();
            if (heldSurfaces == null || heldSurfaces.Length == 0) return false;

            var bestDistance = SnapDistance;
            var bestOffset = Vector2.zero;
            var found = false;

            foreach (var targetPart in GetSnapTargets())
            {
                var targetSurfaces = targetPart.GetAttachmentSurfacesWorld();
                if (targetSurfaces == null || targetSurfaces.Length == 0) continue;

                foreach (var heldSurface in heldSurfaces)
                {
                    var heldDirection = heldSurface.end - heldSurface.start;
                    if (heldDirection.sqrMagnitude < 0.0001f) continue;

                    foreach (var targetSurface in targetSurfaces)
                    {
                        var targetDirection = targetSurface.end - targetSurface.start;
                        if (targetDirection.sqrMagnitude < 0.0001f) continue;

                        var angle = Vector2.Angle(heldDirection, targetDirection);
                        if (angle > SnapAngle && angle < 180f - SnapAngle) continue;

                        var tangent = targetDirection.normalized;
                        var normal = new Vector2(-tangent.y, tangent.x);
                        var separation = Vector2.Dot(targetSurface.start - heldSurface.start, normal);
                        var distance = Mathf.Abs(separation);
                        if (distance > bestDistance) continue;

                        var heldStart = Vector2.Dot(heldSurface.start, tangent);
                        var heldEnd = Vector2.Dot(heldSurface.end, tangent);
                        var targetStart = Vector2.Dot(targetSurface.start, tangent);
                        var targetEnd = Vector2.Dot(targetSurface.end, tangent);
                        var overlap = Mathf.Min(Mathf.Max(heldStart, heldEnd), Mathf.Max(targetStart, targetEnd))
                                      - Mathf.Max(Mathf.Min(heldStart, heldEnd), Mathf.Min(targetStart, targetEnd));
                        if (overlap < MinimumSurfaceOverlap) continue;

                        bestDistance = distance;
                        bestOffset = normal * separation;
                        found = true;
                    }
                }
            }

            if (found)
            {
                // 吸附成功后保留精确位置，避免网格取整留下缝隙。
                snappedPosition += bestOffset;
            }
            return found;
        }

        void Start()
        {
            AddInputs();
        }

        public void RefreshPartColliders()
        {
            heldPartColliders = CreateBuildColliders(heldPart);
        }

        // 绿色草稿不参与物理碰撞，Place all 后恢复。
        private void SetDraftPhysics(Part part, bool enabled)
        {
            if (part == null) return;
            foreach (var collider in part.GetComponentsInChildren<Collider2D>(true))
            {
                if (collider != null && !collider.isTrigger)
                    collider.enabled = enabled;
            }
            foreach (var body in part.GetComponentsInChildren<Rigidbody2D>(true))
            {
                if (body != null)
                    body.simulated = enabled;
            }
        }

        void InitializeAstronautFollow()
        {
            lastAstronautPosition = GetPlayerPosition;
        }

        void FollowAstronaut()
        {
        }

        PartPlacementState CalculateCollidersAndGetState(Dictionary<Rocket, List<PartCollider>> rocketColliders = null)
        {
            // Green pre-placed parts are blueprints, not physical objects. They never participate in
            // build collision, terrain clipping, or rocket clipping until Place all creates the rocket.
            if (worldBuildActive)
                return PartPlacementState.Allowed;

            if (rocketColliders == null)
            {
                rocketColliders = new Dictionary<Rocket, List<PartCollider>>();
                foreach (var rkt in GameManager.main.rockets.Where(r => r.physics.loader.Loaded))
                {
                    rocketColliders.Add(rkt, CreateBuildColliders(rkt.partHolder.GetArray()));
                }
            }
            
            foreach (var partPoly in heldPartColliders.SelectMany((col) => col.colliders))
            {
                foreach (var kvp in rocketColliders)
                {
                    foreach (var rocketPoly in kvp.Value.SelectMany((col) => col.colliders))
                    {
                        if (ConvexPolygon.Intersect(partPoly, rocketPoly, -0.08f))
                        {
                            return PartPlacementState.ClippingRocket;
                        }
                    }
                }

                foreach (var point in partPoly.points)
                {
                     var worldPos = WorldView.ToGlobalPosition(heldPart.transform.TransformPoint(point));
                     if (WorldView.main.ViewLocation.planet.IsInsideTerrain(worldPos, 1f, false))
                     {
                         return PartPlacementState.ClippingTerrain;
                     }
                }
            }
            return PartPlacementState.Allowed;
        }
        
        void Update()
        {
            HandleBuildShortcuts();
            UpdateSelectedPartOutline();

            // 位置只由输入事件更新，避免屏幕坐标造成偏移。
            if (heldPart == null || !draggingPart)
                return;

            rotOffset = heldPart.orientation.orientation.Value.z;
            closestRocket = GetBestRocket(GameManager.main.rockets.ToArray());
            var angle = closestRocket?.rb2d.rotation ?? ((float)WorldView.ToGlobalPosition(heldPart.transform.position).AngleDegrees - 90f);
            heldPart.transform.rotation = Quaternion.Euler(0, 0, angle + rotOffset);
            if (TrySnapHeldPart(out var snappedPosition))
            {
                partTargetPos = snappedPosition;
                heldPart.transform.position = snappedPosition;
            }
        }

        private void HandleBuildShortcuts()
        {
            if (!worldBuildActive || heldPart == null) return;

            if (Input.GetKeyDown(KeyCode.Q))
                Utility.RotatePart(heldPart, 90f);
            else if (Input.GetKeyDown(KeyCode.E))
                Utility.RotatePart(heldPart, -90f);
            else if (Input.GetKeyDown(KeyCode.W))
                Utility.ScalePart(heldPart, new Vector2(-1f, 1f));
            else if (Input.GetKeyDown(KeyCode.S))
                Utility.ScalePart(heldPart, new Vector2(1f, -1f));
            else if (Input.GetKeyDown(KeyCode.Escape))
                CancelSelectedPart();
            else
                return;

            RefreshPartColliders();
            DragDiagnostic($"Draft shortcut applied: held={heldPart?.name}; Q/E rotate, W horizontal flip, S vertical flip, Esc cancel");
        }

        public void CancelSelectedPart()
        {
            if (heldPart == null) return;
            var selectedPart = heldPart;
            if (draggingStagedPart)
            {
                draggingStagedPart = false;
                StageHeldPart();
                DragDiagnostic($"Draft selection cancelled and restored: part={selectedPart.name}");
            }
            else
            {
                DragDiagnostic($"New preview cancelled: part={selectedPart.name}");
                DestroyHeldPart();
            }
            HideSelectedPartOutline();
        }

        private void EnsureSelectedPartOutline()
        {
            if (selectedPartOutline != null) return;
            var outlineObject = new GameObject("World Build: Selected Draft Outline");
            outlineObject.transform.SetParent(transform, false);
            selectedPartOutline = outlineObject.AddComponent<LineRenderer>();
            selectedPartOutline.useWorldSpace = true;
            selectedPartOutline.loop = true;
            selectedPartOutline.widthMultiplier = 0.055f;
            selectedPartOutline.numCornerVertices = 2;
            selectedPartOutline.numCapVertices = 2;
            selectedPartOutline.material = new Material(Shader.Find("Sprites/Default"));
            selectedPartOutline.startColor = Color.white;
            selectedPartOutline.endColor = Color.white;
            selectedPartOutline.sortingOrder = 200;
            selectedPartOutline.enabled = false;
        }

        private void UpdateSelectedPartOutline()
        {
            if (!worldBuildActive || heldPart == null || !heldPart.gameObject.activeSelf
                || !Part_Utility.GetFramingBounds_WorldSpace(out var bounds, heldPart))
            {
                HideSelectedPartOutline();
                return;
            }

            EnsureSelectedPartOutline();
            var padding = 0.08f;
            var min = bounds.min - Vector2.one * padding;
            var max = bounds.max + Vector2.one * padding;
            selectedPartOutline.positionCount = 4;
            selectedPartOutline.SetPositions(new[]
            {
                new Vector3(min.x, min.y, -0.2f),
                new Vector3(max.x, min.y, -0.2f),
                new Vector3(max.x, max.y, -0.2f),
                new Vector3(min.x, max.y, -0.2f)
            });
            selectedPartOutline.enabled = true;
        }

        private void HideSelectedPartOutline()
        {
            if (selectedPartOutline != null)
                selectedPartOutline.enabled = false;
        }

        IEnumerator PartColliderCalculation()
        {
            while (heldPart != null)
            {
                RefreshPartColliders();
                PartPlacementState = CalculateCollidersAndGetState();
                var runEvery = 8; //th frame

                for (var i = 1; i < runEvery; i++)
                {
                    yield return null;
                }
            }
        }

        List<PartCollider> CreateBuildColliders(params Part[] parts)
        {
            var buildColliders = new List<PartCollider>();
            for (var i = 0; i < parts.Length; i++)
            {
                var modules = parts[i].GetModules<PolygonData>();
                foreach (var polygonData in modules)
                {
                    if (polygonData.BuildCollider /* _IncludeInactive */)
                    {
                        var partCollider = new PartCollider
                        {
                            module = polygonData,
                            colliders = null
                        };
                        partCollider.UpdateColliders();
                        buildColliders.Add(partCollider);
                    }
                }
            }
            return buildColliders;
        }

        public void EnterBuild()
        {
            worldBuildActive = true;
            ApplyFairingTransparency();
            PartPickerUI.CreateUI();
        }

        public void ExitBuild()
        {
            draggingPart = false;
            HideSelectedPartOutline();
            worldBuildActive = false;
            RestoreFairingColors();
            PartPickerUI.DestroyUI();
            DestroyAllPendingParts();
        }

        public void ToggleFairingTransparency()
        {
            fairingTransparencyEnabled = !fairingTransparencyEnabled;
            if (fairingTransparencyEnabled)
                ApplyFairingTransparency();
            else
                RestoreFairingColors();
            DragDiagnostic($"Fairing transparency toggled: enabled={fairingTransparencyEnabled}");
        }

        private bool IsFairingPart(Part part)
        {
            if (part == null) return false;
            if (part.variants != null && part.variants.Any(variantGroup =>
                variantGroup.tags != null && variantGroup.tags.Any(tag => tag?.tag != null
                    && string.Equals(tag.tag.displayName.Field, "Fairings", StringComparison.OrdinalIgnoreCase))))
                return true;

            var category = part.pickCategoryName?.Field ?? string.Empty;
            var name = part.name ?? string.Empty;
            return category.IndexOf("fairing", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("fairing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private IEnumerable<Part> GetDraftFairings()
        {
            foreach (var part in stagedParts)
            {
                if (part != null && IsFairingPart(part))
                    yield return part;
            }
            if (heldPart != null && !stagedParts.Contains(heldPart) && IsFairingPart(heldPart))
                yield return heldPart;
        }

        private void RememberMeshColors(Part part)
        {
            if (part == null) return;
            foreach (var partMesh in part.GetModules<BaseMesh>())
            {
                var mesh = AccessTools.FieldRefAccess<BaseMesh, Mesh>("meshReference").Invoke(partMesh);
                if (mesh == null || defaultMeshColors.ContainsKey(mesh)) continue;
                var colors = new List<Color32>();
                mesh.GetColors(colors);
                defaultMeshColors.Add(mesh, colors);
            }
        }

        private void ApplyFairingTransparency()
        {
            if (!fairingTransparencyEnabled) return;
            var fairings = GetDraftFairings().ToArray();
            if (fairingTransparentMaterial == null)
            {
                fairingTransparentMaterial = new Material(Shader.Find("Sprites/Default"))
                {
                    name = "World Build: Transparent Fairing Material",
                    color = new Color(1f, 1f, 1f, FairingTransparencyAlpha / 255f),
                    renderQueue = 3000
                };
            }

            foreach (var fairing in fairings)
            {
                RememberMeshColors(fairing);
                foreach (var partMesh in fairing.GetModules<BaseMesh>())
                {
                    var mesh = AccessTools.FieldRefAccess<BaseMesh, Mesh>("meshReference").Invoke(partMesh);
                    var renderer = partMesh.GetComponent<MeshRenderer>();
                    if (renderer != null && !fairingOriginalMaterials.ContainsKey(renderer))
                        fairingOriginalMaterials.Add(renderer, renderer.sharedMaterials);
                    if (renderer != null)
                        renderer.sharedMaterials = Enumerable.Repeat(fairingTransparentMaterial, Math.Max(renderer.sharedMaterials.Length, 1)).ToArray();
                    if (mesh != null && defaultMeshColors.TryGetValue(mesh, out var colors))
                        mesh.SetColors(colors.Select(color => new Color32(color.r, color.g, color.b, FairingTransparencyAlpha)).ToList());
                }
            }
            DragDiagnostic($"Fairing transparency applied: fairings={fairings.Length}; renderers={fairingOriginalMaterials.Count}");
        }

        private void RestoreFairingColors()
        {
            foreach (var pair in fairingOriginalMaterials.ToArray())
            {
                if (pair.Key != null)
                    pair.Key.sharedMaterials = pair.Value;
            }
            fairingOriginalMaterials.Clear();
            foreach (var fairing in GetDraftFairings())
                ResetPartColor(fairing);
        }

        public void ToggleBuild()
        {
            if (PartPickerUI.GUIHolder == null)
                EnterBuild();
            else
                ExitBuild();
        }

        public void CreateNewPart(VariantRef variant, Vector2 mousePos)
        {
            if (heldPart != null)
            {
                if (awaitingWorldPlacement) DestroyHeldPart();
                else if (!StageHeldPart()) return;
            }

            dragDiagnosticSession++;
            lastMovementDiagnosticFrame = -999;
            heldPart = PartsLoader.CreatePart(variant, true);
            heldPart.transform.parent = transform;
            // 在零件栏内保持隐藏，离开后才定位显示。
            heldPart.gameObject.SetActive(false);
            partTargetPos = Vector2.zero;
            pendingVariant = null;
            awaitingWorldPlacement = false;
            dragExitedPartsPanel = false;
            dragExitFrames = 0;
            draggingPart = false;
            draggingStagedPart = false;
            dragDiagnosticWorldPointerCaptured = false;
            DragDiagnostic($"Drag generation armed: variant={variant}; uiProjectedWorld={mousePos}; part hidden until pointer exits Parts.");

            SetDraftPhysics(heldPart, false);

            RememberMeshColors(heldPart);

            RefreshPartColliders();
            StartCoroutine(nameof(InitialDragCoro));
            StartCoroutine(nameof(PartColliderCalculation));
        }

        public void DestroyHeldPart()
        {
            var partToDestroy = heldPart;
            heldPart = null;
            HideSelectedPartOutline();
            draggingPart = false;
            awaitingWorldPlacement = false;
            dragExitedPartsPanel = false;
            draggingStagedPart = false;
            dragDiagnosticWorldPointerCaptured = false;
            disabledColliders.Clear();
            if (partToDestroy == null) return;
            try { partToDestroy.DestroyPart(false, false, DestructionReason.Intentional); } catch (NullReferenceException) { }
        }

        public bool StageHeldPart()
        {
            if (heldPart == null) return true;

            // 预放置前执行吸附，确保零件真正连接。
            if (TrySnapHeldPart(out var snappedPosition))
            {
                partTargetPos = snappedPosition;
                heldPart.transform.position = snappedPosition;
            }

            RefreshPartColliders();
            PartPlacementState = CalculateCollidersAndGetState();
            DragDiagnostic($"Pre-place requested: part={heldPart.name}; position={heldPart.transform.position}; target={partTargetPos}; state={PartPlacementState}; worldPointerCaptured={dragDiagnosticWorldPointerCaptured}");
            if (PartPlacementState == PartPlacementState.ClippingTerrain)
            {
                MsgDrawer.main.Log("Cannot pre-place a part inside the ground.");
                return false;
            }

            // 预放置后仍保持草稿状态。
            SetDraftPhysics(heldPart, false);
            SetPartColor(new Color(0f, 1f, 0f, 0.5f));
            if (!stagedParts.Contains(heldPart))
                stagedParts.Add(heldPart);
            DragDiagnostic($"Staged: part={heldPart.name}; position={heldPart.transform.position}; state={PartPlacementState}; stagedNow={stagedParts.Count}");
            ApplyFairingTransparency();
            lastStagedPart = heldPart;
            heldPart = null;
            HideSelectedPartOutline();
            draggingPart = false;
            awaitingWorldPlacement = false;
            dragExitedPartsPanel = false;
            draggingStagedPart = false;
            dragDiagnosticWorldPointerCaptured = false;
            disabledColliders.Clear();
            return true;
        }

        public void RefillOxygen()
        {
            var astronaut = ActiveAstronaut;
            if (astronaut == null)
            {
                MsgDrawer.main.Log("Control an EVA astronaut to refill oxygen.");
                return;
            }

            astronaut.RefillOxygenFromNearestRocket();
        }

        public void FinalizeBuild()
        {
            if (!StageHeldPart()) return;

            var parts = stagedParts.Where(part => part != null).ToList();
            // 清理未显示且仍在原点的无效预览。
            var originGhosts = parts.Where(part => ((Vector2)part.transform.position).sqrMagnitude < 0.0001f).ToList();
            foreach (var ghost in originGhosts)
            {
                stagedParts.Remove(ghost);
                try { ghost.DestroyPart(false, false, DestructionReason.Intentional); } catch (NullReferenceException) { }
            }
            if (originGhosts.Count > 0)
                DragDiagnostic($"Place all removed origin ghosts: count={originGhosts.Count}");

            parts = stagedParts.Where(part => part != null).ToList();
            DragDiagnostic($"Place all begin: staged={parts.Count}; parts=[{string.Join(",", parts.Select(part => part.name + "@" + part.transform.position))}]");
            if (parts.Count == 0)
            {
                MsgDrawer.main.Log("Pre-place at least one part first.");
                return;
            }

            JointGroup finalGroup = null;
            Rocket targetRocket = null;
            var oldParents = parts.ToDictionary(part => part, part => part.transform.parent);

            // 只尝试连接附近火箭。
            foreach (var candidate in GameManager.main.rockets
                         .Where(rocket => rocket != null && rocket.physics.loader.Loaded)
                         .OrderBy(rocket => parts.Min(part => ((Vector2)part.transform.position - (Vector2)rocket.partHolder.transform.position).sqrMagnitude)))
            {
                var nearestDistance = parts.Min(part => ((Vector2)part.transform.position - (Vector2)candidate.partHolder.transform.position).sqrMagnitude);
                if (nearestDistance > 2.25f) break;

                foreach (var part in parts) part.transform.parent = candidate.partHolder.transform;
                var combinedParts = candidate.partHolder.GetArray().ToList();
                var candidateJoints = RocketManager.GenerateJoints(combinedParts.ToArray());
                var candidateGroup = new JointGroup(candidateJoints, combinedParts);
                candidateGroup.RecreateGroups(out var candidateGroups);
                DragDiagnostic($"Candidate rocket: name={candidate.rocketName}; parts={combinedParts.Count}; joints={candidateJoints.Count}; groups={candidateGroups.Count}; nearestSqr={nearestDistance}");
                if (candidateGroups.Count == 1)
                {
                    finalGroup = candidateGroups[0];
                    targetRocket = candidate;
                    break;
                }

                foreach (var pair in oldParents) pair.Key.transform.parent = pair.Value;
            }

            if (finalGroup == null)
            {
                // 多个零件必须先连接成一组。
                var standaloneJoints = RocketManager.GenerateJoints(parts.ToArray());
                var standaloneGroup = new JointGroup(standaloneJoints, parts);
                standaloneGroup.RecreateGroups(out var standaloneGroups);
                DragDiagnostic($"Standalone group: parts={parts.Count}; joints={standaloneJoints.Count}; groups={standaloneGroups.Count}");
                if (standaloneGroups.Count != 1)
                {
                    MsgDrawer.main.Log("Connect all pre-placed parts before Place all.");
                    return;
                }
                finalGroup = standaloneGroups[0];
            }

            if (targetRocket != null)
            {
                targetRocket.SetJointGroup(finalGroup);
            }
            else
            {
                // 先记录预览中心，避免组装后位置跳变。
                var groupParts = finalGroup.parts.Where(part => part != null).ToArray();
                var totalMass = groupParts.Sum(part => part.mass.Value);
                var previewWorldCenter = totalMass > 0f
                    ? groupParts.Aggregate(Vector2.zero, (sum, part) => sum + (Vector2)part.transform.TransformPoint(part.centerOfMass.Value) * part.mass.Value) / totalMass
                    : (Vector2)parts[0].transform.TransformPoint(parts[0].centerOfMass.Value);
                var previewGlobalCenter = WorldView.ToGlobalPosition(previewWorldCenter);
                var previewRotation = parts[0].transform.rotation;
                DragDiagnostic($"Place all capture before SetJointGroup: worldCenter={previewWorldCenter}; globalCenter={previewGlobalCenter}; firstPart={parts[0].transform.position}");

                var rocket = Instantiate(AccessTools.StaticFieldRefAccess<RocketManager, Rocket>("prefab"));
                rocket.SetJointGroup(finalGroup);
                rocket.rb2d.SetRotation(previewRotation);
                rocket.physics.SetLocationAndState(
                    new Location(
                        WorldTime.main.worldTime,
                        WorldView.main.ViewLocation.planet,
                        previewGlobalCenter,
                        PlayerController.main.player.Value.location.velocity),
                    false);
                DragDiagnostic($"Place all physics initialized: rocketPosition={rocket.transform.position}; expectedPreviewCenter={previewWorldCenter}");
                rocket.stats.Load(-1);
            }

            RestoreFairingColors();
            foreach (var part in parts)
            {
                SetDraftPhysics(part, true);
                ResetPartColor(part);
            }

            stagedParts.Clear();
            lastStagedPart = null;
            pendingVariant = null;
            awaitingWorldPlacement = false;
            draggingPart = false;
            PlacedFrames = 0;
            DragDiagnostic($"Place all complete: attachedToExisting={targetRocket != null}; finalParts={finalGroup.parts.Count}; finalJoints={finalGroup.joints.Count}");
        }

        public void TryBuildPart() => FinalizeBuild();

        private void DestroyAllPendingParts()
        {
            DestroyHeldPart();
            foreach (var part in stagedParts.Where(part => part != null))
            {
                try { part.DestroyPart(false, false, DestructionReason.Intentional); } catch (NullReferenceException) { }
            }
            stagedParts.Clear();
            lastStagedPart = null;
            pendingVariant = null;
            awaitingWorldPlacement = false;
        }

        public void SetPartColor(Color color)
        {
            if (heldPart)
            {
                foreach (var partMesh in heldPart.GetModules<BaseMesh>())
                {
                    var mesh = AccessTools.FieldRefAccess<BaseMesh, Mesh>("meshReference").Invoke(partMesh);
                    if (!defaultMeshColors.ContainsKey(mesh))
                    {
                        var colors = new List<Color32>();
                        mesh.GetColors(colors);
                        defaultMeshColors.Add(mesh, colors);
                    }
                    mesh.SetColors(Enumerable.Repeat(color, mesh.vertices.Length).ToList());
                }
            }
        }

        public void ResetPartColor()
        {
            ResetPartColor(heldPart);
        }

        private void ResetPartColor(Part part)
        {
            if (part == null) return;
            foreach (var partMesh in part.GetModules<BaseMesh>())
            {
                var mesh = AccessTools.FieldRefAccess<BaseMesh, Mesh>("meshReference").Invoke(partMesh);
                if (mesh != null && defaultMeshColors.TryGetValue(mesh, out var colors))
                    mesh.SetColors(colors);
            }
        }

        public void AddInputs()
        {
            var input = GameManager.main.world_Input;
            input.onInputStart += OnWorldInputStart;
            input.onDrag += OnWorldDrag;
            input.onInputEnd += OnWorldInputEnd;
        }
    }

    enum PartPlacementState
    {
        ClippingTerrain,
        ClippingRocket,
        TooExpensive,
        Allowed,
    }
}