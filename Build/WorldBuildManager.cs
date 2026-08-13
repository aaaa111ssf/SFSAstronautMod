using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
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
using WorldBuild.Mod.Modules;
using SFS;
// ReSharper disable All

namespace WorldBuild.Mod.Build
{
    public class WorldBuildManager : WorldManager<WorldBuildManager>
    {
        public bool worldBuildActive;
        public bool draggingPart;

        public Part heldPart { get; set; }

        private List<PartCollider> heldPartColliders;

        Rocket closestRocket;
        Vector2 partTargetPos;
        List<Collider2D> disabledColliders = new List<Collider2D>();
        Dictionary<Mesh, List<Color32>> defaultMeshColors = new Dictionary<Mesh, List<Color32>>();

        float rotOffset = 0;

        Color originalPartColor;

        PartPlacementState _partState;

        Vector2 lastAstronautPosition;
        
        Vector2 GetPlayerPosition => PlayerController.main.player.Value.GetComponentInChildren<PartHolder>().transform.position;

        public static int PlacedFrames = int.MaxValue;
        
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

        IEnumerator InitialDragCoro()
        {
            while (Input.GetMouseButton(0))
            {
                partTargetPos = new TouchPosition(Input.mousePosition).World(0f) - heldPart.centerOfMass.Value;
                yield return null;
            }
        }

        Rocket GetBestRocket(Rocket[] rockets, float limiter = 6f)
        {
            var bestDist = limiter;
            Rocket bestRocket = null;

            var partPoints = new HashSet<Vector2>();

            foreach (var convex in heldPart.GetBuildColliderPolygons().Item1)
            {
                convex.points.ForEach(p => partPoints.Add(p));
            }

            foreach (var rocket in rockets)
            {
                if (!rocket.physics.loader.Loaded) continue;

                foreach (var rocketPart in rocket.partHolder.partsSet)
                {
                    foreach (var convex in rocketPart.GetBuildColliderPolygons().Item1)
                    {
                        foreach (var point in convex.points)
                        {
                            var tp = point; // seems like i shouldnt use transformpoint here
                            foreach (var partPoint in partPoints)
                            {
                                var dist = (tp - partPoint).magnitude;
                                if (dist <= bestDist)
                                {
                                    bestDist = dist;
                                    bestRocket = rocket;
                                }
                            }
                        }
                    }
                }
            }
            return bestRocket;
        }

        void Start()
        {
            AddInputs();
        }

        public void RefreshPartColliders()
        {
            heldPartColliders = CreateBuildColliders(heldPart);
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
            if (rocketColliders == null)
            {
                rocketColliders = new Dictionary<Rocket, List<PartCollider>>();
                foreach (var rkt in GameManager.main.rockets.Where(r => r.physics.loader.Loaded))
                {
                    rocketColliders.Add(rkt, CreateBuildColliders(rkt.partHolder.GetArray()));
                }
            }
            
            MsgDrawer.main.Log(WorldView.ToGlobalPosition(heldPart.transform.TransformPoint(Vector3.zero)).ToString());
            MsgDrawer.main.Log(WorldView.main.ViewLocation.planet.GetTerrainHeightAtAngle(WorldView.main.ViewLocation.position.AngleRadians, false).ToString(CultureInfo.InvariantCulture));

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
            if (heldPart == null)
                return;

            partTargetPos += GetPlayerPosition - lastAstronautPosition;
            InitializeAstronautFollow();
            
            rotOffset = heldPart.orientation.orientation.Value.z;

            closestRocket = GetBestRocket(GameManager.main.rockets.ToArray());

            // * Update part rotation.
            var angle = closestRocket?.rb2d.rotation ?? ((float) WorldView.ToGlobalPosition(heldPart.transform.position).AngleDegrees - 90f);
            heldPart.transform.rotation = Quaternion.Euler(0, 0, angle + rotOffset);

            // * Update part position.
            var pos = partTargetPos;
            if (closestRocket != null)
            {
                Vector2 localPos = closestRocket.partHolder.transform.InverseTransformPoint(pos);
                pos = closestRocket.partHolder.transform.TransformPoint(localPos.Round(0.5f));
            }
            heldPart.transform.position = pos;

            PlacedFrames++;
        }

        IEnumerator PartColliderCalculation()
        {
            while (heldPart != null)
            {
                RefreshPartColliders();
                PartPlacementState = CalculateCollidersAndGetState();
                if (AstronautSpawner.main.eva.GetComponent<Astronaut>().materialLeft < PartPriceCalculator.Calculate(heldPart))
                    PartPlacementState = PartPlacementState.TooExpensive;
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
            PartPickerUI.CreateUI();
        }

        public void ExitBuild()
        {
            draggingPart = false;
            worldBuildActive = false;
            PartPickerUI.DestroyUI();
            DestroyHeldPart();
        }

        public void ToggleBuild()
        {
            if (PartPickerUI.GUIHolder == null)
            {
                EnterBuild();
            } else {
                ExitBuild();
            }
        }

        public void CreateNewPart(VariantRef variant, Vector2 mousePos)
        {
            DestroyHeldPart();
            
            heldPart = PartsLoader.CreatePart(variant, true);
            heldPart.transform.parent = transform;
            heldPart.transform.position = mousePos;
            partTargetPos = mousePos;
            draggingPart = true;

            foreach (var col in heldPart.GetModules<Collider2D>())
            {
                if (col.isActiveAndEnabled && !col.isTrigger)
                {
                    disabledColliders.Add(col);
                    col.enabled = false;
                }
            }

            if (heldPart != null)
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
                }
            }

