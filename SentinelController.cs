using System;
using System.Collections.Generic;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using MEC;
using UnityEngine;
using ToyLight = Exiled.API.Features.Toys.Light;

namespace TowerSentinel
{
    public class SentinelController
    {
        private static readonly int BeamCollisionMask = CreateBeamCollisionMask();

        private sealed class TurretPart
        {
            public Primitive Primitive;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 TargetScale;
        }

        private readonly List<Primitive> _barricades = new List<Primitive>();
        private readonly List<TurretPart> _compoundParts = new List<TurretPart>();
        private readonly List<Primitive> _temporaryPrimitives = new List<Primitive>();
        private readonly List<ToyLight> _temporaryLights = new List<ToyLight>();

        private readonly Vector3 _adminTowerCenter;
        private Primitive _turretBase;
        private Primitive _laserCore;
        private Primitive _minigunBarrelLeft;
        private Primitive _minigunBarrelRight;
        private ToyLight _turretLight;
        private Vector3 _turretPivot;
        private Quaternion _currentTurretRotation = Quaternion.identity;
        private bool _minigunAltFire;
        private bool _isFiring;
        private float _nextAllowedFireTime;
        private int _generation;

        public SentinelController(Vector3 center)
        {
            _adminTowerCenter = center + new Vector3(0f, Plugin.Instance.Config.StructureYOffset, 0f);
        }

        public bool IsActive { get; private set; }

        public void Activate()
        {
            if (IsActive)
                return;

            IsActive = true;
            int generation = ++_generation;

            try
            {
                SpawnBarricades(generation);
                SpawnTurret(generation);
            }
            catch (Exception exception)
            {
                Log.Error($"Tower deployment failed: {exception}");
                Deactivate();
            }
        }

        public void Deactivate()
        {
            IsActive = false;
            _generation++;
            _isFiring = false;
            _nextAllowedFireTime = 0f;

            foreach (Primitive primitive in _temporaryPrimitives.ToArray())
                SafeDestroy(primitive);
            _temporaryPrimitives.Clear();

            foreach (ToyLight light in _temporaryLights.ToArray())
                SafeDestroy(light);
            _temporaryLights.Clear();

            foreach (Primitive barricade in _barricades)
                SafeDestroy(barricade);
            _barricades.Clear();

            SafeDestroy(_turretBase);
            foreach (TurretPart part in _compoundParts)
                SafeDestroy(part.Primitive);
            _compoundParts.Clear();

            SafeDestroy(_turretLight);

            _turretBase = null;
            _laserCore = null;
            _minigunBarrelLeft = null;
            _minigunBarrelRight = null;
            _turretLight = null;
        }

        public bool WasTowerHit(Vector3 origin, Vector3 direction, float maximumDistance, Player shooter)
        {
            if (!IsActive || direction.sqrMagnitude < 0.0001f)
                return false;

            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                direction.normalized,
                maximumDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (shooter != null && Player.Get(hit.collider) == shooter)
                    continue;

                return IsPrimitive(hit.collider.gameObject);
            }

            return false;
        }

