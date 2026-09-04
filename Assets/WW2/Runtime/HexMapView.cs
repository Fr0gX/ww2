using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Runtime
{
    public sealed class HexMapView : MonoBehaviour
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<GameObject> _staticSpawned = new List<GameObject>();
        private readonly Dictionary<HexCoord, Renderer> _tileRenderers = new Dictionary<HexCoord, Renderer>();
        private readonly Dictionary<HexCoord, Color> _tileColors = new Dictionary<HexCoord, Color>();
        private readonly Dictionary<int, GameObject> _unitMarkers = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, WallVisual> _wallVisuals = new Dictionary<int, WallVisual>();
        private readonly List<Coroutine> _combatPresentationCoroutines = new List<Coroutine>();
        private readonly Dictionary<Color32, Material> _materials = new Dictionary<Color32, Material>();
        private readonly Dictionary<Color32, Material> _litMaterials = new Dictionary<Color32, Material>();
        private static Mesh _hexMesh;
        private static Mesh _hexPrismMesh;
        private static Mesh _coneMesh;
        private static Mesh _gableRoofMesh;
        private static Mesh _beveledBoxMesh;
        private static Mesh _moundMesh;
        private static Mesh _mountainMesh;
        private static Mesh _irregularDiscMesh;
        private static Mesh _coniferCrownMesh;
        private static Mesh _lowPolySphereMesh;
        private static Material _surfaceTemplate;
        private static Material _unlitTemplate;
        private static Material _trailMaterial;
        private static Material _overlayTemplate;
        private static Material _grassSurfaceMaterial;
        private static Material _unitSpriteMaterial;
        private static Texture2D _grassSurfaceTexture;
        private static readonly Dictionary<TerrainType, Sprite> TerrainSprites =
            new Dictionary<TerrainType, Sprite>();
        private static readonly Dictionary<UnitType, Sprite> UnitSprites =
            new Dictionary<UnitType, Sprite>();
        private HexCoord? _hovered;
        private Coroutine _cameraRoutine;
        private int _leftClickFrame = -1;
        private int _rightClickFrame = -1;
        private Vector3 _overviewPosition = new Vector3(6f, 16f, -4f);
        private float _overviewSize = 8.5f;
        private HexMap _builtMap;
        private bool _trackingStatic;
        private int _activeCombatPresentations;

        private sealed class WallVisual
        {
            public GameObject Root;
            public readonly List<WallFaceVisual> Faces = new List<WallFaceVisual>();
        }

        private sealed class WallFaceVisual
        {
            public GameObject Root;
            public Renderer RootRenderer;
            public readonly List<GameObject> Segments = new List<GameObject>();
            public readonly List<GameObject> Rubble = new List<GameObject>();
        }

        public event Action<HexCoord> CellClicked;
        public event Action<HexCoord> CellRightClicked;
        public event Action<HexCoord?> CellHovered;
        public event Action BackgroundClicked;

        public void Build(GameState state, int viewingNationId, HashSet<HexCoord> visibleCells,
            HexCoord? selected, int? selectedUnitId,
            HashSet<int> selectableUnitIds, HashSet<HexCoord> legalMoves, HashSet<HexCoord> legalTargets,
            HashSet<HexCoord> legalSupportTargets, HashSet<int> legalWallTargetIds, HashSet<HexCoord> supplyReach,
            HashSet<HexCoord> enemyControlReach, IReadOnlyList<HexCoord> supplyPath, int supplyTier)
        {
            var rebuildStaticMap = !ReferenceEquals(_builtMap, state.Map);
            if (rebuildStaticMap)
            {
                ClearAll();
                _builtMap = state.Map;
                CalculateOverview(state);
                _trackingStatic = true;
                CreateBackdrop(state);
                foreach (var cell in state.Map.Cells.Values) CreateCellSurface(cell);
                CreateSharedGrid(state.Map);
                foreach (var cell in state.Map.Cells.Values) CreateTerrainFeature(cell);
                foreach (var cell in state.Map.Cells.Values)
                {
                    foreach (var neighbor in cell.RoadNeighbors)
                    {
                        if (Compare(cell.Coord, neighbor) < 0) CreateRoad(cell.Coord, neighbor);
                    }
                }
                foreach (var cell in state.Map.Cells.Values)
                    if (cell.RoadNeighbors.Count > 0) CreateRoadHub(state, cell);
                foreach (var wall in state.CityWalls.Values) CreateWallMarker(state, wall);
                _trackingStatic = false;
            }
            else
            {
                ClearDynamic();
            }

            var visible = visibleCells ?? new HashSet<HexCoord>();
            var legalWallTargetCells = new HashSet<HexCoord>();
            if (legalWallTargetIds != null)
            {
                foreach (var wallId in legalWallTargetIds)
                    if (state.CityWalls.TryGetValue(wallId, out var wall)) legalWallTargetCells.Add(wall.InnerPosition);
            }

            foreach (var cell in state.Map.Cells.Values)
            {
                UpdateCellSurface(state, cell, viewingNationId, selected, legalMoves, legalTargets, supplyReach,
                    enemyControlReach,
                    legalSupportTargets,
                    legalWallTargetCells);
                CreateCellContents(state, cell, viewingNationId, visible, selected, selectedUnitId, selectableUnitIds,
                    legalMoves, legalTargets);
            }

            foreach (var wall in state.CityWalls.Values)
            {
                UpdateWallMarker(state, wall, legalWallTargetIds != null && legalWallTargetIds.Contains(wall.Id));
            }

            if (selected.HasValue && supplyPath != null && supplyPath.Count > 1)
            {
                CreateSupplyPath(supplyPath, supplyTier);
            }

            foreach (var unit in state.Units.Values)
            {
                if (unit.Type != UnitType.MainInfantry || unit.Health <= 0 ||
                    !selectedUnitId.HasValue || selectedUnitId.Value != unit.Id ||
                    (unit.NationId != viewingNationId && !visible.Contains(unit.Position)))
                {
                    continue;
                }

                if (!unit.IsGarrisoned) continue;
                CreateZoneBoundary(state.Map, unit.Position, 1, new Color(1f, 0.82f, 0.16f),
                    $"Control Coverage {unit.Id}", 0.045f, 0.23f);
            }
        }

        public void NotifyCellClicked(HexCoord coord)
        {
            if (_leftClickFrame == Time.frameCount) return;
            _leftClickFrame = Time.frameCount;
            CellClicked?.Invoke(coord);
        }

        public void NotifyCellRightClicked(HexCoord coord)
        {
            if (_rightClickFrame == Time.frameCount) return;
            _rightClickFrame = Time.frameCount;
            CellRightClicked?.Invoke(coord);
        }

        public void NotifyBackgroundClicked()
        {
            if (_leftClickFrame == Time.frameCount) return;
            _leftClickFrame = Time.frameCount;
            BackgroundClicked?.Invoke();
        }

        public void NotifyCellHovered(HexCoord coord)
        {
            if (_hovered.HasValue && !_hovered.Value.Equals(coord)) RestoreTileColor(_hovered.Value);
            _hovered = coord;
            if (_tileRenderers.TryGetValue(coord, out var renderer) && _tileColors.TryGetValue(coord, out var color))
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", Color.Lerp(color, Color.white, 0.30f));
                renderer.SetPropertyBlock(block);
            }
            CellHovered?.Invoke(coord);
        }

        public void NotifyCellHoverEnded(HexCoord coord)
        {
            if (!_hovered.HasValue || !_hovered.Value.Equals(coord)) return;
            RestoreTileColor(coord);
            _hovered = null;
            CellHovered?.Invoke(null);
        }

        public void FocusOn(HexCoord coord, int actionRadius)
        {
            var world = ToWorld(coord);
            var size = Mathf.Clamp(4.8f + actionRadius * 0.35f, 5.2f, 7.4f);
            MoveCameraTo(WorldPresentation.CameraPositionForTarget(world, size, 1.2f), size);
        }

        public void FocusOverview()
        {
            MoveCameraTo(_overviewPosition, _overviewSize);
        }

        public void CancelCameraMotion()
        {
            if (_cameraRoutine == null) return;
            StopCoroutine(_cameraRoutine);
            _cameraRoutine = null;
        }

        public void PlayDamageNumber(HexCoord coord, int damage, bool counter)
        {
            PlayDamageNumberAt(ToWorld(coord), damage, counter);
        }

        private void PlayDamageNumberAt(Vector3 world, int damage, bool counter)
        {
            if (damage <= 0) return;
            var textObject = new GameObject(counter ? "Counter Damage" : "Damage");
            textObject.transform.SetParent(transform, false);
            textObject.transform.position = world + Vector3.up * 1.55f;
            var text = textObject.AddComponent<TextMesh>();
            text.text = $"-{damage}";
            text.fontSize = 92;
            text.characterSize = 0.105f;
            text.fontStyle = FontStyle.Bold;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = counter ? new Color(1f, 0.82f, 0.18f) : new Color(1f, 0.16f, 0.10f);
            var shadowObject = new GameObject("Damage Shadow");
            shadowObject.transform.SetParent(textObject.transform, false);
            shadowObject.transform.localPosition = new Vector3(0.035f, -0.035f, 0.025f);
            var shadow = shadowObject.AddComponent<TextMesh>();
            shadow.text = text.text;
            shadow.fontSize = text.fontSize;
            shadow.characterSize = text.characterSize;
            shadow.fontStyle = FontStyle.Bold;
            shadow.anchor = TextAnchor.MiddleCenter;
            shadow.alignment = TextAlignment.Center;
            shadow.color = new Color(0.04f, 0.04f, 0.05f, 0.92f);
            textObject.AddComponent<FloatingDamageEffect>().Initialize(0.92f);
        }

        public void PlayActionEffect(HexCoord from, HexCoord to, Color color, bool attack)
        {
            CreateActionTracer(from, to, color, attack);
        }

        public void PlayHealing(HexCoord from, HexCoord to, int amount)
        {
            var color = new Color(0.18f, 0.92f, 0.56f);
            CreateActionTracer(from, to, color, false, 0.34f, 0.30f, 1.15f);
            PlayPulse(to, color);
            var textObject = new GameObject("Healing");
            textObject.transform.SetParent(transform, false);
            textObject.transform.position = ToWorld(to) + Vector3.up * 1.55f;
            var text = textObject.AddComponent<TextMesh>();
            text.text = $"+{amount}";
            text.fontSize = 92;
            text.characterSize = 0.105f;
            text.fontStyle = FontStyle.Bold;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            textObject.AddComponent<FloatingDamageEffect>().Initialize(0.92f);
        }

        private void CreateActionTracer(HexCoord from, HexCoord to, Color color, bool attack,
            float duration = -1f, float arc = -1f, float size = 1f)
        {
            CreateActionTracerAt(ToWorld(from), ToWorld(to), color, attack, duration, arc, size);
        }

        private void CreateActionTracerAt(Vector3 from, Vector3 to, Color color, bool attack,
            float duration = -1f, float arc = -1f, float size = 1f)
        {
            var tracer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tracer.name = attack ? "Attack Trace" : "Move Trace";
            tracer.transform.SetParent(transform, false);
            tracer.transform.localScale = (attack ? new Vector3(0.30f, 0.18f, 0.30f) : Vector3.one * 0.18f) * size;
            var collider = tracer.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            SetColor(tracer.GetComponent<Renderer>(), color);
            var trail = tracer.AddComponent<TrailRenderer>();
            trail.time = attack ? 0.30f : 0.18f;
            trail.startWidth = attack ? 0.24f : 0.10f;
            trail.endWidth = 0f;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.sharedMaterial = TrailMaterial;
            tracer.AddComponent<WorldActionEffect>().Initialize(
                from + Vector3.up * 0.72f,
                to + Vector3.up * 0.72f,
                duration > 0f ? duration : attack ? 0.18f : 0.42f,
                arc >= 0f ? arc : attack ? 0.34f : 0.18f);
        }

        public void PlayUnitMove(int unitId, IReadOnlyList<HexCoord> path)
        {
            PlayUnitMove(unitId, path, 1f);
        }

        public void PlayUnitMove(int unitId, IReadOnlyList<HexCoord> path, float speedMultiplier)
        {
            if (path == null || path.Count < 2 || !_unitMarkers.TryGetValue(unitId, out var marker)) return;
            var points = new Vector3[path.Count];
            for (var i = 0; i < path.Count; i++) points[i] = ToWorld(path[i]);
            var trail = marker.AddComponent<TrailRenderer>();
            trail.time = 0.16f;
            trail.startWidth = 0.07f;
            trail.endWidth = 0f;
            trail.startColor = new Color(0.72f, 0.89f, 0.90f, 0.50f);
            trail.endColor = new Color(0.72f, 0.89f, 0.90f, 0f);
            trail.sharedMaterial = TrailMaterial;
            marker.AddComponent<UnitPathMoveEffect>().Initialize(points, 0.135f / Mathf.Max(0.1f, speedMultiplier));
        }

        public bool HasUnitMarker(int unitId)
        {
            return _unitMarkers.TryGetValue(unitId, out var marker) && marker != null && marker.activeInHierarchy;
        }

        public void PlayUnitMove(int unitId, HexCoord from, HexCoord to)
        {
            PlayUnitMove(unitId, new[] { from, to });
        }

        public bool IsUnitAnimating(int unitId)
        {
            if (!_unitMarkers.TryGetValue(unitId, out var marker) || marker == null) return false;
            var move = marker.GetComponent<UnitPathMoveEffect>();
            var lunge = marker.GetComponent<UnitLungeEffect>();
            return move != null && move.enabled || lunge != null && lunge.enabled;
        }

        public bool IsCombatPresentationActive => _activeCombatPresentations > 0;

        public void CancelActionPresentations()
        {
            foreach (var routine in _combatPresentationCoroutines)
                if (routine != null) StopCoroutine(routine);
            _combatPresentationCoroutines.Clear();
            _activeCombatPresentations = 0;
            foreach (var marker in _unitMarkers.Values)
            {
                if (marker == null) continue;
                var move = marker.GetComponent<UnitPathMoveEffect>();
                if (move != null) move.CompleteImmediately();
            }
        }

        public void PlayCombatSequence(int? attackerId, UnitType attackerType, HexCoord from, HexCoord to, int damage,
            int counterDamage, Color color, int? defenderId = null, UnitType? defenderType = null,
            bool defenderDestroyed = false, bool attackerDestroyed = false, bool wallDestroyed = false,
            float speedMultiplier = 1f, Vector3? targetWorld = null)
        {
            _activeCombatPresentations++;
            var routine = StartCoroutine(CombatSequence(attackerId, attackerType, from, to, damage, counterDamage,
                color, defenderId, defenderType, defenderDestroyed, attackerDestroyed, wallDestroyed,
                speedMultiplier, targetWorld));
            _combatPresentationCoroutines.Add(routine);
        }

        private IEnumerator CombatSequence(int? attackerId, UnitType attackerType, HexCoord from, HexCoord to,
            int damage, int counterDamage, Color color, int? defenderId, UnitType? defenderType,
            bool defenderDestroyed, bool attackerDestroyed, bool wallDestroyed, float speedMultiplier,
            Vector3? targetWorld)
        {
            var timing = 1f / Mathf.Max(0.25f, speedMultiplier);
            var fromWorld = ToWorld(from);
            var toWorld = targetWorld ?? ToWorld(to);
            if (attackerId.HasValue && _unitMarkers.TryGetValue(attackerId.Value, out var attackerMarker))
                attackerMarker.AddComponent<UnitWeaponRecoilEffect>().Initialize(attackerType, toWorld, 0.30f * timing);

            var shots = attackerType == UnitType.MainInfantry ? 4 : attackerType == UnitType.Medic ? 1 : 1;
            var projectileDuration = attackerType == UnitType.LightArtillery ? 0.38f : attackerType == UnitType.LightArmor ? 0.16f : 0.12f;
            var projectileArc = attackerType == UnitType.LightArtillery ? 1.18f : attackerType == UnitType.LightArmor ? 0.08f : 0.12f;
            var projectileSize = attackerType == UnitType.LightArmor ? 1.62f : attackerType == UnitType.LightArtillery ? 1.28f : 0.42f;
            for (var i = 0; i < shots; i++)
            {
                PlayMuzzleAt(fromWorld, toWorld, attackerType, color);
                CreateActionTracerAt(fromWorld, toWorld, color, true, projectileDuration * timing, projectileArc, projectileSize);
                if (i + 1 < shots) yield return new WaitForSecondsRealtime(0.052f * timing);
            }
            yield return new WaitForSecondsRealtime(projectileDuration * 0.82f * timing);
            PlayImpactAt(toWorld, color, attackerType,
                attackerType == UnitType.LightArmor || attackerType == UnitType.LightArtillery ? 1.48f : 0.90f);
            if (defenderId.HasValue && !defenderDestroyed && _unitMarkers.TryGetValue(defenderId.Value, out var targetMarker))
                targetMarker.AddComponent<UnitHitReactEffect>().Initialize(toWorld - fromWorld, 0.25f * timing);
            PlayDamageNumberAt(toWorld, damage, false);
            if (defenderDestroyed && defenderType.HasValue) PlayUnitDeathAt(toWorld, defenderType.Value, color, toWorld - fromWorld);
            if (wallDestroyed) PlayWallCollapseAt(toWorld, color);
            if (counterDamage <= 0)
            {
                yield return new WaitForSecondsRealtime(0.16f * timing);
                EndCombatPresentation();
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.14f * timing);
            var counterColor = new Color(1f, 0.78f, 0.20f);
            var counterType = defenderType ?? UnitType.MainInfantry;
            if (defenderId.HasValue && _unitMarkers.TryGetValue(defenderId.Value, out var defenderMarker))
                defenderMarker.AddComponent<UnitWeaponRecoilEffect>().Initialize(counterType, fromWorld, 0.25f * timing);
            PlayMuzzleAt(toWorld, fromWorld, counterType, counterColor);
            var counterArc = counterType == UnitType.LightArtillery ? 0.86f : 0.12f;
            var counterSize = counterType == UnitType.LightArmor ? 1.52f : counterType == UnitType.LightArtillery ? 1.14f : 0.50f;
            CreateActionTracerAt(toWorld, fromWorld, counterColor, true, 0.16f * timing, counterArc, counterSize);
            yield return new WaitForSecondsRealtime(0.14f * timing);
            PlayImpactAt(fromWorld, counterColor, counterType, 1.02f);
            if (attackerId.HasValue && !attackerDestroyed && _unitMarkers.TryGetValue(attackerId.Value, out attackerMarker))
                attackerMarker.AddComponent<UnitHitReactEffect>().Initialize(fromWorld - toWorld, 0.22f * timing);
            PlayDamageNumberAt(fromWorld, counterDamage, true);
            if (attackerDestroyed) PlayUnitDeathAt(fromWorld, attackerType, counterColor, fromWorld - toWorld);
            yield return new WaitForSecondsRealtime(0.16f * timing);
            EndCombatPresentation();
        }

        private void EndCombatPresentation()
        {
            _activeCombatPresentations = Mathf.Max(0, _activeCombatPresentations - 1);
            if (_activeCombatPresentations == 0) _combatPresentationCoroutines.Clear();
        }

        private void PlayImpactAt(Vector3 world, Color color, float intensity = 1f)
        {
            PlayImpactAt(world, color, UnitType.MainInfantry, intensity);
        }

        private void PlayMuzzleAt(Vector3 from, Vector3 to, UnitType type, Color color)
        {
            var direction = to - from;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            direction.Normalize();
            var height = type == UnitType.LightArmor ? 0.78f : type == UnitType.LightArtillery ? 0.66f : 0.82f;
            var distance = type == UnitType.LightArmor ? 0.58f : type == UnitType.LightArtillery ? 0.52f : 0.34f;
            var size = type == UnitType.LightArmor ? 0.62f : type == UnitType.LightArtillery ? 0.52f : 0.24f;
            var texture = type == UnitType.MainInfantry || type == UnitType.Medic ? "FX/muzzle_03" : "FX/muzzle_01";
            CreateFxBillboard(texture, from + direction * distance + Vector3.up * height,
                size, Color.Lerp(color, Color.white, 0.55f), 0.16f, 1.20f, direction * 0.10f);
            if (type == UnitType.LightArmor || type == UnitType.LightArtillery)
            {
                CreateFxBillboard("FX/smoke_04", from + direction * (distance * 0.65f) + Vector3.up * height,
                    size * 0.70f, Color.white, 0.34f, 1.65f, Vector3.up * 0.26f);
            }
        }

        private void PlayImpactAt(Vector3 world, Color color, UnitType type, float intensity = 1f)
        {
            PlayPulseAt(world, color);
            var heavy = type == UnitType.LightArmor || type == UnitType.LightArtillery;
            var primary = type == UnitType.LightArtillery ? "FX/dirt_03" :
                type == UnitType.LightArmor ? "FX/fire_01" : "FX/spark_06";
            CreateFxBillboard(primary, world + Vector3.up * (heavy ? 0.70f : 0.62f),
                (heavy ? 0.90f : 0.48f) * intensity, Color.white, heavy ? 0.54f : 0.28f,
                heavy ? 1.82f : 1.35f, Vector3.up * (heavy ? 0.42f : 0.18f));
            if (heavy)
            {
                CreateFxBillboard(type == UnitType.LightArtillery ? "FX/dirt_01" : "FX/smoke_07",
                    world + Vector3.up * 0.52f, 0.68f * intensity, Color.white, 0.66f, 2.05f,
                    Vector3.up * 0.48f);
            }
            var fragments = Mathf.RoundToInt(7f * intensity);
            for (var i = 0; i < fragments; i++)
            {
                var fragment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fragment.name = "Impact Fragment";
                fragment.transform.SetParent(transform, false);
                fragment.transform.position = world + Vector3.up * 0.66f;
                fragment.transform.localScale = new Vector3(0.07f, 0.07f, 0.22f) * intensity;
                fragment.transform.rotation = Quaternion.Euler(0f, i * (360f / fragments), 0f);
                var collider = fragment.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
                SetColor(fragment.GetComponent<Renderer>(), Color.Lerp(color, Color.white, 0.20f));
                var angle = i * Mathf.PI * 2f / fragments;
                fragment.AddComponent<ImpactFragmentEffect>().Initialize(
                    new Vector3(Mathf.Cos(angle), 0.34f, Mathf.Sin(angle)), 0.30f + intensity * 0.06f);
            }
            var camera = Camera.main;
            if (camera != null) camera.gameObject.AddComponent<CameraImpulseEffect>().Initialize(0.045f * intensity, 0.18f);
        }

        private void PlayUnitDeathAt(Vector3 world, UnitType type, Color impactColor, Vector3 attackDirection)
        {
            var proxy = CreateUnitModel(type, Color.Lerp(impactColor, new Color(0.28f, 0.29f, 0.28f), 0.52f));
            proxy.name = $"Defeated {type}";
            proxy.transform.SetParent(transform, false);
            proxy.transform.position = world + Vector3.up * 0.36f;
            var collider = proxy.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            proxy.AddComponent<UnitDeathEffect>().Initialize(type == UnitType.LightArmor ? 1.08f : 0.82f,
                type, attackDirection);
            var smokeCount = type == UnitType.LightArmor ? 5 : type == UnitType.LightArtillery ? 3 : 2;
            for (var i = 0; i < smokeCount; i++)
            {
                var angle = i * Mathf.PI * 2f / Mathf.Max(1, smokeCount);
                var texture = type == UnitType.LightArmor && i == 0 ? "FX/fire_01" : "FX/smoke_07";
                CreateFxBillboard(texture,
                    world + new Vector3(Mathf.Cos(angle) * 0.18f, 0.45f + i * 0.06f, Mathf.Sin(angle) * 0.18f),
                    type == UnitType.LightArmor ? 0.72f : 0.42f, Color.white,
                    type == UnitType.LightArmor ? 1.10f : 0.72f, 2.10f,
                    new Vector3(Mathf.Cos(angle) * 0.16f, 0.52f, Mathf.Sin(angle) * 0.16f));
            }
            if (type == UnitType.LightArmor && Camera.main != null)
                Camera.main.gameObject.AddComponent<CameraImpulseEffect>().Initialize(0.11f, 0.32f);
        }

        private void CreateFxBillboard(string texturePath, Vector3 position, float size, Color color,
            float duration, float growth, Vector3 velocity)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"FX {texturePath}";
            quad.transform.SetParent(transform, false);
            quad.transform.position = position;
            quad.transform.localScale = Vector3.one * size;
            var collider = quad.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = quad.GetComponent<Renderer>();
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = "Transient battle effect",
                mainTexture = PrototypeArtCatalog.Texture(texturePath),
                color = color
            };
            renderer.material = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.AddComponent<FxBurstEffect>().Initialize(duration, growth, velocity);
        }

        private void PlayWallCollapseAt(Vector3 world, Color color)
        {
            var debrisColor = Color.Lerp(color, new Color(0.34f, 0.31f, 0.27f), 0.68f);
            PlayImpactAt(world, debrisColor, 1.32f);
            CreateFxBillboard("FX/dirt_03", world + Vector3.up * 0.42f, 1.05f, Color.white,
                0.78f, 2.15f, Vector3.up * 0.38f);
            CreateFxBillboard("FX/smoke_04", world + Vector3.up * 0.50f, 0.72f, Color.white,
                0.92f, 2.30f, Vector3.up * 0.52f);
            for (var i = 0; i < 6; i++)
            {
                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "Collapsing Wall Block";
                chunk.transform.SetParent(transform, false);
                chunk.transform.position = world + new Vector3(0f, 0.30f + (i % 2) * 0.10f, 0f);
                chunk.transform.localScale = new Vector3(0.16f, 0.13f, 0.30f);
                chunk.transform.rotation = Quaternion.Euler(i * 9f, i * 57f, i * 13f);
                var collider = chunk.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
                SetColor(chunk.GetComponent<Renderer>(), i % 2 == 0 ? debrisColor : new Color(0.18f, 0.19f, 0.19f));
                var angle = i * Mathf.PI * 2f / 6f;
                chunk.AddComponent<ImpactFragmentEffect>().Initialize(
                    new Vector3(Mathf.Cos(angle) * 0.75f, 0.42f + (i % 3) * 0.15f,
                        Mathf.Sin(angle) * 0.75f), 0.58f);
            }
        }

        public void PlayPulse(HexCoord coord, Color color)
        {
            PlayPulseAt(ToWorld(coord), color);
        }

        private void PlayPulseAt(Vector3 world, Color color)
        {
            var pulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulse.name = "Result Pulse";
            pulse.transform.SetParent(transform, false);
            pulse.transform.position = world + Vector3.up * 0.22f;
            pulse.transform.localScale = new Vector3(0.70f, 0.035f, 0.70f);
            var collider = pulse.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            SetColor(pulse.GetComponent<Renderer>(), color);
            pulse.AddComponent<WorldPulseEffect>().Initialize(0.60f);
        }

        private void CreateCellSurface(HexCell cell)
        {
            var tile = new GameObject($"Hex {cell.Coord}");
            tile.transform.SetParent(transform, false);
            tile.transform.position = ToWorld(cell.Coord);
            var filter = tile.AddComponent<MeshFilter>();
            filter.sharedMesh = HexMesh;
            var groundRenderer = tile.AddComponent<MeshRenderer>();
            if (cell.Terrain == TerrainType.Plain)
                SetGrassSurface(groundRenderer, GroundColor(cell), cell.Coord);
            else
                SetColor(groundRenderer, GroundColor(cell), true);

            var overlay = new GameObject("Interaction field");
            overlay.transform.SetParent(tile.transform, false);
            overlay.transform.localPosition = Vector3.up * WorldPresentation.InteractionY;
            overlay.AddComponent<MeshFilter>().sharedMesh = HexMesh;
            var overlayRenderer = overlay.AddComponent<MeshRenderer>();
            _tileRenderers[cell.Coord] = overlayRenderer;
            var collider = tile.AddComponent<MeshCollider>();
            collider.sharedMesh = HexMesh;
            tile.AddComponent<HexCellClickTarget>().Initialize(this, cell.Coord);
            Track(tile);
        }

        private void UpdateCellSurface(GameState state, HexCell cell, int viewingNationId, HexCoord? selected,
            HashSet<HexCoord> legalMoves, HashSet<HexCoord> legalTargets,
            HashSet<HexCoord> supplyReach, HashSet<HexCoord> enemyControlReach,
            HashSet<HexCoord> legalSupportTargets, HashSet<HexCoord> legalWallTargets)
        {
            if (!_tileRenderers.TryGetValue(cell.Coord, out var renderer)) return;
            var tileColor = CellColor(state, cell, viewingNationId, selected, legalMoves, legalTargets, supplyReach,
                enemyControlReach, legalSupportTargets,
                legalWallTargets);
            tileColor.a = selected.HasValue ? 0.38f : 0f;
            // Hover uses a MaterialPropertyBlock. It must be removed on every
            // rebuild or the old highlighted color survives until the mouse moves.
            renderer.SetPropertyBlock(null);
            SetOverlayColor(renderer, tileColor);
            renderer.receiveShadows = false;
            _tileColors[cell.Coord] = tileColor;
        }

        private void CreateTerrainOverlay(HexCell cell)
        {
            if (cell.Terrain == TerrainType.Plain) return;
            var sprite = TerrainSprite(cell.Terrain);
            if (sprite == null) return;

            var overlay = new GameObject($"{cell.Terrain} ArtV6 overlay {cell.Coord}");
            overlay.transform.SetParent(transform, false);
            overlay.transform.position = ToWorld(cell.Coord) + Vector3.up * TerrainOverlayHeight(cell.Terrain);
            overlay.transform.rotation = WorldPresentation.CameraRotation;
            var size = TerrainOverlaySize(cell.Terrain);
            overlay.transform.localScale = new Vector3(size, size, 1f);

            var renderer = overlay.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.flipX = ((cell.Coord.Q * 17 + cell.Coord.R * 31) & 1) != 0;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Track(overlay);
        }

        private static Sprite TerrainSprite(TerrainType terrain)
        {
            if (TerrainSprites.TryGetValue(terrain, out var sprite)) return sprite;
            var asset = terrain switch
            {
                TerrainType.Forest => "ArtV6/Terrain/forest-overlay-v2",
                TerrainType.Hill => "ArtV6/Terrain/hill-overlay-v2",
                TerrainType.Mountain => "ArtV6/Terrain/mountain-overlay-v2",
                TerrainType.Marsh => "ArtV6/Terrain/marsh-overlay",
                _ => null
            };
            if (asset == null) return null;
            var texture = Resources.Load<Texture2D>(asset);
            if (texture == null)
            {
                Debug.LogWarning($"Missing ArtV6 terrain overlay: {asset}");
                return null;
            }

            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), Mathf.Max(texture.width, texture.height));
            sprite.name = terrain + " ArtV6 sprite";
            TerrainSprites[terrain] = sprite;
            return sprite;
        }

        private static float TerrainOverlaySize(TerrainType terrain)
        {
            return terrain switch
            {
                TerrainType.Forest => 2.38f,
                TerrainType.Hill => 2.34f,
                TerrainType.Mountain => 2.26f,
                TerrainType.Marsh => 2.30f,
                _ => 2.24f
            };
        }

        private static float TerrainOverlayHeight(TerrainType terrain)
        {
            return terrain switch
            {
                TerrainType.Forest => 0.80f,
                TerrainType.Hill => 0.66f,
                TerrainType.Mountain => 0.90f,
                TerrainType.Marsh => 0.88f,
                _ => 0.72f
            };
        }

        private void CreateSharedGrid(HexMap map)
        {
            const float radius = 1.12f;
            const float halfWidth = 0.009f;
            const float height = WorldPresentation.GridY;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            foreach (var cell in map.Cells.Values)
            {
                var center = ToWorld(cell.Coord);
                for (var edge = 0; edge < 6; edge++)
                {
                    var direction = EdgeDirection(edge);
                    if (direction >= 3 && map.TryGet(cell.Coord.Neighbor(direction), out _)) continue;

                    var a = center + HexCorner(edge, radius);
                    var b = center + HexCorner((edge + 1) % 6, radius);
                    var side = Vector3.Cross(Vector3.up, b - a).normalized * halfWidth;
                    var start = vertices.Count;
                    vertices.Add(new Vector3(a.x + side.x, height, a.z + side.z));
                    vertices.Add(new Vector3(a.x - side.x, height, a.z - side.z));
                    vertices.Add(new Vector3(b.x - side.x, height, b.z - side.z));
                    vertices.Add(new Vector3(b.x + side.x, height, b.z + side.z));
                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                    triangles.Add(start);
                    triangles.Add(start + 2);
                    triangles.Add(start + 3);
                }
            }

            var grid = new GameObject("Single shared hex grid");
            grid.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Shared hex grid mesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            grid.AddComponent<MeshFilter>().sharedMesh = mesh;
            SetColor(grid.AddComponent<MeshRenderer>(), new Color(0.36f, 0.47f, 0.20f));
            Track(grid);
        }

        private static int EdgeDirection(int edge)
        {
            return edge switch
            {
                0 => 0,
                1 => 5,
                2 => 4,
                3 => 3,
                4 => 2,
                _ => 1
            };
        }

        private static Vector3 HexCorner(int index, float radius)
        {
            var angle = Mathf.Deg2Rad * (60f * index);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private void CreateCellContents(GameState state, HexCell cell, int viewingNationId, HashSet<HexCoord> visible,
            HexCoord? selected, int? selectedUnitId, HashSet<int> selectableUnitIds,
            HashSet<HexCoord> legalMoves, HashSet<HexCoord> legalTargets)
        {
            var world = ToWorld(cell.Coord);
            if (cell.CityId.HasValue)
            {
                var city = state.Cities[cell.CityId.Value];
                CreateCityModel(city, world, cell.Coord);
                CreateCityFlag(city, world, cell.Coord);
            }

            if (cell.BuildingId.HasValue && state.Buildings.TryGetValue(cell.BuildingId.Value, out var building))
                CreateBuildingModel(state, building, world, cell.Coord);

            if (cell.UnitId.HasValue && state.Units.TryGetValue(cell.UnitId.Value, out var unit) &&
                (unit.NationId == viewingNationId || visible.Contains(cell.Coord)))
            {
                var markerColor = unit.IsSuppressed
                    ? new Color(0.68f, 0.36f, 0.78f)
                    : unit.NationId == 1
                    ? new Color(0.24f, 0.55f, 0.86f)
                    : new Color(0.82f, 0.29f, 0.24f);
                var exhausted = unit.NationId == viewingNationId && !unit.CanActThisTurn;
                if (exhausted)
                {
                    markerColor = Color.Lerp(markerColor, new Color(0.28f, 0.30f, 0.32f), 0.72f);
                }
                var visual = new GameObject($"Unit Visual #{unit.Id}");
                visual.transform.SetParent(transform, false);
                visual.transform.position = world;
                visual.AddComponent<HexCellClickTarget>().Initialize(this, cell.Coord);
                Track(visual);
                CreateUnitBase(unit, visual.transform);
                var marker = CreateUnitModel(unit, markerColor);
                marker.name = $"{unit.Type} #{unit.Id}";
                marker.transform.SetParent(visual.transform, false);
                marker.transform.localPosition = Vector3.up * 0.025f;
                if (exhausted) marker.transform.localScale *= 0.88f;
                marker.AddComponent<HexCellClickTarget>().Initialize(this, cell.Coord);
                _unitMarkers[unit.Id] = visual;

                if (selectedUnitId.HasValue && selectedUnitId.Value == unit.Id)
                {
                    marker.transform.localScale *= 1.15f;
                    CreateUnitRing(unit, visual.transform, new Color(1f, 0.84f, 0.18f), 0.80f, true);
                }
                else if (selectableUnitIds != null && selectableUnitIds.Contains(unit.Id))
                {
                    CreateUnitRing(unit, visual.transform, new Color(0.20f, 0.88f, 1f), 0.62f, true);
                }

                if (unit.NationId == viewingNationId && unit.Level < 4 &&
                    unit.PromotionKills >= RuleMath.KillsRequiredForPromotion(unit.Level))
                {
                    CreatePromotionBeacon(unit, visual.transform);
                }

                if (unit.IsGarrisoned)
                {
                    var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    ring.name = $"Garrison #{unit.Id}";
                    ring.transform.SetParent(visual.transform, false);
                    ring.transform.localPosition = Vector3.up * 0.025f;
                    ring.transform.localScale = new Vector3(0.66f, 0.04f, 0.66f);
                    SetColor(ring.GetComponent<Renderer>(), new Color(1f, 0.82f, 0.20f));
                    ring.AddComponent<HexCellClickTarget>().Initialize(this, cell.Coord);
                }
            }

        }

        private void CreateUnitRing(UnitState unit, Transform parent, Color color, float size, bool animated)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = $"Selectable #{unit.Id}";
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = Vector3.up * 0.018f;
            var diameter = Mathf.Max(size, UnitBaseDiameter(unit.Type) + 0.08f);
            ring.transform.localScale = new Vector3(diameter, 0.025f, diameter);
            SetColor(ring.GetComponent<Renderer>(), color);
            ring.AddComponent<HexCellClickTarget>().Initialize(this, unit.Position);
            if (animated) ring.AddComponent<SelectionPulseEffect>().Initialize(unit.Id * 0.75f);
        }

        private void CreatePromotionBeacon(UnitState unit, Transform parent)
        {
            var halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            halo.name = $"Promotion Halo #{unit.Id}";
            halo.transform.SetParent(parent, false);
            halo.transform.localPosition = Vector3.up * 0.84f;
            halo.transform.localScale = new Vector3(0.30f, 0.018f, 0.30f);
            SetColor(halo.GetComponent<Renderer>(), new Color(1f, 0.62f, 0.06f));
            halo.AddComponent<HexCellClickTarget>().Initialize(this, unit.Position);

            var diamond = GameObject.CreatePrimitive(PrimitiveType.Cube);
            diamond.name = $"Promotion Ready #{unit.Id}";
            diamond.transform.SetParent(parent, false);
            diamond.transform.localPosition = Vector3.up * 1.02f;
            diamond.transform.localRotation = Quaternion.Euler(0f, 45f, 45f);
            diamond.transform.localScale = Vector3.one * 0.22f;
            SetColor(diamond.GetComponent<Renderer>(), new Color(1f, 0.86f, 0.18f));
            diamond.AddComponent<HexCellClickTarget>().Initialize(this, unit.Position);
            diamond.AddComponent<SelectionPulseEffect>().Initialize(unit.Id * 0.61f);
        }

        private void CreateUnitBase(UnitState unit, Transform parent)
        {
            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            basePlate.name = $"Unit Base #{unit.Id}";
            basePlate.transform.SetParent(parent, false);
            basePlate.transform.localPosition = Vector3.up * 0.035f;
            var diameter = UnitBaseDiameter(unit.Type);
            basePlate.transform.localScale = new Vector3(diameter, 0.035f, diameter);
            SetColor(basePlate.GetComponent<Renderer>(), unit.NationId == 1
                ? new Color(0.055f, 0.16f, 0.27f)
                : new Color(0.28f, 0.075f, 0.055f));
            basePlate.AddComponent<HexCellClickTarget>().Initialize(this, unit.Position);
        }

        private GameObject CreateUnitModel(UnitState unit, Color color)
        {
            var tint = unit.IsSuppressed
                ? new Color(0.90f, 0.80f, 0.94f, 1f)
                : unit.CanActThisTurn
                    ? Color.white
                    : new Color(0.72f, 0.73f, 0.69f, 1f);
            return CreateUnitModel(unit.Type, tint, unit.NationId == 2);
        }

        private GameObject CreateUnitModel(UnitType type, Color color)
        {
            return CreateUnitModel(type, color, false);
        }

        private GameObject CreateUnitModel(UnitType type, Color color, bool flipX)
        {
            var root = new GameObject($"{type} Model");
            if (!CreateUnitSprite(root.transform, type, color, flipX))
            {
                root.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
                switch (type)
                {
                    case UnitType.LightArmor:
                        CreateLightArmorModel(root.transform, color);
                        break;
                    case UnitType.LightArtillery:
                        CreateLightArtilleryModel(root.transform, color);
                        break;
                    case UnitType.Medic:
                        CreateMedicModel(root.transform, color);
                        break;
                    default:
                        CreateInfantryModel(root.transform, color);
                        break;
                }
            }

            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.42f, 0f);
            collider.size = new Vector3(1.38f, 1.08f, 1.08f);
            return root;
        }

        private static bool CreateUnitSprite(Transform parent, UnitType type, Color tint, bool flipX)
        {
            var sprite = UnitSprite(type);
            if (sprite == null) return false;

            var spriteObject = new GameObject($"{type} AI-painted sprite");
            spriteObject.transform.SetParent(parent, false);
            spriteObject.transform.localPosition = Vector3.up * 0.018f;
            spriteObject.transform.rotation = WorldPresentation.CameraRotation;
            var size = UnitSpriteSize(type);
            spriteObject.transform.localScale = new Vector3(size, size, 1f);

            var renderer = spriteObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = tint;
            renderer.flipX = flipX;
            renderer.sharedMaterial = UnitSpriteMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            spriteObject.AddComponent<CameraFacingSprite>();
            return true;
        }

        private static Sprite UnitSprite(UnitType type)
        {
            if (UnitSprites.TryGetValue(type, out var cached)) return cached;
            var asset = type switch
            {
                UnitType.LightArmor => "ArtV6/Units/light-armor",
                UnitType.LightArtillery => "ArtV6/Units/light-artillery",
                UnitType.Medic => "ArtV6/Units/medic",
                _ => "ArtV6/Units/main-infantry"
            };
            var texture = Resources.Load<Texture2D>(asset);
            if (texture == null)
            {
                Debug.LogWarning($"Missing AI-painted unit sprite: {asset}");
                return null;
            }

            var pivot = type switch
            {
                // These are the centroids of the painted ground-contact shadows,
                // not the bottom of each transparent canvas. The physical base
                // therefore sits under the unit's weight rather than behind it.
                UnitType.LightArmor => new Vector2(0.504f, 0.335f),
                UnitType.LightArtillery => new Vector2(0.466f, 0.377f),
                UnitType.Medic => new Vector2(0.460f, 0.267f),
                _ => new Vector2(0.467f, 0.266f)
            };
            cached = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                pivot, Mathf.Max(texture.width, texture.height));
            cached.name = type + " AI-painted sprite";
            UnitSprites[type] = cached;
            return cached;
        }

        private static float UnitSpriteSize(UnitType type)
        {
            return type switch
            {
                UnitType.LightArmor => 2.65f,
                UnitType.LightArtillery => 2.20f,
                UnitType.Medic => 1.30f,
                _ => 1.90f
            };
        }

        private static Material UnitSpriteMaterial
        {
            get
            {
                if (_unitSpriteMaterial != null) return _unitSpriteMaterial;
                var shader = Resources.Load<Shader>("ArtV6BuildingSprite") ??
                             Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
                _unitSpriteMaterial = new Material(shader)
                {
                    name = "Shared AI-painted unit sprite"
                };
                return _unitSpriteMaterial;
            }
        }

        private static float UnitBaseDiameter(UnitType type)
        {
            return type == UnitType.LightArmor ? 1.08f : type == UnitType.LightArtillery ? 1.02f : 0.84f;
        }

        private void CreateInfantryModel(Transform parent, Color accent)
        {
            CreateSoldierFigure(parent, new Vector3(-0.18f, 0f, 0.06f), 8f, accent, false);
            CreateSoldierFigure(parent, new Vector3(0.18f, 0f, -0.07f), -9f, accent, false);
        }

        private void CreateMedicModel(Transform parent, Color accent)
        {
            CreateSoldierFigure(parent, new Vector3(-0.11f, 0f, 0.04f), 4f, accent, true);
            var canvas = new Color(0.68f, 0.64f, 0.51f);
            var medicalRed = new Color(0.68f, 0.16f, 0.12f);
            CreateStructureBlock(parent, "Grounded medical satchel", new Vector3(0.23f, 0.015f, -0.06f),
                new Vector3(0.25f, 0.22f, 0.18f), canvas);
            CreateStructureBlock(parent, "Medical cross vertical", new Vector3(0.23f, 0.075f, 0.036f),
                new Vector3(0.045f, 0.125f, 0.022f), medicalRed);
            CreateStructureBlock(parent, "Medical cross horizontal", new Vector3(0.23f, 0.112f, 0.037f),
                new Vector3(0.125f, 0.045f, 0.024f), medicalRed);
        }

        private void CreateSoldierFigure(Transform parent, Vector3 position, float yaw, Color accent, bool medic)
        {
            var figure = new GameObject(medic ? "Medic figure" : "Infantry figure");
            figure.transform.SetParent(parent, false);
            figure.transform.localPosition = position;
            figure.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var uniform = new Color(0.29f, 0.32f, 0.20f);
            var uniformDark = new Color(0.20f, 0.24f, 0.16f);
            var leather = new Color(0.29f, 0.20f, 0.12f);
            var skin = new Color(0.58f, 0.43f, 0.31f);
            var metal = new Color(0.15f, 0.17f, 0.15f);
            CreateStructureBlock(figure.transform, "Left boot", new Vector3(-0.055f, 0f, 0.018f),
                new Vector3(0.075f, 0.050f, 0.13f), leather);
            CreateStructureBlock(figure.transform, "Right boot", new Vector3(0.055f, 0f, 0.018f),
                new Vector3(0.075f, 0.050f, 0.13f), leather);
            CreateStructureBlock(figure.transform, "Left leg", new Vector3(-0.055f, 0.045f, 0f),
                new Vector3(0.072f, 0.22f, 0.085f), uniformDark);
            CreateStructureBlock(figure.transform, "Right leg", new Vector3(0.055f, 0.045f, 0f),
                new Vector3(0.072f, 0.22f, 0.085f), uniformDark);
            CreateStructureBlock(figure.transform, "Uniform torso", new Vector3(0f, 0.245f, 0f),
                new Vector3(0.21f, 0.27f, 0.15f), uniform);
            CreateStructureBlock(figure.transform, "Equipment belt", new Vector3(0f, 0.35f, 0.004f),
                new Vector3(0.225f, 0.048f, 0.158f), leather);
            CreateStructureBlock(figure.transform, "Left arm", new Vector3(-0.132f, 0.275f, 0.01f),
                new Vector3(0.055f, 0.22f, 0.070f), uniform);
            CreateStructureBlock(figure.transform, "Right arm", new Vector3(0.132f, 0.275f, 0.01f),
                new Vector3(0.055f, 0.22f, 0.070f), uniform);
            CreateStructureBlock(figure.transform, "Canvas pack", new Vector3(0f, 0.30f, -0.102f),
                new Vector3(0.16f, 0.19f, 0.075f), medic ? new Color(0.62f, 0.59f, 0.48f) : leather);
            CreateUnitMeshPart(figure.transform, "Low-poly head", new Vector3(0f, 0.585f, 0f),
                new Vector3(0.075f, 0.080f, 0.075f), skin, LowPolySphereMesh);
            CreateUnitMeshPart(figure.transform, "Steel helmet", new Vector3(0f, 0.653f, 0f),
                new Vector3(0.105f, 0.055f, 0.105f), medic ? new Color(0.56f, 0.57f, 0.48f) : uniformDark,
                LowPolySphereMesh);
            CreateStructureBlock(figure.transform, medic ? "Medic armband" : "Nation shoulder patch",
                new Vector3(-0.161f, 0.425f, 0.035f), new Vector3(0.025f, 0.075f, 0.060f),
                medic ? new Color(0.72f, 0.69f, 0.57f) : accent);
            if (medic) return;
            var rifle = CreateModelPart(PrimitiveType.Cube, figure.transform, new Vector3(0.14f, 0.405f, 0.085f),
                new Vector3(0.035f, 0.035f, 0.44f), metal);
            rifle.name = "Service rifle";
            rifle.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
            CreateStructureBlock(figure.transform, "Rifle stock", new Vector3(0.117f, 0.31f, -0.105f),
                new Vector3(0.075f, 0.070f, 0.16f), leather);
        }

        private void CreateLightArmorModel(Transform parent, Color accent)
        {
            var track = new Color(0.13f, 0.15f, 0.14f);
            var hull = new Color(0.31f, 0.34f, 0.22f);
            var hullLight = new Color(0.39f, 0.41f, 0.27f);
            var metal = new Color(0.18f, 0.20f, 0.18f);
            CreateStructureBlock(parent, "Left continuous track", new Vector3(-0.27f, 0.02f, 0f),
                new Vector3(0.18f, 0.18f, 0.72f), track);
            CreateStructureBlock(parent, "Right continuous track", new Vector3(0.27f, 0.02f, 0f),
                new Vector3(0.18f, 0.18f, 0.72f), track);
            CreateStructureBlock(parent, "Armour lower hull", new Vector3(0f, 0.10f, 0.01f),
                new Vector3(0.64f, 0.22f, 0.62f), hull);
            CreateStructureBlock(parent, "Armour sloped deck", new Vector3(0f, 0.29f, 0.02f),
                new Vector3(0.52f, 0.14f, 0.45f), hullLight);
            CreateStructureBlock(parent, "Armour turret", new Vector3(0f, 0.42f, 0.02f),
                new Vector3(0.35f, 0.16f, 0.30f), hull);
            var barrel = CreateModelPart(PrimitiveType.Cube, parent, new Vector3(0f, 0.515f, 0.38f),
                new Vector3(0.058f, 0.058f, 0.54f), metal);
            barrel.name = "Armour cannon barrel";
            var muzzle = CreateModelPart(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.515f, 0.66f),
                new Vector3(0.055f, 0.055f, 0.055f), metal);
            muzzle.name = "Armour muzzle brake";
            muzzle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateStructureBlock(parent, "Commander hatch", new Vector3(-0.055f, 0.58f, 0f),
                new Vector3(0.16f, 0.055f, 0.13f), metal);
            CreateStructureBlock(parent, "Nation glacis stripe", new Vector3(0f, 0.315f, 0.254f),
                new Vector3(0.30f, 0.075f, 0.026f), accent);
            CreateStructureBlock(parent, "Left track guard", new Vector3(-0.27f, 0.195f, 0f),
                new Vector3(0.20f, 0.055f, 0.73f), hullLight);
            CreateStructureBlock(parent, "Right track guard", new Vector3(0.27f, 0.195f, 0f),
                new Vector3(0.20f, 0.055f, 0.73f), hullLight);
        }

        private void CreateLightArtilleryModel(Transform parent, Color accent)
        {
            var wheelColor = new Color(0.13f, 0.15f, 0.14f);
            var carriage = new Color(0.30f, 0.33f, 0.21f);
            var carriageLight = new Color(0.39f, 0.42f, 0.28f);
            var metal = new Color(0.17f, 0.19f, 0.18f);
            var leftWheel = CreateModelPart(PrimitiveType.Cylinder, parent, new Vector3(-0.30f, 0.15f, 0.03f),
                new Vector3(0.28f, 0.060f, 0.28f), wheelColor);
            leftWheel.name = "Left artillery wheel";
            leftWheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var rightWheel = CreateModelPart(PrimitiveType.Cylinder, parent, new Vector3(0.30f, 0.15f, 0.03f),
                new Vector3(0.28f, 0.060f, 0.28f), wheelColor);
            rightWheel.name = "Right artillery wheel";
            rightWheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            CreateStructureBlock(parent, "Artillery axle", new Vector3(0f, 0.11f, 0.03f),
                new Vector3(0.68f, 0.08f, 0.10f), metal);
            CreateStructureBlock(parent, "Artillery shield", new Vector3(0f, 0.18f, 0.09f),
                new Vector3(0.54f, 0.28f, 0.065f), carriageLight);
            CreateStructureBlock(parent, "Artillery breech", new Vector3(0f, 0.25f, 0.13f),
                new Vector3(0.19f, 0.16f, 0.22f), carriage);
            var barrel = CreateModelPart(PrimitiveType.Cube, parent, new Vector3(0f, 0.44f, 0.40f),
                new Vector3(0.070f, 0.070f, 0.68f), metal);
            barrel.name = "Raised artillery barrel";
            barrel.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            CreateStructureBlock(parent, "Left artillery trail", new Vector3(-0.13f, 0.035f, -0.29f),
                new Vector3(0.09f, 0.09f, 0.56f), carriage);
            CreateStructureBlock(parent, "Right artillery trail", new Vector3(0.13f, 0.035f, -0.29f),
                new Vector3(0.09f, 0.09f, 0.56f), carriage);
            CreateStructureBlock(parent, "Artillery identification plate", new Vector3(0f, 0.29f, 0.126f),
                new Vector3(0.25f, 0.09f, 0.025f), accent);
        }

        private GameObject CreateUnitMeshPart(Transform parent, string label, Vector3 localPosition,
            Vector3 localScale, Color color, Mesh mesh)
        {
            var part = new GameObject(label);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            SetColor(part.AddComponent<MeshRenderer>(), color, true);
            return part;
        }

        private GameObject CreateModelPart(PrimitiveType primitive, Transform parent, Vector3 localPosition,
            Vector3 localScale, Color color)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            SetColor(part.GetComponent<Renderer>(), color, true);
            var collider = part.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            return part;
        }

        private void CreateCityModel(CityState city, Vector3 world, HexCoord coord)
        {
            var root = new GameObject($"City {city.Id}");
            root.transform.SetParent(transform, false);
            root.transform.position = world;
            var level = Mathf.Clamp(city.Level, 1, 3);
            root.transform.rotation = Quaternion.Euler(0f, -12f, 0f);
            root.transform.localScale = Vector3.one * StructureScaleForLevel(level);
            CreateCityBlocks(root.transform, level, city.NationId, city.IsDisabled);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.62f + level * 0.08f, 0f);
            collider.size = new Vector3(1.30f + level * 0.20f, 1.30f + level * 0.16f,
                1.20f + level * 0.18f);
            root.AddComponent<HexCellClickTarget>().Initialize(this, coord);
            Track(root);
        }

        private void CreateBuildingModel(GameState state, BuildingState building, Vector3 world, HexCoord coord)
        {
            var root = new GameObject($"Building {building.Id} {building.Type}");
            root.transform.SetParent(transform, false);
            root.transform.position = world;
            var occupied = state.Map.TryGet(coord, out var cell) && cell.UnitId.HasValue &&
                           state.Units.TryGetValue(cell.UnitId.Value, out var unit) &&
                           unit.Health > 0 && unit.NationId != building.NationId;
            var cityDisabled = !state.Cities.TryGetValue(building.CityId, out var city) || city.IsDisabled;
            var disabled = building.IsDisabled || occupied || cityDisabled;
            var level = Mathf.Clamp(building.Level, 1, 3);
            root.transform.rotation = Quaternion.Euler(0f,
                building.Type == BuildingType.MilitaryFactory ? 16f : -18f, 0f);
            root.transform.localScale = Vector3.one * StructureScaleForLevel(level);
            if (building.Type == BuildingType.MilitaryFactory)
                CreateFactoryBlocks(root.transform, level, building.NationId, disabled);
            else if (building.Type == BuildingType.CivilEnterprise)
                CreateEnterpriseBlocks(root.transform, level, building.NationId, disabled);
            else
                CreateResearchBlocks(root.transform, level, building.NationId, disabled);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.55f + level * 0.08f, 0f);
            collider.size = new Vector3(1.20f + level * 0.22f, 1.18f + level * 0.18f,
                1.12f + level * 0.20f);
            root.AddComponent<HexCellClickTarget>().Initialize(this, coord);
            Track(root);
        }

        private static float StructureScaleForLevel(int level)
        {
            // Lower levels leave more unused space inside their hex. Scale each
            // tier up to the largest size its foundation safely allows.
            return level <= 1 ? 1.18f : level == 2 ? 1.10f : 1.07f;
        }

        private void CreateCityBlocks(Transform parent, int level, int nationId, bool disabled)
        {
            var stone = StructureColor(new Color(0.72f, 0.66f, 0.51f), disabled);
            var paleStone = StructureColor(new Color(0.82f, 0.77f, 0.62f), disabled);
            var roof = StructureColor(new Color(0.46f, 0.20f, 0.14f), disabled);
            var darkRoof = StructureColor(new Color(0.26f, 0.27f, 0.25f), disabled);
            var accent = StructureColor(NationAccent(nationId), disabled);
            var foundation = StructureColor(new Color(0.48f, 0.44f, 0.34f), disabled);
            var window = StructureColor(new Color(0.20f, 0.30f, 0.31f), disabled);
            CreateHexFoundation(parent, "City hexagonal stone apron", level, foundation);

            if (level == 1)
            {
                CreateGabledBuilding(parent, "Town hall", new Vector3(-0.24f, 0.075f, 0.12f),
                    new Vector3(0.66f, 0.46f, 0.54f), 0.17f, paleStone, roof);
                CreateStructureBlock(parent, "Town door", new Vector3(-0.24f, 0.075f, -0.158f),
                    new Vector3(0.15f, 0.25f, 0.035f), darkRoof);
                CreateWindowRow(parent, "Town hall windows", -0.24f, 0.32f, -0.159f, 2, 0.20f,
                    new Vector2(0.105f, 0.095f), window);
                CreateGabledBuilding(parent, "Detached town annex", new Vector3(0.40f, 0.075f, 0.23f),
                    new Vector3(0.32f, 0.30f, 0.30f), 0.11f, stone, darkRoof);
                CreateStructureBlock(parent, "Town standard", new Vector3(0.37f, 0.075f, -0.215f),
                    new Vector3(0.10f, 0.18f, 0.025f), accent);
                return;
            }

            if (level == 2)
            {
                CreateGabledBuilding(parent, "Civic hall", new Vector3(-0.28f, 0.075f, 0.18f),
                    new Vector3(0.72f, 0.54f, 0.52f), 0.19f, paleStone, roof);
                CreateStructureBlock(parent, "Civic tower", new Vector3(0.40f, 0.075f, 0.22f),
                    new Vector3(0.28f, 0.80f, 0.28f), stone);
                CreatePyramidRoof(parent, "Civic tower roof", new Vector3(0.40f, 0.875f, 0.22f),
                    new Vector3(0.25f, 0.23f, 0.25f), darkRoof);
                CreateGabledBuilding(parent, "West residence", new Vector3(-0.48f, 0.075f, -0.34f),
                    new Vector3(0.34f, 0.32f, 0.34f), 0.12f, stone, darkRoof);
                CreateGabledBuilding(parent, "East residence", new Vector3(0.36f, 0.075f, -0.35f),
                    new Vector3(0.36f, 0.36f, 0.34f), 0.13f, stone, roof);
                CreateWindowRow(parent, "Civic hall windows", -0.28f, 0.36f, -0.084f, 3, 0.18f,
                    new Vector2(0.09f, 0.09f), window);
                CreateStructureBlock(parent, "Civic banner", new Vector3(0.40f, 0.48f, 0.073f),
                    new Vector3(0.13f, 0.17f, 0.025f), accent);
                return;
            }

            CreateGabledBuilding(parent, "Metropolitan hall", new Vector3(-0.04f, 0.075f, 0.24f),
                new Vector3(0.78f, 0.62f, 0.50f), 0.20f, paleStone, roof);
            CreateStructureBlock(parent, "Metropolitan cornice", new Vector3(-0.04f, 0.645f, 0.24f),
                new Vector3(0.84f, 0.075f, 0.56f), stone);
            CreateStructureBlock(parent, "Metropolitan tower", new Vector3(-0.62f, 0.075f, 0.29f),
                new Vector3(0.27f, 0.96f, 0.27f), stone);
            CreatePyramidRoof(parent, "Metropolitan tower roof", new Vector3(-0.62f, 1.035f, 0.29f),
                new Vector3(0.25f, 0.25f, 0.25f), darkRoof);
            CreateGabledBuilding(parent, "Metropolitan east wing", new Vector3(0.58f, 0.075f, 0.27f),
                new Vector3(0.28f, 0.44f, 0.38f), 0.13f, stone, darkRoof);
            CreateGabledBuilding(parent, "Southwest residence", new Vector3(-0.50f, 0.075f, -0.38f),
                new Vector3(0.36f, 0.37f, 0.36f), 0.13f, stone, darkRoof);
            CreateGabledBuilding(parent, "Southeast residence", new Vector3(0.45f, 0.075f, -0.39f),
                new Vector3(0.38f, 0.42f, 0.36f), 0.14f, paleStone, roof);
            CreateWindowRow(parent, "Metropolitan windows", -0.04f, 0.38f, -0.014f, 4, 0.17f,
                new Vector2(0.085f, 0.10f), window);
            CreateStructureBlock(parent, "Metropolitan entrance", new Vector3(-0.04f, 0.075f, -0.018f),
                new Vector3(0.16f, 0.28f, 0.035f), accent);
        }

        private void CreateEnterpriseBlocks(Transform parent, int level, int nationId, bool disabled)
        {
            var foundation = StructureColor(new Color(0.44f, 0.42f, 0.34f), disabled);
            var plaster = StructureColor(new Color(0.74f, 0.68f, 0.52f), disabled);
            var upper = StructureColor(new Color(0.66f, 0.57f, 0.40f), disabled);
            var roof = StructureColor(new Color(0.28f, 0.30f, 0.28f), disabled);
            var glass = StructureColor(new Color(0.25f, 0.43f, 0.47f), disabled);
            var accent = StructureColor(NationAccent(nationId), disabled);
            CreateHexFoundation(parent, "Enterprise hexagonal paved lot", level, foundation);
            var mainX = level == 1 ? -0.18f : -0.27f;
            var mainZ = level == 3 ? 0.15f : 0.11f;
            var mainSize = level == 1 ? new Vector3(0.70f, 0.50f, 0.54f) :
                level == 2 ? new Vector3(0.72f, 0.56f, 0.55f) : new Vector3(0.76f, 0.62f, 0.58f);
            CreateStructureBlock(parent, "Enterprise main office", new Vector3(mainX, 0.075f, mainZ),
                mainSize, plaster);
            var upperSize = new Vector3(mainSize.x - 0.16f, 0.20f + level * 0.06f, mainSize.z - 0.12f);
            CreateStructureBlock(parent, "Enterprise setback floor",
                new Vector3(mainX, 0.075f + mainSize.y, mainZ + 0.025f), upperSize, upper);
            CreateStructureBlock(parent, "Enterprise roof coping",
                new Vector3(mainX, 0.075f + mainSize.y + upperSize.y, mainZ + 0.025f),
                new Vector3(upperSize.x + 0.07f, 0.065f, upperSize.z + 0.07f), roof);
            var frontZ = mainZ - mainSize.z * 0.5f - 0.018f;
            CreateWindowRow(parent, "Enterprise shop windows", mainX - 0.06f, 0.16f, frontZ, level + 2,
                0.16f, new Vector2(0.095f, 0.18f), glass);
            CreateStructureBlock(parent, "Enterprise entrance",
                new Vector3(mainX + mainSize.x * 0.34f, 0.075f, frontZ - 0.002f),
                new Vector3(0.12f, 0.27f, 0.038f), accent);
            CreateStructureBlock(parent, "Enterprise awning",
                new Vector3(mainX, 0.32f, frontZ - 0.055f),
                new Vector3(mainSize.x * 0.82f, 0.055f, 0.14f), accent);

            if (level == 1)
            {
                CreateStructureBlock(parent, "Detached market kiosk", new Vector3(0.43f, 0.075f, -0.25f),
                    new Vector3(0.30f, 0.29f, 0.30f), upper);
                CreateStructureBlock(parent, "Kiosk roof", new Vector3(0.43f, 0.365f, -0.25f),
                    new Vector3(0.35f, 0.06f, 0.35f), roof);
                return;
            }

            var wingX = level == 2 ? 0.45f : 0.53f;
            var wingZ = level == 2 ? 0.16f : 0.19f;
            CreateStructureBlock(parent, "Detached enterprise wing", new Vector3(wingX, 0.075f, wingZ),
                new Vector3(level == 2 ? 0.40f : 0.36f, level == 2 ? 0.46f : 0.54f, 0.46f), plaster);
            CreateStructureBlock(parent, "Enterprise wing roof",
                new Vector3(wingX, level == 2 ? 0.535f : 0.615f, wingZ),
                new Vector3(level == 2 ? 0.45f : 0.41f, 0.06f, 0.51f), roof);
            if (level == 3)
            {
                CreateStructureBlock(parent, "Detached rear office", new Vector3(0.20f, 0.075f, -0.43f),
                    new Vector3(0.46f, 0.38f, 0.28f), upper);
                CreateWindowRow(parent, "Rear office windows", 0.20f, 0.25f, -0.576f, 2, 0.16f,
                    new Vector2(0.085f, 0.09f), glass);
            }
        }

        private void CreateFactoryBlocks(Transform parent, int level, int nationId, bool disabled)
        {
            var foundation = StructureColor(new Color(0.40f, 0.40f, 0.34f), disabled);
            var concrete = StructureColor(new Color(0.60f, 0.59f, 0.51f), disabled);
            var brick = StructureColor(new Color(0.46f, 0.27f, 0.20f), disabled);
            var roof = StructureColor(new Color(0.25f, 0.29f, 0.28f), disabled);
            var metal = StructureColor(new Color(0.32f, 0.39f, 0.39f), disabled);
            var accent = StructureColor(NationAccent(nationId), disabled);
            CreateHexFoundation(parent, "Factory hexagonal concrete yard", level, foundation);
            if (level == 1)
            {
                CreateFactoryHall(parent, "Small production hall", new Vector3(-0.18f, 0.075f, 0.10f),
                    new Vector3(0.76f, 0.46f, 0.58f), 0.17f, concrete, roof, metal, accent);
                CreateChimney(parent, "Factory chimney", new Vector3(0.47f, 0.075f, 0.29f),
                    0.09f, 0.68f, brick);
                return;
            }

            if (level == 2)
            {
                CreateFactoryHall(parent, "Primary production hall", new Vector3(-0.37f, 0.075f, 0.22f),
                    new Vector3(0.68f, 0.52f, 0.50f), 0.18f, concrete, roof, metal, accent);
                CreateFactoryHall(parent, "Brick assembly hall", new Vector3(0.38f, 0.075f, -0.23f),
                    new Vector3(0.56f, 0.43f, 0.42f), 0.15f, brick, roof, metal, accent);
                CreateChimney(parent, "Factory chimney", new Vector3(0.48f, 0.075f, 0.40f),
                    0.095f, 0.82f, brick);
                return;
            }

            CreateFactoryHall(parent, "West production hall", new Vector3(-0.56f, 0.075f, 0f),
                new Vector3(0.39f, 0.48f, 0.86f), 0.15f, brick, roof, metal, accent);
            CreateFactoryHall(parent, "Central production hall", new Vector3(0f, 0.075f, 0f),
                new Vector3(0.43f, 0.57f, 0.92f), 0.18f, concrete, roof, metal, accent);
            CreateFactoryHall(parent, "East production hall", new Vector3(0.58f, 0.075f, 0f),
                new Vector3(0.39f, 0.50f, 0.84f), 0.15f, brick, roof, metal, accent);
            CreateChimney(parent, "West factory chimney", new Vector3(-0.56f, 0.075f, 0.59f),
                0.085f, 0.88f, brick);
            CreateChimney(parent, "East factory chimney", new Vector3(0.58f, 0.075f, 0.58f),
                0.085f, 0.94f, concrete);
        }

        private void CreateResearchBlocks(Transform parent, int level, int nationId, bool disabled)
        {
            var foundation = StructureColor(new Color(0.44f, 0.43f, 0.36f), disabled);
            var pale = StructureColor(new Color(0.73f, 0.75f, 0.68f), disabled);
            var roof = StructureColor(new Color(0.25f, 0.31f, 0.35f), disabled);
            var accent = StructureColor(NationAccent(nationId), disabled);
            CreateHexFoundation(parent, "Institute hexagonal foundation", level, foundation);
            CreateStructureBlock(parent, "Institute laboratory", new Vector3(0f, 0.07f, 0.06f),
                new Vector3(0.80f + level * 0.18f, 0.48f + level * 0.14f, 0.64f + level * 0.08f), pale);
            CreatePyramidRoof(parent, "Institute observatory", new Vector3(0f, 0.55f + level * 0.14f, 0.06f),
                new Vector3(0.30f + level * 0.03f, 0.28f, 0.30f + level * 0.03f), roof);
            CreateStructureBlock(parent, "Institute entrance", new Vector3(0f, 0.07f, -0.30f - level * 0.04f),
                new Vector3(0.34f, 0.28f, 0.04f), accent);
        }

        private GameObject CreateStructureBlock(Transform parent, string label, Vector3 basePosition,
            Vector3 size, Color color)
        {
            var center = basePosition + Vector3.up * (size.y * 0.5f);
            var part = new GameObject(label);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = center;
            part.transform.localScale = size;
            part.AddComponent<MeshFilter>().sharedMesh = BeveledBoxMesh;
            SetColor(part.AddComponent<MeshRenderer>(), color, true);
            return part;
        }

        private GameObject CreateHexFoundation(Transform parent, string label, int level, Color color)
        {
            var radius = level == 1 ? 0.88f : level == 2 ? 0.98f : 1.03f;
            var height = level == 3 ? 0.085f : 0.075f;
            var foundation = new GameObject(label);
            foundation.transform.SetParent(parent, false);
            foundation.transform.localPosition = Vector3.up * (height * 0.5f);
            foundation.transform.localScale = new Vector3(radius, height, radius);
            foundation.AddComponent<MeshFilter>().sharedMesh = HexPrismMesh;
            SetColor(foundation.AddComponent<MeshRenderer>(), color, true);
            return foundation;
        }

        private void CreateGabledBuilding(Transform parent, string label, Vector3 basePosition, Vector3 bodySize,
            float roofHeight, Color wallColor, Color roofColor)
        {
            CreateStructureBlock(parent, label + " walls", basePosition, bodySize, wallColor);
            var roof = new GameObject(label + " roof");
            roof.transform.SetParent(parent, false);
            roof.transform.localPosition = basePosition + Vector3.up * bodySize.y;
            roof.transform.localScale = new Vector3(bodySize.x * 1.08f, roofHeight, bodySize.z * 1.12f);
            roof.AddComponent<MeshFilter>().sharedMesh = GableRoofMesh;
            SetColor(roof.AddComponent<MeshRenderer>(), roofColor, true);
        }

        private void CreatePyramidRoof(Transform parent, string label, Vector3 basePosition, Vector3 size,
            Color color)
        {
            var roof = new GameObject(label);
            roof.transform.SetParent(parent, false);
            roof.transform.localPosition = basePosition;
            roof.transform.localScale = size;
            roof.AddComponent<MeshFilter>().sharedMesh = ConeMesh;
            SetColor(roof.AddComponent<MeshRenderer>(), color, true);
        }

        private void CreateChimney(Transform parent, string label, Vector3 basePosition, float radius,
            float height, Color color)
        {
            var chimney = CreateModelPart(PrimitiveType.Cylinder, parent,
                basePosition + Vector3.up * (height * 0.5f),
                new Vector3(radius, height * 0.5f, radius), color);
            chimney.name = label;
            CreateStructureBlock(parent, label + " cap",
                basePosition + Vector3.up * height - new Vector3(0f, 0.02f, 0f),
                new Vector3(radius * 2.35f, 0.07f, radius * 2.35f), color);
        }

        private void CreateWindowRow(Transform parent, string label, float centerX, float baseY, float frontZ,
            int count, float spacing, Vector2 size, Color color)
        {
            var start = centerX - spacing * (count - 1) * 0.5f;
            for (var i = 0; i < count; i++)
            {
                CreateStructureBlock(parent, $"{label} {i + 1}",
                    new Vector3(start + i * spacing, baseY, frontZ),
                    new Vector3(size.x, size.y, 0.035f), color);
            }
        }

        private void CreateFactoryHall(Transform parent, string label, Vector3 basePosition, Vector3 bodySize,
            float roofHeight, Color wallColor, Color roofColor, Color doorColor, Color accentColor)
        {
            CreateGabledBuilding(parent, label, basePosition, bodySize, roofHeight, wallColor, roofColor);
            var frontZ = basePosition.z - bodySize.z * 0.5f - 0.018f;
            CreateStructureBlock(parent, label + " loading door",
                new Vector3(basePosition.x, basePosition.y, frontZ),
                new Vector3(bodySize.x * 0.42f, Mathf.Min(0.31f, bodySize.y * 0.66f), 0.038f), doorColor);
            CreateWindowRow(parent, label + " clerestory", basePosition.x,
                basePosition.y + bodySize.y * 0.69f, frontZ - 0.002f, 2, bodySize.x * 0.26f,
                new Vector2(bodySize.x * 0.13f, 0.075f), accentColor);
        }

        private static Color NationAccent(int nationId)
        {
            return nationId == 1 ? new Color(0.16f, 0.42f, 0.62f) : new Color(0.64f, 0.24f, 0.17f);
        }

        private static Color StructureColor(Color color, bool disabled)
        {
            return disabled ? Color.Lerp(color, new Color(0.45f, 0.45f, 0.41f), 0.68f) : color;
        }

        private void CreateRoad(HexCoord from, HexCoord to)
        {
            var start = ToWorld(from) + Vector3.up * WorldPresentation.RoadShadowY;
            var end = ToWorld(to) + Vector3.up * WorldPresentation.RoadShadowY;
            var side = Vector3.Cross(Vector3.up, end - start).normalized;
            // ArtV6 roads are graphic components rather than miniature terrain.
            // Exact center-to-center geometry guarantees all six edge sockets meet;
            // two offset color shapes provide the hand-painted value break without
            // a dark outline, gravel texture or realistic wheel ruts.
            CreateRoadLayer(start + side * 0.030f, end + side * 0.030f, 0.245f, 0.008f,
                new Color(0.58f, 0.43f, 0.24f), "Road shadow shape");
            var paintLift = WorldPresentation.RoadPaintY - WorldPresentation.RoadShadowY;
            CreateRoadLayer(start - side * 0.012f + Vector3.up * paintLift,
                end - side * 0.012f + Vector3.up * paintLift, 0.205f, 0.006f,
                new Color(0.78f, 0.64f, 0.40f), "Road paint shape");
        }

        private void CreateRoadLayer(Vector3 start, Vector3 end, float width, float height, Color color, string label)
        {
            var road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = label;
            road.transform.SetParent(transform, false);
            road.transform.position = (start + end) * 0.5f;
            road.transform.localScale = new Vector3(width, height, Vector3.Distance(start, end));
            road.transform.rotation = Quaternion.LookRotation(end - start, Vector3.up);
            SetColor(road.GetComponent<Renderer>(), color);
            var collider = road.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            Track(road);
        }

        private void CreateRoadHub(GameState state, HexCell cell)
        {
            if (HasBuildingFootprint(state, cell.Coord)) return;
            var world = ToWorld(cell.Coord);
            CreateRoadHubLayer(world + new Vector3(0.025f, WorldPresentation.RoadShadowY, -0.018f),
                0.245f, 0.008f,
                new Color(0.58f, 0.43f, 0.24f), "Road junction shadow shape");
            CreateRoadHubLayer(world + new Vector3(-0.010f, WorldPresentation.RoadPaintY, 0.008f),
                0.205f, 0.006f,
                new Color(0.78f, 0.64f, 0.40f), "Road junction paint shape");
        }

        private static bool HasBuildingFootprint(GameState state, HexCoord coord)
        {
            foreach (var city in state.Cities.Values)
                if (city.Center.Equals(coord)) return true;
            return state.Map.TryGet(coord, out var cell) && cell.BuildingId.HasValue;
        }

        private void CreateRoadHubLayer(Vector3 position, float diameter, float height, Color color, string label)
        {
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = label;
            hub.transform.SetParent(transform, false);
            hub.transform.position = position;
            hub.transform.localScale = new Vector3(diameter, height, diameter);
            SetColor(hub.GetComponent<Renderer>(), color);
            var collider = hub.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            Track(hub);
        }

        private void CreateTerrainFeature(HexCell cell)
        {
            if (cell.Terrain == TerrainType.Plain) return;
            var root = new GameObject($"{cell.Terrain} Terrain {cell.Coord}");
            root.transform.SetParent(transform, false);
            root.transform.position = ToWorld(cell.Coord);
            var variant = Mathf.Abs(cell.Coord.Q * 19 + cell.Coord.R * 31) % 3;
            root.transform.rotation = Quaternion.Euler(0f, variant * 17f - 17f, 0f);

            switch (cell.Terrain)
            {
                case TerrainType.Forest:
                    CreateTree(root.transform, new Vector3(-0.48f, 0f, 0.27f), 0.90f,
                        new Color(0.12f, 0.25f, 0.16f));
                    CreateTree(root.transform, new Vector3(0.32f, 0f, 0.38f), 0.82f,
                        new Color(0.17f, 0.34f, 0.20f));
                    CreateTree(root.transform, new Vector3(-0.26f, 0f, -0.42f), 0.76f,
                        new Color(0.20f, 0.38f, 0.22f));
                    CreateTree(root.transform, new Vector3(0.48f, 0f, -0.27f), 0.84f,
                        new Color(0.14f, 0.29f, 0.18f));
                    CreateTree(root.transform, new Vector3(0f, 0f, -0.01f), 0.58f,
                        new Color(0.18f, 0.35f, 0.20f));
                    break;
                case TerrainType.Hill:
                    CreateTerrainCustomMesh(root.transform, new Vector3(-0.03f, 0.004f, 0.03f),
                        new Vector3(1.04f, 0.35f, 0.89f), new Color(0.48f, 0.39f, 0.24f), "Sculpted Hill",
                        MoundMesh);
                    CreateTerrainPart(PrimitiveType.Cube, root.transform, new Vector3(-0.35f, 0.0375f, -0.24f),
                        new Vector3(0.18f, 0.075f, 0.13f), new Color(0.62f, 0.53f, 0.34f), "Embedded Hill Stone");
                    break;
                case TerrainType.Mountain:
                    CreateMountain(root.transform, new Vector3(-0.06f, 0f, 0.03f), 0.92f,
                        new Color(0.31f, 0.34f, 0.34f));
                    break;
                case TerrainType.Marsh:
                    CreateTerrainCustomMesh(root.transform, new Vector3(-0.20f, 0.018f, 0.09f),
                        new Vector3(0.72f, 1f, 0.48f), new Color(0.14f, 0.34f, 0.34f), "Irregular Marsh Pool",
                        IrregularDiscMesh, false);
                    CreateTerrainCustomMesh(root.transform, new Vector3(0.43f, 0.020f, -0.28f),
                        new Vector3(0.36f, 1f, 0.25f), new Color(0.24f, 0.46f, 0.42f), "Small Marsh Pool",
                        IrregularDiscMesh, false);
                    CreateReedCluster(root.transform, new Vector3(-0.47f, 0f, -0.30f));
                    CreateReedCluster(root.transform, new Vector3(0.46f, 0f, 0.26f));
                    break;
            }
            Track(root);
        }

        private void CreateTree(Transform parent, Vector3 position, float scale, Color foliage)
        {
            CreateTerrainPart(PrimitiveType.Cylinder, parent, position + Vector3.up * (0.125f * scale),
                new Vector3(0.050f, 0.125f, 0.050f) * scale, new Color(0.25f, 0.18f, 0.11f), "Grounded Tree Trunk");
            CreateTerrainCustomMesh(parent, position + Vector3.up * (0.15f * scale),
                new Vector3(0.60f, 0.72f, 0.60f) * scale, foliage, "Layered Conifer Crown",
                ConiferCrownMesh);
        }

        private void CreateMountain(Transform parent, Vector3 position, float scale, Color rock)
        {
            CreateTerrainCustomMesh(parent, position + Vector3.up * 0.004f,
                new Vector3(1.06f, 1.00f, 0.93f) * scale, rock, "Faceted Mountain Mass",
                MountainMesh);
            CreateTerrainPart(PrimitiveType.Cube, parent,
                position + new Vector3(-0.36f, 0.050f, -0.23f) * scale,
                new Vector3(0.25f, 0.10f, 0.18f) * scale, Color.Lerp(rock, Color.white, 0.14f),
                "Mountain Base Boulder");
        }

        private void CreateReedCluster(Transform parent, Vector3 position)
        {
            var reed = new Color(0.55f, 0.49f, 0.25f);
            for (var i = 0; i < 3; i++)
            {
                CreateTerrainPart(PrimitiveType.Cylinder, parent,
                    position + new Vector3((i - 1) * 0.07f, 0.11f + i * 0.018f, (i & 1) * 0.035f),
                    new Vector3(0.018f, 0.11f + i * 0.018f, 0.018f), reed, "Marsh Reed");
            }
        }

        private GameObject CreateTerrainPart(PrimitiveType primitive, Transform parent, Vector3 localPosition,
            Vector3 localScale, Color color, string label)
        {
            var part = CreateModelPart(primitive, parent, localPosition, localScale, color);
            part.name = label;
            return part;
        }

        private GameObject CreateTerrainCustomMesh(Transform parent, Vector3 localPosition, Vector3 localScale,
            Color color, string label, Mesh mesh, bool lit = true)
        {
            var part = new GameObject(label);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            SetColor(renderer, color, lit);
            return part;
        }

        private void CreateZoneBoundary(HexMap map, HexCoord center, int radius, Color color, string label,
            float thickness, float height)
        {
            foreach (var pair in map.Cells)
            {
                if (center.DistanceTo(pair.Key) > radius) continue;
                for (var direction = 0; direction < 6; direction++)
                {
                    var neighbor = pair.Key.Neighbor(direction);
                    if (center.DistanceTo(neighbor) <= radius) continue;
                    CreateBoundaryEdge(pair.Key, neighbor, color, label, thickness, height);
                }
            }
        }

        private void CreateBoundaryEdge(HexCoord from, HexCoord outside, Color color, string label,
            float thickness, float height)
        {
            var start = ToWorld(from);
            var delta = ToWorld(outside) - start;
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = label;
            edge.transform.SetParent(transform, false);
            edge.transform.position = start + delta * 0.5f + Vector3.up * height;
            var tangent = new Vector3(-delta.z, 0f, delta.x).normalized;
            edge.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
            edge.transform.localScale = new Vector3(thickness, 0.045f, 1.08f);
            SetColor(edge.GetComponent<Renderer>(), color);
            var collider = edge.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            Track(edge);
        }

        private void CreateSetBoundary(HexMap map, HashSet<HexCoord> cells, Color color, string label,
            float thickness, float height)
        {
            foreach (var coord in cells)
            {
                for (var direction = 0; direction < 6; direction++)
                {
                    var neighbor = coord.Neighbor(direction);
                    if (cells.Contains(neighbor)) continue;
                    CreateBoundaryEdge(coord, neighbor, color, label, thickness, height);
                }
            }
        }

        private void CreateSupplyPath(IReadOnlyList<HexCoord> path, int tier)
        {
            var color = tier == 0 ? new Color(0.18f, 0.88f, 0.78f) : new Color(0.96f, 0.48f, 0.12f);
            for (var i = 1; i < path.Count; i++)
            {
                var start = ToWorld(path[i - 1]) + Vector3.up * 0.24f;
                var end = ToWorld(path[i]) + Vector3.up * 0.24f;
                CreateSupplyPathSegment(start, end, new Color(0.025f, 0.06f, 0.07f), 0.15f,
                    "Active Supply Line Shadow", false, i);
                CreateSupplyPathSegment(start + Vector3.up * 0.025f, end + Vector3.up * 0.025f, color, 0.072f,
                    "Active Supply Line", true, i);
            }
        }

        private void CreateSupplyPathSegment(Vector3 start, Vector3 end, Color color, float width,
            string label, bool animated, int phase)
        {
                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = label;
                segment.transform.SetParent(transform, false);
                segment.transform.position = (start + end) * 0.5f;
                segment.transform.localScale = new Vector3(width, 0.055f, Vector3.Distance(start, end));
                segment.transform.rotation = Quaternion.LookRotation(end - start, Vector3.up);
                SetColor(segment.GetComponent<Renderer>(), color);
                var collider = segment.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
                if (animated) segment.AddComponent<SelectionPulseEffect>().Initialize(phase * 0.45f);
                Track(segment);
        }

        private void CreateCityFlag(CityState city, Vector3 world, HexCoord coord)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = $"City Flag Pole {city.Id}";
            pole.transform.SetParent(transform, false);
            pole.transform.position = world + new Vector3(0.28f, 1.05f, 0f);
            pole.transform.localScale = new Vector3(0.035f, 0.52f, 0.035f);
            SetColor(pole.GetComponent<Renderer>(), new Color(0.18f, 0.18f, 0.20f));
            var poleCollider = pole.GetComponent<Collider>();
            if (poleCollider != null) poleCollider.enabled = false;
            Track(pole);

            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = $"City Owner Flag {city.Id}";
            flag.transform.SetParent(transform, false);
            flag.transform.position = world + new Vector3(0.56f, 1.37f, 0f);
            flag.transform.localScale = new Vector3(0.58f, 0.28f, 0.06f);
            var flagColor = city.IsDisabled ? new Color(1f, 0.72f, 0.14f) : city.NationId == 1
                ? new Color(0.05f, 0.42f, 1f)
                : new Color(0.95f, 0.08f, 0.05f);
            SetColor(flag.GetComponent<Renderer>(), flagColor);
            flag.AddComponent<HexCellClickTarget>().Initialize(this, coord);
            Track(flag);
        }

        private void CreateReadyBeacon(UnitState unit, Vector3 world)
        {
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beacon.name = $"Ready Beacon #{unit.Id}";
            beacon.transform.SetParent(transform, false);
            beacon.transform.position = world + Vector3.up * 1.40f;
            beacon.transform.rotation = Quaternion.Euler(0f, 45f, 45f);
            beacon.transform.localScale = Vector3.one * 0.20f;
            SetColor(beacon.GetComponent<Renderer>(), new Color(0.10f, 1f, 0.90f));
            beacon.AddComponent<HexCellClickTarget>().Initialize(this, unit.Position);
            beacon.AddComponent<SelectionPulseEffect>().Initialize(unit.Id * 0.62f);
            Track(beacon);
        }

        private void CreateWallMarker(GameState state, CityWallState wall)
        {
            var root = new GameObject();
            root.name = $"City Edge Wall {wall.Id}";
            root.transform.SetParent(transform, false);
            var innerWorld = ToWorld(wall.InnerPosition);
            var visual = new WallVisual { Root = root };
            if (state.Cities.TryGetValue(wall.CityId, out var city))
            {
                for (var direction = 0; direction < 6; direction++)
                {
                    var outside = wall.InnerPosition.Neighbor(direction);
                    if (!state.Map.TryGet(outside, out _) || city.Center.DistanceTo(outside) <= city.Level) continue;
                    var outerWorld = ToWorld(outside);
                    var delta = outerWorld - innerWorld;
                    var tangent = new Vector3(-delta.z, 0f, delta.x).normalized;
                    var faceRoot = new GameObject();
                    faceRoot.name = $"Wall Face {direction + 1}";
                    faceRoot.transform.SetParent(root.transform, false);
                    faceRoot.transform.position = (innerWorld + outerWorld) * 0.5f;
                    faceRoot.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
                    var foundation = CreateStructureBlock(faceRoot.transform, "Wall continuous footing", Vector3.zero,
                        new Vector3(0.36f, 0.075f, 1.24f), new Color(0.31f, 0.30f, 0.25f));
                    visual.Faces.Add(new WallFaceVisual
                    {
                        Root = faceRoot,
                        RootRenderer = foundation.GetComponent<Renderer>()
                    });
                }
            }
            _wallVisuals[wall.Id] = visual;
            Track(root);
        }

        private void UpdateWallMarker(GameState state, CityWallState wall, bool attackable)
        {
            if (!_wallVisuals.TryGetValue(wall.Id, out var visual) || visual.Root == null) return;
            var ratio = wall.MaxHealth <= 0 ? 0f : wall.Health / (float)wall.MaxHealth;
            var stage = wall.Health <= 0 ? 3 : ratio <= 0.34f ? 2 : ratio <= 0.67f ? 1 : 0;
            foreach (var face in visual.Faces)
            {
                if (face.Root == null) continue;
                EnsureWallSegments(face);
                ApplyWallStage(face, stage, attackable);
            }
        }

        public void ApplyDiagnosticWallStages()
        {
            var faceIndex = 0;
            foreach (var visual in _wallVisuals.Values)
            foreach (var face in visual.Faces)
            {
                EnsureWallSegments(face);
                ApplyWallStage(face, faceIndex % 4, false);
                faceIndex++;
            }
        }

        private void ApplyWallStage(WallFaceVisual face, int stage, bool attackable)
        {
            var footingColor = attackable
                ? new Color(0.72f, 0.38f, 0.10f)
                : stage >= 2 ? new Color(0.24f, 0.23f, 0.20f) : new Color(0.31f, 0.30f, 0.25f);
            SetColor(face.RootRenderer, footingColor, true);
            for (var i = 0; i < face.Segments.Count; i++)
            {
                var segment = face.Segments[i];
                if (segment == null) continue;
                segment.SetActive(true);
                segment.transform.localPosition = new Vector3(0f, 0.075f, -0.48f + i * 0.24f);
                segment.transform.localRotation = Quaternion.identity;
                segment.transform.localScale = Vector3.one;
                SetWallChildActive(segment, "Crenellation", true);
                if (stage == 1)
                {
                    if (i == 3)
                    {
                        segment.transform.localScale = new Vector3(1f, 0.72f, 1f);
                        SetWallChildActive(segment, "Crenellation", false);
                    }
                    else if (i == 2) SetWallChildActive(segment, "Crenellation", false);
                }
                else if (stage == 2)
                {
                    if (i == 2)
                    {
                        segment.SetActive(false);
                    }
                    else if (i == 1 || i == 3)
                    {
                        segment.transform.localScale = new Vector3(1f, i == 1 ? 0.72f : 0.58f, 1f);
                        SetWallChildActive(segment, "Crenellation", false);
                    }
                }
                else if (stage == 3)
                {
                    if (i >= 1 && i <= 3)
                    {
                        segment.SetActive(false);
                    }
                    else
                    {
                        segment.transform.localScale = new Vector3(1f, 0.56f, 1f);
                        SetWallChildActive(segment, "Crenellation", false);
                    }
                }
                SetWallSegmentColor(segment, stage);
            }

            var visibleRubble = stage == 1 ? 1 : stage == 2 ? 3 : stage == 3 ? face.Rubble.Count : 0;
            for (var i = 0; i < face.Rubble.Count; i++)
                if (face.Rubble[i] != null) face.Rubble[i].SetActive(i < visibleRubble);
        }

        private void EnsureWallSegments(WallFaceVisual visual)
        {
            if (visual.Segments.Count > 0) return;
            for (var i = 0; i < 5; i++)
            {
                var segment = new GameObject($"Stone wall module {i + 1}");
                segment.name = $"Wall Segment {i + 1}";
                segment.transform.SetParent(visual.Root.transform, false);
                CreateStructureBlock(segment.transform, "Dressed stone block", Vector3.zero,
                    new Vector3(0.29f, 0.36f, 0.24f), new Color(0.58f, 0.56f, 0.48f));
                CreateStructureBlock(segment.transform, "Stone course", new Vector3(0f, 0.17f, 0f),
                    new Vector3(0.305f, 0.035f, 0.238f), new Color(0.66f, 0.63f, 0.53f));
                CreateStructureBlock(segment.transform, "Wall coping", new Vector3(0f, 0.36f, 0f),
                    new Vector3(0.32f, 0.075f, 0.245f), new Color(0.70f, 0.67f, 0.56f));
                CreateStructureBlock(segment.transform, "Crenellation", new Vector3(0f, 0.435f, 0f),
                    new Vector3(0.30f, 0.13f, 0.13f), new Color(0.67f, 0.64f, 0.54f));
                visual.Segments.Add(segment);
            }

            var rubbleOffsets = new[]
            {
                new Vector3(-0.035f, 0.075f, 0.03f), new Vector3(0.045f, 0.075f, -0.13f),
                new Vector3(-0.055f, 0.075f, 0.17f), new Vector3(0.035f, 0.075f, -0.30f),
                new Vector3(-0.025f, 0.075f, 0.32f), new Vector3(0.060f, 0.075f, -0.02f)
            };
            for (var i = 0; i < rubbleOffsets.Length; i++)
            {
                var size = new Vector3(0.13f + (i % 2) * 0.035f, 0.055f + (i % 3) * 0.018f,
                    0.11f + ((i + 1) % 3) * 0.025f);
                var rubble = CreateStructureBlock(visual.Root.transform, $"Grounded wall rubble {i + 1}",
                    rubbleOffsets[i], size, new Color(0.43f, 0.42f, 0.37f));
                rubble.transform.localRotation = Quaternion.Euler(0f, i * 29f - 37f, 0f);
                rubble.SetActive(false);
                visual.Rubble.Add(rubble);
            }
        }

        private static void SetWallChildActive(GameObject segment, string childName, bool active)
        {
            var child = segment.transform.Find(childName);
            if (child != null) child.gameObject.SetActive(active);
        }

        private void SetWallSegmentColor(GameObject segment, int stage)
        {
            var body = stage == 0 ? new Color(0.58f, 0.56f, 0.48f) : stage == 1
                ? new Color(0.50f, 0.48f, 0.42f) : stage == 2
                    ? new Color(0.41f, 0.40f, 0.35f) : new Color(0.31f, 0.31f, 0.28f);
            var cap = Color.Lerp(body, new Color(0.78f, 0.74f, 0.62f), stage == 0 ? 0.45f : 0.22f);
            foreach (var renderer in segment.GetComponentsInChildren<Renderer>(true))
            {
                var color = renderer.gameObject.name == "Dressed stone block" ? body : cap;
                if (renderer.gameObject.name == "Stone course") color = Color.Lerp(body, cap, 0.48f);
                SetColor(renderer, color, true);
            }
        }

        private void CalculateOverview(GameState state)
        {
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            foreach (var pair in state.Map.Cells)
            {
                var world = ToWorld(pair.Key);
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minZ = Mathf.Min(minZ, world.z);
                maxZ = Mathf.Max(maxZ, world.z);
            }
            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            var cameraUp = WorldPresentation.CameraRotation * Vector3.up;
            var cameraRight = WorldPresentation.CameraRight;
            var cameraForward = WorldPresentation.CameraForward;
            var aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;
            var usableAspect = Mathf.Max(0.80f, aspect * 0.78f);
            var halfFovTangent = Mathf.Tan(WorldPresentation.CameraFieldOfView * 0.5f * Mathf.Deg2Rad);
            const float compositionOffset = 1.2f;
            var aim = center - cameraRight * compositionOffset;
            var requiredDistance = 0f;
            foreach (var cell in state.Map.Cells.Values)
            {
                var cellCenter = ToWorld(cell.Coord);
                for (var corner = 0; corner < 6; corner++)
                {
                    var relative = cellCenter + HexCorner(corner, 1.12f) - aim;
                    var forward = Vector3.Dot(relative, cameraForward);
                    var horizontal = Mathf.Abs(Vector3.Dot(relative, cameraRight));
                    var vertical = Mathf.Abs(Vector3.Dot(relative, cameraUp));
                    requiredDistance = Mathf.Max(requiredDistance,
                        vertical / halfFovTangent - forward,
                        horizontal / (halfFovTangent * usableAspect) - forward);
                }
            }
            _overviewSize = Mathf.Clamp((requiredDistance + 1.8f) * halfFovTangent, 8f, 32f);
            _overviewPosition = WorldPresentation.CameraPositionForTarget(center, _overviewSize,
                compositionOffset);
        }

        private void CreateBackdrop(GameState state)
        {
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            foreach (var cell in state.Map.Cells.Values)
            {
                var world = ToWorld(cell.Coord);
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minZ = Mathf.Min(minZ, world.z);
                maxZ = Mathf.Max(maxZ, world.z);
            }

            var backdrop = new GameObject("Map Backdrop");
            backdrop.name = "Map Backdrop";
            backdrop.transform.SetParent(transform, false);
            var boardMesh = CreateBoardBaseMesh(state.Map, 0.20f, 0.72f);
            backdrop.AddComponent<MeshFilter>().sharedMesh = boardMesh;
            var renderer = backdrop.AddComponent<MeshRenderer>();
            SetColor(renderer, new Color(0.13f, 0.14f, 0.08f), true);
            var collider = backdrop.AddComponent<MeshCollider>();
            collider.sharedMesh = boardMesh;
            backdrop.AddComponent<MapBackdropClickTarget>().Initialize(this);
            Track(backdrop);

        }

        private static Mesh CreateBoardBaseMesh(HexMap map, float rim, float thickness)
        {
            var points = new List<Vector2>();
            foreach (var cell in map.Cells.Values)
            {
                var center = ToWorld(cell.Coord);
                for (var corner = 0; corner < 6; corner++)
                {
                    var point = center + HexCorner(corner, 1.12f);
                    points.Add(new Vector2(point.x, point.z));
                }
            }
            points.Sort((left, right) =>
            {
                var x = left.x.CompareTo(right.x);
                return x != 0 ? x : left.y.CompareTo(right.y);
            });

            var hull = new List<Vector2>();
            foreach (var point in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            var lowerCount = hull.Count;
            for (var i = points.Count - 2; i >= 0; i--)
            {
                var point = points[i];
                while (hull.Count > lowerCount &&
                       Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);

            var centerPoint = Vector2.zero;
            foreach (var point in hull) centerPoint += point;
            centerPoint /= Mathf.Max(1, hull.Count);
            for (var i = 0; i < hull.Count; i++)
                hull[i] += (hull[i] - centerPoint).normalized * rim;

            var count = hull.Count;
            var vertices = new Vector3[count * 2];
            for (var i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(hull[i].x, -0.012f, hull[i].y);
                vertices[i + count] = new Vector3(hull[i].x, -thickness, hull[i].y);
            }
            var triangles = new List<int>();
            for (var i = 1; i < count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i + 1);
                triangles.Add(i);
                triangles.Add(count);
                triangles.Add(count + i);
                triangles.Add(count + i + 1);
            }
            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                triangles.Add(i);
                triangles.Add(next + count);
                triangles.Add(i + count);
                triangles.Add(i);
                triangles.Add(next);
                triangles.Add(next + count);
            }

            var mesh = new Mesh { name = "Physical hex board base" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Cross(Vector2 origin, Vector2 a, Vector2 b)
        {
            return (a.x - origin.x) * (b.y - origin.y) - (a.y - origin.y) * (b.x - origin.x);
        }

        private void Track(GameObject item)
        {
            if (_trackingStatic) _staticSpawned.Add(item);
            else _spawned.Add(item);
        }

        private void ClearDynamic()
        {
            foreach (var item in _spawned)
            {
                if (item != null)
                {
                    item.SetActive(false);
                    Destroy(item);
                }
            }
            _spawned.Clear();
            _unitMarkers.Clear();
            _hovered = null;
        }

        private void ClearAll()
        {
            ClearDynamic();
            foreach (var item in _staticSpawned)
            {
                if (item != null)
                {
                    item.SetActive(false);
                    Destroy(item);
                }
            }
            _staticSpawned.Clear();
            _tileRenderers.Clear();
            _tileColors.Clear();
            _wallVisuals.Clear();
            _builtMap = null;
        }

        public static Vector3 ToWorld(HexCoord coord)
        {
            const float radius = 1.12f;
            var x = radius * 1.5f * coord.Q;
            var z = radius * Mathf.Sqrt(3f) * (coord.R + coord.Q * 0.5f);
            return new Vector3(x, 0f, z);
        }

        public static Vector3 WallWorldPosition(CityWallState wall)
        {
            return ToWorld(wall.InnerPosition);
        }

        private static Color CellColor(GameState state, HexCell cell, int viewingNationId, HexCoord? selected,
            HashSet<HexCoord> legalMoves, HashSet<HexCoord> legalTargets,
            HashSet<HexCoord> supplyReach, HashSet<HexCoord> enemyControlReach,
            HashSet<HexCoord> legalSupportTargets, HashSet<HexCoord> legalWallTargets)
        {
            var terrain = GroundColor(cell);
            foreach (var city in state.Cities.Values)
            {
                if (city.Center.DistanceTo(cell.Coord) > city.Level) continue;
                terrain = city.IsDisabled
                    ? Color.Lerp(terrain, new Color(0.78f, 0.52f, 0.18f), 0.19f)
                    : city.NationId == 1
                        ? Color.Lerp(terrain, new Color(0.10f, 0.40f, 0.68f), 0.21f)
                        : Color.Lerp(terrain, new Color(0.68f, 0.18f, 0.12f), 0.21f);
                break;
            }
            var variation = ((cell.Coord.Q * 17 + cell.Coord.R * 31) & 3) * 0.008f - 0.012f;
            terrain = new Color(Mathf.Clamp01(terrain.r + variation), Mathf.Clamp01(terrain.g + variation),
                Mathf.Clamp01(terrain.b + variation), terrain.a);
            if (!selected.HasValue) return terrain;

            // Three independent whole-cell fields and their harmonic intersections.
            var supplied = supplyReach != null && supplyReach.Contains(cell.Coord);
            var movable = legalMoves != null && legalMoves.Contains(cell.Coord);
            var controlled = enemyControlReach != null && enemyControlReach.Contains(cell.Coord);
            var rangeMask = (supplied ? 1 : 0) | (movable ? 2 : 0) | (controlled ? 4 : 0);
            var fieldColor = rangeMask switch
            {
                1 => new Color(0.18f, 0.70f, 0.34f), // supply
                2 => new Color(0.10f, 0.47f, 0.96f), // movement
                3 => new Color(0.04f, 0.80f, 0.75f), // supply + movement
                4 => new Color(0.94f, 0.32f, 0.18f), // enemy control
                5 => new Color(0.82f, 0.68f, 0.18f), // supply + control
                6 => new Color(0.67f, 0.36f, 0.88f), // movement + control
                7 => new Color(0.91f, 0.55f, 0.62f), // all three
                _ => new Color(0.10f, 0.13f, 0.16f)
            };
            terrain = Color.Lerp(terrain, fieldColor, rangeMask == 0 ? 0.48f : 0.82f);

            if (selected.Value.Equals(cell.Coord)) return Color.Lerp(terrain, new Color(1f, 0.82f, 0.20f), 0.70f);
            if (legalTargets != null && legalTargets.Contains(cell.Coord))
                return Color.Lerp(terrain, new Color(0.92f, 0.16f, 0.12f), 0.76f);
            if (legalSupportTargets != null && legalSupportTargets.Contains(cell.Coord))
                return Color.Lerp(terrain, new Color(0.18f, 0.92f, 0.54f), 0.70f);
            if (legalWallTargets != null && legalWallTargets.Contains(cell.Coord))
                return Color.Lerp(terrain, new Color(1f, 0.47f, 0.08f), 0.76f);
            if (legalMoves != null && legalMoves.Contains(cell.Coord))
            {
                if (cell.CityId.HasValue && state.Cities.TryGetValue(cell.CityId.Value, out var targetCity) &&
                    targetCity.NationId != viewingNationId)
                {
                    return Color.Lerp(terrain, new Color(1f, 0.64f, 0.12f), 0.72f);
                }
                return terrain;
            }
            return terrain;
        }

        private static Color TerrainColor(TerrainType terrain, int owner)
        {
            var baseColor = terrain switch
            {
                TerrainType.Forest => new Color(0.19f, 0.34f, 0.22f),
                TerrainType.Hill => new Color(0.48f, 0.40f, 0.25f),
                TerrainType.Mountain => new Color(0.39f, 0.42f, 0.43f),
                TerrainType.Marsh => new Color(0.22f, 0.39f, 0.36f),
                _ => new Color(0.38f, 0.49f, 0.31f)
            };

            if (owner == 1) return Color.Lerp(baseColor, new Color(0.08f, 0.34f, 0.62f), 0.10f);
            if (owner == 2) return Color.Lerp(baseColor, new Color(0.62f, 0.14f, 0.10f), 0.10f);
            return baseColor;
        }

        private static Color GroundColor(HexCell cell)
        {
            // Terrain owns the complete cell surface. The model above it supplies
            // silhouette and height; it is no longer a small icon on generic grass.
            var baseColor = cell.Terrain switch
            {
                TerrainType.Forest => new Color(0.11f, 0.25f, 0.12f),
                TerrainType.Hill => new Color(0.43f, 0.34f, 0.19f),
                TerrainType.Mountain => new Color(0.29f, 0.32f, 0.31f),
                TerrainType.Marsh => new Color(0.12f, 0.31f, 0.28f),
                _ => new Color(0.22f, 0.35f, 0.07f)
            };
            var variation = ((cell.Coord.Q * 17 + cell.Coord.R * 31) & 3) * 0.006f - 0.009f;
            var color = new Color(baseColor.r + variation, baseColor.g + variation,
                baseColor.b + variation * 0.7f);
            if (cell.OwnerNationId == 1)
                color = Color.Lerp(color, new Color(0.18f, 0.45f, 0.68f), 0.025f);
            else if (cell.OwnerNationId == 2)
                color = Color.Lerp(color, new Color(0.72f, 0.23f, 0.14f), 0.025f);
            return color;
        }

        private static void SetGrassSurface(Renderer renderer, Color color, HexCoord coord)
        {
            renderer.sharedMaterial = GrassSurfaceMaterial;
            var center = ToWorld(coord);
            const float worldRepeat = 7.5f;
            const float tileDiameter = 2.24f;
            var textureScale = tileDiameter / worldRepeat;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetVector("_MainTex_ST", new Vector4(textureScale, textureScale,
                center.x / worldRepeat - textureScale * 0.5f,
                center.z / worldRepeat - textureScale * 0.5f));
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static Material GrassSurfaceMaterial
        {
            get
            {
                if (_grassSurfaceMaterial != null) return _grassSurfaceMaterial;
                var template = Resources.Load<Material>("PrototypeSurface");
                if (template == null)
                {
                    var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
                    template = new Material(shader);
                }
                _grassSurfaceMaterial = new Material(template)
                {
                    name = "Shared subtle hand-painted grass",
                    color = Color.white,
                    mainTexture = GrassSurfaceTexture
                };
                if (_grassSurfaceMaterial.HasProperty("_Glossiness"))
                    _grassSurfaceMaterial.SetFloat("_Glossiness", 0f);
                if (_grassSurfaceMaterial.HasProperty("_Smoothness"))
                    _grassSurfaceMaterial.SetFloat("_Smoothness", 0f);
                if (_grassSurfaceMaterial.HasProperty("_Metallic"))
                    _grassSurfaceMaterial.SetFloat("_Metallic", 0f);
                return _grassSurfaceMaterial;
            }
        }

        private static Texture2D GrassSurfaceTexture
        {
            get
            {
                if (_grassSurfaceTexture != null) return _grassSurfaceTexture;
                const int size = 256;
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)size;
                    var v = y / (float)size;
                    var broad = Mathf.Sin(Mathf.PI * 2f * u) * 0.46f +
                                Mathf.Sin(Mathf.PI * 2f * v) * 0.30f +
                                Mathf.Sin(Mathf.PI * 2f * (u + v)) * 0.24f;
                    var secondary = Mathf.Sin(Mathf.PI * 2f * (2f * u - v)) * 0.55f +
                                    Mathf.Sin(Mathf.PI * 2f * (u + 2f * v)) * 0.45f;
                    var dryWash = Mathf.SmoothStep(0.48f, 0.92f, secondary * 0.5f + 0.5f);
                    var value = 0.965f + broad * 0.038f + secondary * 0.012f;
                    var red = Mathf.Clamp01(value + dryWash * 0.018f);
                    var green = Mathf.Clamp01(value + dryWash * 0.006f);
                    var blue = Mathf.Clamp01(value - dryWash * 0.020f);
                    pixels[y * size + x] = new Color(red, green, blue, 1f);
                }
                _grassSurfaceTexture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
                {
                    name = "Procedural broad grass wash",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 2
                };
                _grassSurfaceTexture.SetPixels32(pixels);
                _grassSurfaceTexture.Apply(true, true);
                return _grassSurfaceTexture;
            }
        }

        private static int Compare(HexCoord left, HexCoord right)
        {
            var q = left.Q.CompareTo(right.Q);
            return q != 0 ? q : left.R.CompareTo(right.R);
        }

        private void SetColor(Renderer renderer, Color color, bool modelLighting = false)
        {
            if (renderer == null) return;
            if (modelLighting && _surfaceTemplate == null)
            {
                _surfaceTemplate = Resources.Load<Material>("PrototypeSurface");
                if (_surfaceTemplate == null)
                {
                    var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default") ??
                                 Shader.Find("Hidden/Internal-Colored");
                    _surfaceTemplate = new Material(shader);
                }
            }

            if (!modelLighting && _unlitTemplate == null)
            {
                var shader = Resources.Load<Shader>("ArtV6UnlitColor") ?? Shader.Find("Unlit/Color") ??
                             Shader.Find("Sprites/Default") ??
                             Shader.Find("Hidden/Internal-Colored");
                _unlitTemplate = new Material(shader) { name = "ArtV6 unlit shape" };
            }

            var key = (Color32)color;
            var cache = modelLighting ? _litMaterials : _materials;
            if (!cache.TryGetValue(key, out var material))
            {
                material = new Material(modelLighting ? _surfaceTemplate : _unlitTemplate) { color = color };
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                cache.Add(key, material);
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = modelLighting
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = modelLighting;
        }

        private static void SetOverlayColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            if (_overlayTemplate == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default") ??
                             Shader.Find("Hidden/Internal-Colored");
                _overlayTemplate = new Material(shader) { name = "Tabletop field overlay" };
                if (_overlayTemplate.HasProperty("_Mode")) _overlayTemplate.SetFloat("_Mode", 3f);
                if (_overlayTemplate.HasProperty("_SrcBlend"))
                    _overlayTemplate.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (_overlayTemplate.HasProperty("_DstBlend"))
                    _overlayTemplate.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (_overlayTemplate.HasProperty("_ZWrite")) _overlayTemplate.SetInt("_ZWrite", 0);
                _overlayTemplate.DisableKeyword("_ALPHATEST_ON");
                _overlayTemplate.EnableKeyword("_ALPHABLEND_ON");
                _overlayTemplate.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _overlayTemplate.renderQueue = 3000;
            }
            renderer.sharedMaterial = _overlayTemplate;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void RestoreTileColor(HexCoord coord)
        {
            if (_tileRenderers.TryGetValue(coord, out var renderer) && renderer != null &&
                _tileColors.TryGetValue(coord, out var color))
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }
        }

        private void MoveCameraTo(Vector3 position, float viewSize)
        {
            var camera = Camera.main;
            if (camera == null) return;
            if (_cameraRoutine != null) StopCoroutine(_cameraRoutine);
            _cameraRoutine = StartCoroutine(SmoothCamera(camera, position, viewSize));
        }

        private static IEnumerator SmoothCamera(Camera camera, Vector3 position, float viewSize)
        {
            var startPosition = camera.transform.position;
            var startSize = camera.orthographicSize;
            const float duration = 0.32f;
            var elapsed = 0f;
            while (elapsed < duration && camera != null)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                camera.transform.position = Vector3.Lerp(startPosition, position, t);
                if (camera.orthographic) camera.orthographicSize = Mathf.Lerp(startSize, viewSize, t);
                yield return null;
            }

            if (camera != null)
            {
                camera.transform.position = position;
                if (camera.orthographic) camera.orthographicSize = viewSize;
                else camera.fieldOfView = WorldPresentation.CameraFieldOfView;
            }
        }

        private static Mesh HexMesh
        {
            get
            {
                if (_hexMesh != null) return _hexMesh;
                var vertices = new Vector3[7];
                var uvs = new Vector2[7];
                vertices[0] = Vector3.zero;
                uvs[0] = new Vector2(0.5f, 0.5f);
                for (var i = 0; i < 6; i++)
                {
                    var angle = Mathf.Deg2Rad * (60f * i);
                    vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 1.12f, 0f,
                        Mathf.Sin(angle) * 1.12f);
                    uvs[i + 1] = new Vector2(0.5f + vertices[i + 1].x / 2.24f,
                        0.5f + vertices[i + 1].z / 2.24f);
                }

                var triangles = new int[18];
                for (var i = 0; i < 6; i++)
                {
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = (i + 1) % 6 + 1;
                    triangles[i * 3 + 2] = i + 1;
                }

                _hexMesh = new Mesh { name = "Runtime Hex" };
                _hexMesh.vertices = vertices;
                _hexMesh.uv = uvs;
                _hexMesh.triangles = triangles;
                _hexMesh.RecalculateNormals();
                _hexMesh.RecalculateBounds();
                return _hexMesh;
            }
        }

        private static Mesh HexPrismMesh
        {
            get
            {
                if (_hexPrismMesh != null) return _hexPrismMesh;
                const int sides = 6;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    var angle = Mathf.Deg2Rad * (60f * i);
                    var nextAngle = Mathf.Deg2Rad * (60f * next);
                    var top = new Vector3(Mathf.Cos(angle), 0.5f, Mathf.Sin(angle));
                    var topNext = new Vector3(Mathf.Cos(nextAngle), 0.5f, Mathf.Sin(nextAngle));
                    var bottom = new Vector3(top.x, -0.5f, top.z);
                    var bottomNext = new Vector3(topNext.x, -0.5f, topNext.z);
                    AddTriangleOutward(vertices, triangles, new Vector3(0f, 0.5f, 0f), top, topNext,
                        Vector3.up);
                    AddTriangleOutward(vertices, triangles, new Vector3(0f, -0.5f, 0f), bottomNext, bottom,
                        Vector3.down);
                    AddQuadOutward(vertices, triangles, bottom, top, topNext, bottomNext,
                        top + topNext);
                }

                _hexPrismMesh = new Mesh { name = "Runtime grounded hexagonal foundation" };
                _hexPrismMesh.SetVertices(vertices);
                _hexPrismMesh.SetTriangles(triangles, 0);
                _hexPrismMesh.RecalculateNormals();
                _hexPrismMesh.RecalculateBounds();
                return _hexPrismMesh;
            }
        }

        private static Mesh ConeMesh
        {
            get
            {
                if (_coneMesh != null) return _coneMesh;
                const int sides = 7;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (var i = 0; i < sides; i++)
                {
                    var angle = Mathf.Deg2Rad * (360f * i / sides);
                    var nextAngle = Mathf.Deg2Rad * (360f * (i + 1) / sides);
                    var current = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var next = new Vector3(Mathf.Cos(nextAngle), 0f, Mathf.Sin(nextAngle));
                    AddTriangleOutward(vertices, triangles, new Vector3(0f, 1f, 0f), next, current,
                        (current + next).normalized + Vector3.up * 0.35f);
                    AddTriangleOutward(vertices, triangles, Vector3.zero, current, next, Vector3.down);
                }

                _coneMesh = new Mesh { name = "Runtime Low Poly Cone" };
                _coneMesh.SetVertices(vertices);
                _coneMesh.SetTriangles(triangles, 0);
                _coneMesh.RecalculateNormals();
                _coneMesh.RecalculateBounds();
                return _coneMesh;
            }
        }

        private static Mesh MoundMesh
        {
            get
            {
                if (_moundMesh != null) return _moundMesh;
                const int sides = 10;
                var radii = new[] { 1.00f, 0.94f, 1.05f, 0.91f, 1.02f, 0.96f, 1.06f, 0.92f, 0.98f, 1.03f };
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    var angle = Mathf.Deg2Rad * (360f * i / sides);
                    var nextAngle = Mathf.Deg2Rad * (360f * next / sides);
                    var bottom = new Vector3(Mathf.Cos(angle) * radii[i], 0f, Mathf.Sin(angle) * radii[i]);
                    var bottomNext = new Vector3(Mathf.Cos(nextAngle) * radii[next], 0f,
                        Mathf.Sin(nextAngle) * radii[next]);
                    var shoulder = new Vector3(Mathf.Cos(angle + 0.10f) * radii[i] * 0.68f + 0.06f, 0.62f,
                        Mathf.Sin(angle + 0.10f) * radii[i] * 0.68f - 0.03f);
                    var shoulderNext = new Vector3(Mathf.Cos(nextAngle + 0.10f) * radii[next] * 0.68f + 0.06f,
                        0.62f, Mathf.Sin(nextAngle + 0.10f) * radii[next] * 0.68f - 0.03f);
                    var top = new Vector3(Mathf.Cos(angle - 0.08f) * radii[i] * 0.34f - 0.03f, 0.98f,
                        Mathf.Sin(angle - 0.08f) * radii[i] * 0.34f + 0.02f);
                    var topNext = new Vector3(Mathf.Cos(nextAngle - 0.08f) * radii[next] * 0.34f - 0.03f,
                        0.98f, Mathf.Sin(nextAngle - 0.08f) * radii[next] * 0.34f + 0.02f);
                    AddQuadOutward(vertices, triangles, bottom, bottomNext, shoulderNext, shoulder,
                        bottom + bottomNext + Vector3.up * 0.25f);
                    AddQuadOutward(vertices, triangles, shoulder, shoulderNext, topNext, top,
                        shoulder + shoulderNext + Vector3.up * 0.45f);
                    AddTriangleOutward(vertices, triangles, new Vector3(-0.03f, 1.02f, 0.02f), top, topNext,
                        Vector3.up);
                    AddTriangleOutward(vertices, triangles, Vector3.zero, bottomNext, bottom, Vector3.down);
                }

                _moundMesh = new Mesh { name = "Runtime sculpted mound" };
                _moundMesh.SetVertices(vertices);
                _moundMesh.SetTriangles(triangles, 0);
                _moundMesh.RecalculateNormals();
                _moundMesh.RecalculateBounds();
                return _moundMesh;
            }
        }

        private static Mesh MountainMesh
        {
            get
            {
                if (_mountainMesh != null) return _mountainMesh;
                const int sides = 9;
                var radii = new[] { 0.96f, 1.08f, 0.88f, 1.03f, 0.93f, 1.06f, 0.90f, 1.02f, 0.95f };
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var peak = new Vector3(0.13f, 1f, -0.10f);
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    var angle = Mathf.Deg2Rad * (360f * i / sides);
                    var nextAngle = Mathf.Deg2Rad * (360f * next / sides);
                    var bottom = new Vector3(Mathf.Cos(angle) * radii[i], 0f, Mathf.Sin(angle) * radii[i]);
                    var bottomNext = new Vector3(Mathf.Cos(nextAngle) * radii[next], 0f,
                        Mathf.Sin(nextAngle) * radii[next]);
                    var shoulder = new Vector3(Mathf.Cos(angle + 0.14f) * radii[i] * 0.52f + 0.04f,
                        0.43f + (i % 3) * 0.035f,
                        Mathf.Sin(angle + 0.14f) * radii[i] * 0.52f - 0.04f);
                    var shoulderNext = new Vector3(Mathf.Cos(nextAngle + 0.14f) * radii[next] * 0.52f + 0.04f,
                        0.43f + (next % 3) * 0.035f,
                        Mathf.Sin(nextAngle + 0.14f) * radii[next] * 0.52f - 0.04f);
                    AddQuadOutward(vertices, triangles, bottom, bottomNext, shoulderNext, shoulder,
                        bottom + bottomNext + Vector3.up * 0.15f);
                    AddTriangleOutward(vertices, triangles, shoulder, shoulderNext, peak,
                        shoulder + shoulderNext + peak);
                    AddTriangleOutward(vertices, triangles, Vector3.zero, bottomNext, bottom, Vector3.down);
                }

                _mountainMesh = new Mesh { name = "Runtime asymmetric mountain" };
                _mountainMesh.SetVertices(vertices);
                _mountainMesh.SetTriangles(triangles, 0);
                _mountainMesh.RecalculateNormals();
                _mountainMesh.RecalculateBounds();
                return _mountainMesh;
            }
        }

        private static Mesh IrregularDiscMesh
        {
            get
            {
                if (_irregularDiscMesh != null) return _irregularDiscMesh;
                const int sides = 11;
                var radii = new[] { 1.00f, 0.82f, 1.08f, 0.91f, 1.02f, 0.85f, 1.05f, 0.90f, 1.09f, 0.86f, 0.96f };
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    var angle = Mathf.Deg2Rad * (360f * i / sides);
                    var nextAngle = Mathf.Deg2Rad * (360f * next / sides);
                    var current = new Vector3(Mathf.Cos(angle) * radii[i], 0f, Mathf.Sin(angle) * radii[i]);
                    var nextPoint = new Vector3(Mathf.Cos(nextAngle) * radii[next], 0f,
                        Mathf.Sin(nextAngle) * radii[next]);
                    AddTriangleOutward(vertices, triangles, Vector3.zero, current, nextPoint, Vector3.up);
                }

                _irregularDiscMesh = new Mesh { name = "Runtime irregular ground pool" };
                _irregularDiscMesh.SetVertices(vertices);
                _irregularDiscMesh.SetTriangles(triangles, 0);
                _irregularDiscMesh.RecalculateNormals();
                _irregularDiscMesh.RecalculateBounds();
                return _irregularDiscMesh;
            }
        }

        private static Mesh ConiferCrownMesh
        {
            get
            {
                if (_coniferCrownMesh != null) return _coniferCrownMesh;
                const int sides = 8;
                var heights = new[] { 0f, 0.25f, 0.29f, 0.55f, 0.59f, 0.81f };
                var radii = new[] { 0.50f, 0.33f, 0.43f, 0.24f, 0.31f, 0.13f };
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (var ring = 0; ring < heights.Length - 1; ring++)
                {
                    for (var i = 0; i < sides; i++)
                    {
                        var next = (i + 1) % sides;
                        var phase = ring * 0.055f;
                        var angle = Mathf.Deg2Rad * (360f * i / sides) + phase;
                        var nextAngle = Mathf.Deg2Rad * (360f * next / sides) + phase;
                        var upperAngle = Mathf.Deg2Rad * (360f * i / sides) + phase + 0.055f;
                        var upperNextAngle = Mathf.Deg2Rad * (360f * next / sides) + phase + 0.055f;
                        var lower = new Vector3(Mathf.Cos(angle) * radii[ring], heights[ring],
                            Mathf.Sin(angle) * radii[ring]);
                        var lowerNext = new Vector3(Mathf.Cos(nextAngle) * radii[ring], heights[ring],
                            Mathf.Sin(nextAngle) * radii[ring]);
                        var upper = new Vector3(Mathf.Cos(upperAngle) * radii[ring + 1], heights[ring + 1],
                            Mathf.Sin(upperAngle) * radii[ring + 1]);
                        var upperNext = new Vector3(Mathf.Cos(upperNextAngle) * radii[ring + 1], heights[ring + 1],
                            Mathf.Sin(upperNextAngle) * radii[ring + 1]);
                        AddQuadOutward(vertices, triangles, lower, lowerNext, upperNext, upper,
                            lower + lowerNext + Vector3.up * 0.20f);
                        if (ring == 0)
                            AddTriangleOutward(vertices, triangles, Vector3.zero, lowerNext, lower, Vector3.down);
                    }
                }

                var finalRing = heights.Length - 1;
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    var phase = (finalRing - 1) * 0.055f + 0.055f;
                    var angle = Mathf.Deg2Rad * (360f * i / sides) + phase;
                    var nextAngle = Mathf.Deg2Rad * (360f * next / sides) + phase;
                    var current = new Vector3(Mathf.Cos(angle) * radii[finalRing], heights[finalRing],
                        Mathf.Sin(angle) * radii[finalRing]);
                    var nextPoint = new Vector3(Mathf.Cos(nextAngle) * radii[finalRing], heights[finalRing],
                        Mathf.Sin(nextAngle) * radii[finalRing]);
                    AddTriangleOutward(vertices, triangles, current, nextPoint, new Vector3(0.025f, 1f, -0.015f),
                        current + nextPoint + Vector3.up);
                }

                _coniferCrownMesh = new Mesh { name = "Runtime layered conifer crown" };
                _coniferCrownMesh.SetVertices(vertices);
                _coniferCrownMesh.SetTriangles(triangles, 0);
                _coniferCrownMesh.RecalculateNormals();
                _coniferCrownMesh.RecalculateBounds();
                return _coniferCrownMesh;
            }
        }

        private static Mesh LowPolySphereMesh
        {
            get
            {
                if (_lowPolySphereMesh != null) return _lowPolySphereMesh;
                const int sides = 8;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var top = new Vector3(0f, 1f, 0f);
                var bottom = new Vector3(0f, -1f, 0f);
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    var angle = Mathf.Deg2Rad * (360f * i / sides);
                    var nextAngle = Mathf.Deg2Rad * (360f * next / sides);
                    var upper = new Vector3(Mathf.Cos(angle) * 0.82f, 0.48f, Mathf.Sin(angle) * 0.82f);
                    var upperNext = new Vector3(Mathf.Cos(nextAngle) * 0.82f, 0.48f,
                        Mathf.Sin(nextAngle) * 0.82f);
                    var middle = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var middleNext = new Vector3(Mathf.Cos(nextAngle), 0f, Mathf.Sin(nextAngle));
                    var lower = new Vector3(Mathf.Cos(angle + 0.05f) * 0.78f, -0.52f,
                        Mathf.Sin(angle + 0.05f) * 0.78f);
                    var lowerNext = new Vector3(Mathf.Cos(nextAngle + 0.05f) * 0.78f, -0.52f,
                        Mathf.Sin(nextAngle + 0.05f) * 0.78f);
                    AddTriangleOutward(vertices, triangles, top, upperNext, upper, upper + upperNext + Vector3.up);
                    AddQuadOutward(vertices, triangles, upper, upperNext, middleNext, middle,
                        upper + upperNext + middle + middleNext);
                    AddQuadOutward(vertices, triangles, middle, middleNext, lowerNext, lower,
                        middle + middleNext + lower + lowerNext);
                    AddTriangleOutward(vertices, triangles, lower, lowerNext, bottom,
                        lower + lowerNext + Vector3.down);
                }

                _lowPolySphereMesh = new Mesh { name = "Runtime low-poly equipment sphere" };
                _lowPolySphereMesh.SetVertices(vertices);
                _lowPolySphereMesh.SetTriangles(triangles, 0);
                _lowPolySphereMesh.RecalculateNormals();
                _lowPolySphereMesh.RecalculateBounds();
                return _lowPolySphereMesh;
            }
        }

        private static Mesh GableRoofMesh
        {
            get
            {
                if (_gableRoofMesh != null) return _gableRoofMesh;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var frontLeft = new Vector3(-0.5f, 0f, -0.5f);
                var frontRight = new Vector3(0.5f, 0f, -0.5f);
                var backLeft = new Vector3(-0.5f, 0f, 0.5f);
                var backRight = new Vector3(0.5f, 0f, 0.5f);
                var frontRidge = new Vector3(0f, 1f, -0.5f);
                var backRidge = new Vector3(0f, 1f, 0.5f);
                AddQuadOutward(vertices, triangles, frontLeft, backLeft, backRight, frontRight, Vector3.down);
                AddTriangleOutward(vertices, triangles, frontLeft, frontRidge, frontRight, Vector3.back);
                AddTriangleOutward(vertices, triangles, backLeft, backRight, backRidge, Vector3.forward);
                AddQuadOutward(vertices, triangles, frontLeft, backLeft, backRidge, frontRidge,
                    Vector3.left + Vector3.up);
                AddQuadOutward(vertices, triangles, frontRight, frontRidge, backRidge, backRight,
                    Vector3.right + Vector3.up);
                _gableRoofMesh = new Mesh { name = "Runtime matte gable roof" };
                _gableRoofMesh.SetVertices(vertices);
                _gableRoofMesh.SetTriangles(triangles, 0);
                _gableRoofMesh.RecalculateNormals();
                _gableRoofMesh.RecalculateBounds();
                return _gableRoofMesh;
            }
        }

        private static Mesh BeveledBoxMesh
        {
            get
            {
                if (_beveledBoxMesh != null) return _beveledBoxMesh;
                const float half = 0.5f;
                const float inset = 0.42f;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();

                AddQuadOutward(vertices, triangles,
                    new Vector3(half, -inset, -inset), new Vector3(half, inset, -inset),
                    new Vector3(half, inset, inset), new Vector3(half, -inset, inset), Vector3.right);
                AddQuadOutward(vertices, triangles,
                    new Vector3(-half, -inset, inset), new Vector3(-half, inset, inset),
                    new Vector3(-half, inset, -inset), new Vector3(-half, -inset, -inset), Vector3.left);
                AddQuadOutward(vertices, triangles,
                    new Vector3(-inset, half, -inset), new Vector3(-inset, half, inset),
                    new Vector3(inset, half, inset), new Vector3(inset, half, -inset), Vector3.up);
                AddQuadOutward(vertices, triangles,
                    new Vector3(-inset, -half, inset), new Vector3(-inset, -half, -inset),
                    new Vector3(inset, -half, -inset), new Vector3(inset, -half, inset), Vector3.down);
                AddQuadOutward(vertices, triangles,
                    new Vector3(-inset, -inset, half), new Vector3(inset, -inset, half),
                    new Vector3(inset, inset, half), new Vector3(-inset, inset, half), Vector3.forward);
                AddQuadOutward(vertices, triangles,
                    new Vector3(inset, -inset, -half), new Vector3(-inset, -inset, -half),
                    new Vector3(-inset, inset, -half), new Vector3(inset, inset, -half), Vector3.back);

                for (var axisA = 0; axisA < 3; axisA++)
                for (var axisB = axisA + 1; axisB < 3; axisB++)
                {
                    var axisC = 3 - axisA - axisB;
                    for (var signA = -1; signA <= 1; signA += 2)
                    for (var signB = -1; signB <= 1; signB += 2)
                    {
                        var a0 = BevelPoint(axisA, signA * half, axisB, signB * inset,
                            axisC, -inset);
                        var a1 = BevelPoint(axisA, signA * inset, axisB, signB * half,
                            axisC, -inset);
                        var a2 = BevelPoint(axisA, signA * inset, axisB, signB * half,
                            axisC, inset);
                        var a3 = BevelPoint(axisA, signA * half, axisB, signB * inset,
                            axisC, inset);
                        var outward = Axis(axisA) * signA + Axis(axisB) * signB;
                        AddQuadOutward(vertices, triangles, a0, a1, a2, a3, outward);
                    }
                }

                for (var signX = -1; signX <= 1; signX += 2)
                for (var signY = -1; signY <= 1; signY += 2)
                for (var signZ = -1; signZ <= 1; signZ += 2)
                {
                    AddTriangleOutward(vertices, triangles,
                        new Vector3(signX * half, signY * inset, signZ * inset),
                        new Vector3(signX * inset, signY * half, signZ * inset),
                        new Vector3(signX * inset, signY * inset, signZ * half),
                        new Vector3(signX, signY, signZ));
                }

                _beveledBoxMesh = new Mesh { name = "Runtime crisp beveled box" };
                _beveledBoxMesh.SetVertices(vertices);
                _beveledBoxMesh.SetTriangles(triangles, 0);
                _beveledBoxMesh.RecalculateNormals();
                _beveledBoxMesh.RecalculateBounds();
                return _beveledBoxMesh;
            }
        }

        private static Vector3 Axis(int axis)
        {
            return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        }

        private static Vector3 BevelPoint(int axisA, float valueA, int axisB, float valueB,
            int axisC, float valueC)
        {
            var point = Vector3.zero;
            point[axisA] = valueA;
            point[axisB] = valueB;
            point[axisC] = valueC;
            return point;
        }

        private static void AddQuadOutward(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            var start = vertices.Count;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            {
                var swap = b;
                b = d;
                d = swap;
            }
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void AddTriangleOutward(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var start = vertices.Count;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            {
                var swap = b;
                b = c;
                c = swap;
            }
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        private static Material TrailMaterial
        {
            get
            {
                if (_trailMaterial != null) return _trailMaterial;
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard") ??
                             Shader.Find("Hidden/Internal-Colored");
                _trailMaterial = new Material(shader) { name = "Shared Action Trail" };
                return _trailMaterial;
            }
        }
    }
}