            InitializeAstronautFollow();
            RefreshPartColliders();

            StartCoroutine(nameof(InitialDragCoro));
            StartCoroutine(nameof(PartColliderCalculation));

            GUIManager.main.GetUI<PartControlsGUI>().NewGUI();
        }

        public void DestroyHeldPart()
        {
            disabledColliders.Clear();
            try { heldPart.DestroyPart(false, false, DestructionReason.Intentional); } catch (NullReferenceException) { }
        }

        public void TryBuildPart()
        {
            if (heldPart == null)
                return;

            RefreshPartColliders();
            PartPlacementState = CalculateCollidersAndGetState();

            if (PartPlacementState == PartPlacementState.ClippingRocket)
            {
                MsgDrawer.main.Log("Cannot build part inside another part! (enable the Part Clipping cheat for that)");
                return;
            }
            if (PartPlacementState == PartPlacementState.ClippingTerrain)
            {
                MsgDrawer.main.Log("Cannot build part inside the ground! (enable the Part Clipping cheat for that)");
                return;
            }

            var price = PartPriceCalculator.Calculate(heldPart);
            var astronaut = AstronautSpawner.main.eva.GetComponent<Astronaut>();
            
            if (astronaut.materialLeft < price)
            {
                MsgDrawer.main.Log("Not enough resources!");
                return;
            }
            
            astronaut.materialLeft -= price;

            foreach (var col in disabledColliders)
            {
                col.enabled = true;
            }

            if (closestRocket != null)
            {
                heldPart.transform.parent = closestRocket.partHolder.transform;
                var parts = closestRocket.partHolder.GetArray().AddItem(heldPart).ToArray();
                new JointGroup(RocketManager.GenerateJoints(parts), parts.ToList()).RecreateGroups(out var jointGroups);
                if (jointGroups.Count == 1)
                {
                    // * Part was attached to rocket.
                    closestRocket.SetJointGroup(jointGroups[0]);
                    goto Reset;
                }
            }
            // * Part was NOT attached to rocket.
            var group = new JointGroup(new List<PartJoint>(), new List<Part>() { heldPart });
            var rocket = Instantiate(AccessTools.StaticFieldRefAccess<RocketManager, Rocket>("prefab"));
            
            rocket.physics.SetLocationAndState
            (
                new Location
                (
                    WorldTime.main.worldTime,
                    WorldView.main.ViewLocation.planet,
                    WorldView.ToGlobalPosition(heldPart.transform.TransformPoint(heldPart.centerOfMass.Value)),
                    PlayerController.main.player.Value.location.velocity
                ),
                false
            );
            rocket.stats.Load(-1);
            rocket.rb2d.SetRotation(heldPart.transform.rotation);
            //TODO rocket.GetRotation()
            rocket.SetJointGroup(group);
            heldPart.transform.localPosition = Vector3.zero;
            
            Reset:
                ResetPartColor();
                draggingPart = false;
                heldPart = null;
                PlacedFrames = 0;
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
            if (heldPart != null)
            {
                foreach (var partMesh in heldPart.GetModules<BaseMesh>())
                {
                    var mesh = AccessTools.FieldRefAccess<BaseMesh, Mesh>("meshReference").Invoke(partMesh);
                    mesh.SetColors(defaultMeshColors.FirstOrDefault(m => m.Key == mesh).Value);
                }
            }
        }

        public void AddInputs()
        {
            var input = GameManager.main.world_Input;
            input.onInputStart += OnInputStart;
            input.onInputEnd += OnInputEnd;
            input.onDrag += OnDrag;
            ActiveCamera.Camera.position.OnChange += OnCameraPositionChange;

            void OnInputStart(OnInputStartData data)
            {
                if (data.inputType == InputType.MouseLeft && heldPart != null)
                {
                    var pos = data.position.World(0f);
                    draggingPart = Part_Utility.RaycastParts(new [] { heldPart }, pos, 0.3f, out var _);
                }
            }

            void OnInputEnd(OnInputEndData data)
            {
                if (data.LeftClick && heldPart != null)
                {
                    draggingPart = false;
                }
            }

            void OnDrag(DragData data)
            {
                if (draggingPart && heldPart != null)
                {
                    partTargetPos -= data.DeltaWorld(0f);
                }
            }

            void OnCameraPositionChange(Vector2 oldPos, Vector2 newPos)
            {
                if (heldPart != null)
                {
                    //partTargetPos += newPos - oldPos;
                }
            }
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