        public bool IsExplosionTouchingTower(Vector3 position, float radius)
        {
            if (!IsActive)
                return false;

            Collider[] colliders = Physics.OverlapSphere(
                position,
                radius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            foreach (Collider collider in colliders)
            {
                if (collider != null && IsPrimitive(collider.gameObject))
                    return true;
            }

            return false;
        }

        public bool IsPrimitive(GameObject gameObject)
        {
            if (!IsActive || gameObject == null)
                return false;

            foreach (Primitive barricade in _barricades)
            {
                if (MatchesPrimitive(barricade, gameObject))
                    return true;
            }

            if (MatchesPrimitive(_turretBase, gameObject))
                return true;

            foreach (TurretPart part in _compoundParts)
            {
                if (MatchesPrimitive(part.Primitive, gameObject))
                    return true;
            }

            return false;
        }

        public void RetaliateLaser(Player target)
        {
            if (!TryBeginRetaliation(target))
                return;

            int generation = _generation;
            Timing.RunCoroutine(RetaliationLaserRoutine(target, generation));
        }

        public void RetaliateMinigun(Player target)
        {
            if (!TryBeginRetaliation(target))
                return;

            int generation = _generation;
            Timing.RunCoroutine(RetaliationMinigunRoutine(target, generation));
        }

        private void SpawnBarricades(int generation)
        {
            Config config = Plugin.Instance.Config;
            float width = config.BarricadeWidth;
            float depth = config.BarricadeDepth;
            float height = config.BarricadeHeight;
            float halfWidth = width / 2f;
            float halfDepth = depth / 2f;
            float halfHeight = height / 2f;

            Vector3[] positions =
            {
                _adminTowerCenter + new Vector3(halfWidth, halfHeight, 0f),
                _adminTowerCenter + new Vector3(-halfWidth, halfHeight, 0f),
                _adminTowerCenter + new Vector3(0f, halfHeight, halfDepth),
                _adminTowerCenter + new Vector3(0f, halfHeight, -halfDepth),
            };

            Vector3[] scales =
            {
                new Vector3(1f, height, depth + 1f),
                new Vector3(1f, height, depth + 1f),
                new Vector3(width + 1f, height, 1f),
                new Vector3(width + 1f, height, 1f),
            };

            for (int index = 0; index < positions.Length; index++)
            {
                Vector3 targetPosition = positions[index];
                Vector3 startPosition = targetPosition - new Vector3(0f, height + 5f, 0f);
                Primitive wall = CreatePrimitive(
                    PrimitiveType.Cube,
                    startPosition,
                    Vector3.zero,
                    scales[index],
                    new Color(0.2f, 0.2f, 0.2f, 0.8f),
                    true);

                _barricades.Add(wall);
                Timing.RunCoroutine(AnimatePrimitive(wall, targetPosition, 1f, generation));
            }

            Vector3 roofTarget = _adminTowerCenter + new Vector3(0f, height + 0.5f, 0f);
            Primitive roof = CreatePrimitive(
                PrimitiveType.Cube,
                roofTarget + new Vector3(0f, 5f, 0f),
                Vector3.zero,
                new Vector3(width, 1f, depth),
                new Color(0.1f, 0.1f, 0.1f, 1f),
                true);

            _barricades.Add(roof);
            Timing.RunCoroutine(AnimatePrimitive(roof, roofTarget, 1f, generation));
        }

        private void SpawnTurret(int generation)
        {
            Config config = Plugin.Instance.Config;
            float height = config.BarricadeHeight;
            float offsetX = config.TurretOffsetX;
            float offsetZ = config.TurretOffsetZ;
            Vector3 basePosition = _adminTowerCenter + new Vector3(offsetX, height + 1.5f, offsetZ);
            Vector3 offsetDirection = new Vector3(offsetX, 0f, offsetZ);
            Vector3 defaultAimDirection = offsetDirection == Vector3.zero ? Vector3.forward : offsetDirection.normalized;

            _currentTurretRotation = Quaternion.LookRotation(defaultAimDirection);
            _turretPivot = basePosition;

            _turretBase = CreatePrimitive(
                PrimitiveType.Cylinder,
                basePosition - new Vector3(0f, 0.5f, 0f),
                Vector3.zero,
                new Vector3(2f, 1f, 2f),
                new Color(0.15f, 0.15f, 0.15f),
                true);

            Primitive AddPart(PrimitiveType type, Vector3 localPosition, Quaternion localRotation, Vector3 targetScale, Color color)
            {
                Quaternion worldRotation = _currentTurretRotation * localRotation;
                Primitive primitive = CreatePrimitive(
                    type,
                    _turretPivot + (_currentTurretRotation * localPosition),
                    worldRotation.eulerAngles,
                    Vector3.zero,
                    color,
                    true);

                _compoundParts.Add(new TurretPart
                {
                    Primitive = primitive,
                    LocalPosition = localPosition,
                    LocalRotation = localRotation,
                    TargetScale = targetScale,
                });

                Timing.RunCoroutine(AnimateScale(primitive, targetScale, 1.2f, generation));
                return primitive;
            }

            AddPart(PrimitiveType.Sphere, Vector3.zero, Quaternion.identity, new Vector3(1.6f, 1.6f, 1.6f), new Color(0.1f, 0.1f, 0.1f));
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0f, 1.2f), Quaternion.identity, new Vector3(0.9f, 0.9f, 2.5f), new Color(0.2f, 0.2f, 0.2f));
            _laserCore = AddPart(PrimitiveType.Cylinder, new Vector3(0f, 0f, 1.3f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.4f, 2.7f, 0.4f), new Color(1f, 0f, 0f, 0.9f));
            AddPart(PrimitiveType.Cube, new Vector3(-0.6f, 0f, 0.8f), Quaternion.identity, new Vector3(0.5f, 0.3f, 0.6f), new Color(0.3f, 0.3f, 0.3f));
            AddPart(PrimitiveType.Cube, new Vector3(0.6f, 0f, 0.8f), Quaternion.identity, new Vector3(0.5f, 0.3f, 0.6f), new Color(0.3f, 0.3f, 0.3f));
            _minigunBarrelLeft = AddPart(PrimitiveType.Cylinder, new Vector3(-0.95f, 0f, 1.5f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.3f, 2.2f, 0.3f), Color.black);
            _minigunBarrelRight = AddPart(PrimitiveType.Cylinder, new Vector3(0.95f, 0f, 1.5f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.3f, 2.2f, 0.3f), Color.black);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.6f, 0.5f), Quaternion.identity, new Vector3(0.4f, 0.4f, 0.6f), new Color(0f, 0.8f, 1f, 0.9f));

            _turretLight = ToyLight.Create(_turretPivot + new Vector3(0f, 2f, 0f));
            if (IsToyAlive(_turretLight))
            {
                _turretLight.Color = Color.cyan;
                _turretLight.Intensity = 2f;
                _turretLight.Range = 15f;
            }

            AimTurret(_currentTurretRotation);
        }

        private IEnumerator<float> AnimatePrimitive(Primitive primitive, Vector3 targetPosition, float duration, int generation)
        {
            if (!CanAnimate(primitive, generation))
                yield break;

            float elapsed = 0f;
            Vector3 startPosition = primitive.Position;
            while (elapsed < duration)
            {
                if (!CanAnimate(primitive, generation))
                    yield break;

                elapsed += Timing.DeltaTime;
                primitive.Position = Vector3.Lerp(startPosition, targetPosition, Mathf.Clamp01(elapsed / duration));
                yield return Timing.WaitForOneFrame;
            }

            if (CanAnimate(primitive, generation))
                primitive.Position = targetPosition;
        }

        private IEnumerator<float> AnimateScale(Primitive primitive, Vector3 targetScale, float duration, int generation)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (!CanAnimate(primitive, generation))
                    yield break;

                elapsed += Timing.DeltaTime;
                primitive.Scale = Vector3.Lerp(Vector3.zero, targetScale, Mathf.Clamp01(elapsed / duration));
                yield return Timing.WaitForOneFrame;
            }

            if (CanAnimate(primitive, generation))
                primitive.Scale = targetScale;
        }

        private IEnumerator<float> RetaliationLaserRoutine(Player target, int generation)
        {
            Primitive laserOuter = null;
            Primitive laserInner = null;
            ToyLight impactLight = null;

            try
            {
                if (!CanContinueRetaliation(target, generation) || !IsToyAlive(_laserCore))
                    yield break;

                float elapsed = 0f;
                const float chargeDuration = 0.5f;
                while (elapsed < chargeDuration)
                {
                    if (!CanContinueRetaliation(target, generation) || !IsToyAlive(_laserCore))
                        yield break;

                    elapsed += Timing.DeltaTime;
                    Vector3 targetDirection = TargetPoint(target) - _turretPivot;
                    if (targetDirection.sqrMagnitude > 0.0001f)
                    {
                        Quaternion desiredRotation = Quaternion.LookRotation(targetDirection);
                        AimTurret(Quaternion.Slerp(_currentTurretRotation, desiredRotation, Mathf.Clamp01(Timing.DeltaTime * 10f)));
                    }

                    if (IsToyAlive(_turretLight))
                    {
                        _turretLight.Color = Color.yellow;
                        _turretLight.Intensity = 10f * Mathf.Clamp01(elapsed / chargeDuration);
                    }

                    yield return Timing.WaitForOneFrame;
                }

                if (!CanContinueRetaliation(target, generation) || !IsToyAlive(_laserCore))
                    yield break;

                Vector3 targetDirectionAtFire = TargetPoint(target) - _turretPivot;
                if (targetDirectionAtFire.sqrMagnitude < 0.0001f)
                    yield break;

                AimTurret(Quaternion.LookRotation(targetDirectionAtFire));
                if (IsToyAlive(_turretLight))
                    _turretLight.Color = Color.red;

                Vector3 startPosition = _laserCore.Position;
                bool canDamage = TryGetImpact(target, startPosition, out Vector3 impactPosition);
                CreateBeam(startPosition, impactPosition, 0.15f, 0.04f, new Color(1f, 0f, 0f, 0.4f), Color.white, out laserOuter, out laserInner);

                impactLight = TrackLight(ToyLight.Create(impactPosition));
                if (IsToyAlive(impactLight))
                {
                    impactLight.Color = Color.red;
                    impactLight.Intensity = 8f;
                    impactLight.Range = 12f;
                }

                if (canDamage && CanContinueRetaliation(target, generation))
                    target.Hurt(Plugin.Instance.Config.TurretDamage, "Heavy Laser");

                yield return Timing.WaitForSeconds(0.4f);
            }
            finally
            {
                DestroyTemporary(laserOuter);
                DestroyTemporary(laserInner);
                DestroyTemporary(impactLight);
                EndRetaliation(generation);
            }
        }

        private IEnumerator<float> RetaliationMinigunRoutine(Player target, int generation)
        {
            Primitive tracerOuter = null;
            Primitive tracerInner = null;
            ToyLight tracerLight = null;

            try
            {
                if (!CanContinueRetaliation(target, generation))
                    yield break;

                Vector3 targetDirection = TargetPoint(target) - _turretPivot;
                if (targetDirection.sqrMagnitude < 0.0001f)
                    yield break;

                AimTurret(Quaternion.LookRotation(targetDirection));
                if (IsToyAlive(_turretLight))
                {
                    _turretLight.Color = new Color(1f, 0.5f, 0f);
                    _turretLight.Intensity = 8f;
                }

                Primitive activeBarrel = _minigunAltFire ? _minigunBarrelRight : _minigunBarrelLeft;
                _minigunAltFire = !_minigunAltFire;
                if (!IsToyAlive(activeBarrel))
                    yield break;

                Vector3 startPosition = activeBarrel.Position;
                bool canDamage = TryGetImpact(target, startPosition, out Vector3 impactPosition);
                if (canDamage && CanContinueRetaliation(target, generation))
                    target.Hurt(Plugin.Instance.Config.MinigunDamage, "Dual Minigun");

                CreateBeam(startPosition, impactPosition, 0.08f, 0.03f, new Color(1f, 0.5f, 0f, 0.4f), Color.yellow, out tracerOuter, out tracerInner);
                tracerLight = TrackLight(ToyLight.Create(impactPosition));
                if (IsToyAlive(tracerLight))
                {
                    tracerLight.Color = new Color(1f, 0.5f, 0f);
                    tracerLight.Intensity = 4f;
                    tracerLight.Range = 8f;
                }

                yield return Timing.WaitForSeconds(0.15f);
            }
            finally
            {
                DestroyTemporary(tracerOuter);
                DestroyTemporary(tracerInner);
                DestroyTemporary(tracerLight);
                EndRetaliation(generation);
            }
        }

        private bool TryBeginRetaliation(Player target)
        {
            bool hasValidTarget = IsValidTarget(target);
            if (!Guardrails.CanStartRetaliation(IsActive, _isFiring, hasValidTarget, Time.time, _nextAllowedFireTime))
                return false;

            _isFiring = true;
            _nextAllowedFireTime = Time.time + Plugin.Instance.Config.RetaliationCooldown;
            return true;
        }

        private bool CanContinueRetaliation(Player target, int generation)
        {
            return IsActive && generation == _generation && IsValidTarget(target);
        }

        private static bool IsValidTarget(Player target)
        {
            return target != null
                && target.IsConnected
                && target.IsAlive
                && Guardrails.CanTargetAttacker(
                    target.Role.Type == PlayerRoles.RoleTypeId.Tutorial,
                    Plugin.Instance.Config.CanTargetTutorialAttackers);
        }

        private void EndRetaliation(int generation)
        {
            if (generation != _generation)
                return;

            _isFiring = false;
            if (IsActive && IsToyAlive(_turretLight))
            {
                _turretLight.Color = Color.cyan;
                _turretLight.Intensity = 2f;
            }
        }

        private bool TryGetImpact(Player target, Vector3 startPosition, out Vector3 impactPosition)
        {
            Vector3 targetPoint = TargetPoint(target);
            Vector3 direction = targetPoint - startPosition;
            float distance = direction.magnitude;
            impactPosition = targetPoint;

            if (distance < 0.01f)
                return true;

            RaycastHit[] hits = Physics.RaycastAll(
                startPosition,
                direction / distance,
                distance,
                BeamCollisionMask,
                QueryTriggerInteraction.Ignore);

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (IsPrimitive(hit.collider.gameObject))
                    continue;

                impactPosition = hit.point;
                Player hitPlayer = Player.Get(hit.collider);
                if (hitPlayer == target)
                    return true;

                if (Plugin.Instance.Config.Debug)
                {
                    string layerName = LayerMask.LayerToName(hit.collider.gameObject.layer);
                    string blocker = hitPlayer == null
                        ? hit.collider.gameObject.name
                        : $"player {hitPlayer.Nickname}";
                    Log.Debug($"Retaliation toward {target.Nickname} was blocked by {blocker} on layer '{layerName}' at {hit.point}.");
                }

                return false;
            }

            return true;
        }

        private void CreateBeam(
            Vector3 startPosition,
            Vector3 endPosition,
            float outerRadius,
            float innerRadius,
            Color outerColor,
            Color innerColor,
            out Primitive outer,
            out Primitive inner)
        {
            Vector3 direction = endPosition - startPosition;
            float distance = direction.magnitude;
            if (distance < 0.01f)
            {
                outer = null;
                inner = null;
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
            Vector3 center = startPosition + (direction / 2f);
            outer = TrackPrimitive(CreatePrimitive(
                PrimitiveType.Cylinder,
                center,
                rotation.eulerAngles,
                new Vector3(outerRadius, distance / 2f, outerRadius),
                outerColor,
                false));
            inner = TrackPrimitive(CreatePrimitive(
                PrimitiveType.Cylinder,
                center,
                rotation.eulerAngles,
                new Vector3(innerRadius, distance / 2f, innerRadius),
                innerColor,
                false));
        }

        private void AimTurret(Quaternion targetRotation)
        {
            _currentTurretRotation = targetRotation;
            foreach (TurretPart part in _compoundParts)
            {
                if (!IsToyAlive(part.Primitive))
                    continue;

                part.Primitive.Rotation = targetRotation * part.LocalRotation;
                part.Primitive.Position = _turretPivot + (targetRotation * part.LocalPosition);
            }
        }

        private bool CanAnimate(Primitive primitive, int generation)
        {
            return IsActive && generation == _generation && IsToyAlive(primitive);
        }

        private static bool MatchesPrimitive(Primitive primitive, GameObject gameObject)
        {
            return IsToyAlive(primitive)
                && (primitive.GameObject == gameObject || gameObject.transform.IsChildOf(primitive.Transform));
        }

        private Primitive TrackPrimitive(Primitive primitive)
        {
            if (primitive != null)
                _temporaryPrimitives.Add(primitive);
            return primitive;
        }

        private ToyLight TrackLight(ToyLight light)
        {
            if (light != null)
                _temporaryLights.Add(light);
            return light;
        }

        private void DestroyTemporary(Primitive primitive)
        {
            if (primitive == null)
                return;

            _temporaryPrimitives.Remove(primitive);
            SafeDestroy(primitive);
        }

        private void DestroyTemporary(ToyLight light)
        {
            if (light == null)
                return;

            _temporaryLights.Remove(light);
            SafeDestroy(light);
        }

        private static Primitive CreatePrimitive(
            PrimitiveType type,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            Color color,
            bool collidable)
        {
            Primitive primitive = Primitive.Create(type, position, rotation, scale, false);
            primitive.Color = color;
            primitive.Collidable = collidable;
            primitive.Spawn();
            return primitive;
        }

        private static Vector3 TargetPoint(Player target)
        {
            return target.Position + Vector3.up;
        }

        private static int CreateBeamCollisionMask()
        {
            // Match SCP:SL's native hitscan layers. Physics.DefaultRaycastLayers also
            // includes Room/culling colliders such as the invisible Surface barrier,
            // which ordinary bullets do not treat as an impact surface.
            int mask = LayerMask.GetMask("Default", "Hitbox", "Glass", "CCTV", "Door");
            return mask != 0 ? mask : Physics.DefaultRaycastLayers;
        }

        private static bool IsToyAlive(AdminToy toy)
        {
            return toy != null && toy.AdminToyBase != null;
        }

        private static void SafeDestroy(AdminToy toy)
        {
            if (!IsToyAlive(toy))
                return;

            try
            {
                toy.Destroy();
            }
            catch (Exception exception)
            {
                Log.Debug($"Failed to destroy an admin toy cleanly: {exception.Message}");
            }
        }
    }
}
