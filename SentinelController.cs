using System;
using System.Collections.Generic;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Pickups.Projectiles;
using Exiled.API.Features.Toys;
using Exiled.Events.EventArgs.Map;
using MEC;
using UnityEngine;
using ToyLight = Exiled.API.Features.Toys.Light;

namespace TowerSentinel
{
    public class SentinelController
    {
        private static readonly int BeamCollisionMask = CreateBeamCollisionMask();
        private const float CannonMuzzleOffset = 2.2f;
        private const float CannonLaunchClearanceRadius = 0.35f;
        private const float CannonMaximumClearanceOffset = 5f;

        private sealed class TurretPart
        {
            public Primitive Primitive;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 TargetScale;
        }

        private sealed class CannonGrenade
        {
            public Projectile Projectile;
            public Player Target;
        }

        private readonly List<Primitive> _barricades = new List<Primitive>();
        private readonly List<TurretPart> _compoundParts = new List<TurretPart>();
        private readonly List<Primitive> _temporaryPrimitives = new List<Primitive>();
        private readonly List<ToyLight> _temporaryLights = new List<ToyLight>();
        private readonly Dictionary<ushort, CannonGrenade> _cannonGrenades = new Dictionary<ushort, CannonGrenade>();

        private readonly Vector3 _adminTowerCenter;
        private Primitive _turretBase;
        private Primitive _laserCore;
        private Primitive _minigunBarrelLeft;
        private Primitive _minigunBarrelRight;
        private Primitive _cannonBarrel;
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

            foreach (CannonGrenade cannonGrenade in new List<CannonGrenade>(_cannonGrenades.Values))
                SafeDestroy(cannonGrenade.Projectile);
            _cannonGrenades.Clear();

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
            _cannonBarrel = null;
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
                BeamCollisionMask,
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

        public bool IsTowerWithinSphere(Vector3 position, float radius)
        {
            if (!IsActive || radius <= 0f)
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

        public void RetaliateCannon(Player target, ProjectileType grenadeType)
        {
            if (grenadeType != ProjectileType.FragGrenade && grenadeType != ProjectileType.Flashbang)
            {
                Log.Debug($"Cannon ignored unsupported projectile type '{grenadeType}'.");
                return;
            }

            if (!TryBeginRetaliation(target))
                return;

            int generation = _generation;
            Timing.RunCoroutine(RetaliationCannonRoutine(target, grenadeType, generation));
        }

        public bool TryIsolateCannonExplosion(ExplodingGrenadeEventArgs ev)
        {
            if (ev?.Projectile == null
                || !_cannonGrenades.TryGetValue(ev.Projectile.Serial, out CannonGrenade cannonGrenade))
                return false;

            _cannonGrenades.Remove(ev.Projectile.Serial);
            ev.TargetsToAffect.Clear();

            if (IsValidTarget(cannonGrenade.Target))
                ev.TargetsToAffect.Add(cannonGrenade.Target);

            if (Plugin.Instance.Config.Debug)
            {
                string targetName = IsValidTarget(cannonGrenade.Target)
                    ? cannonGrenade.Target.Nickname
                    : "nobody (the attacker is no longer eligible)";
                Log.Debug($"Isolated cannon {ev.Projectile.ProjectileType} explosion to {targetName}.");
            }

            return true;
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
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.85f, 0.55f), Quaternion.identity, new Vector3(0.9f, 0.65f, 0.9f), new Color(0.12f, 0.18f, 0.22f));
            _cannonBarrel = AddPart(PrimitiveType.Cylinder, new Vector3(0f, 1.15f, 1.65f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.65f, 1.4f, 0.65f), new Color(0.08f, 0.1f, 0.12f));
            AddPart(PrimitiveType.Cylinder, new Vector3(0f, 1.15f, 3.1f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.85f, 0.25f, 0.85f), new Color(0.45f, 0.2f, 0.05f));

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

        private IEnumerator<float> RetaliationCannonRoutine(Player target, ProjectileType grenadeType, int generation)
        {
            try
            {
                if (!CanContinueRetaliation(target, generation) || !IsToyAlive(_cannonBarrel))
                    yield break;

                Vector3 targetDirection = TargetPoint(target) - _turretPivot;
                if (targetDirection.sqrMagnitude < 0.0001f)
                    yield break;

                AimTurret(Quaternion.LookRotation(targetDirection));
                if (IsToyAlive(_turretLight))
                {
                    _turretLight.Color = new Color(1f, 0.15f, 0.8f);
                    _turretLight.Intensity = 10f;
                }

                yield return Timing.WaitForSeconds(0.2f);

                if (!CanContinueRetaliation(target, generation) || !IsToyAlive(_cannonBarrel))
                    yield break;

                if (!TryLaunchCannonGrenade(target, grenadeType))
                    yield break;

                yield return Timing.WaitForSeconds(0.25f);
            }
            finally
            {
                EndRetaliation(generation);
            }
        }

        private bool TryLaunchCannonGrenade(Player target, ProjectileType grenadeType)
        {
            Projectile projectile = null;

            try
            {
                Vector3 directionAtFire = TargetPoint(target) - _turretPivot;
                if (directionAtFire.sqrMagnitude < 0.0001f)
                    return false;

                AimTurret(Quaternion.LookRotation(directionAtFire));
                Vector3 launchDirection = directionAtFire.normalized;
                if (!TryFindCannonLaunchPosition(launchDirection, out Vector3 muzzlePosition))
                {
                    Log.Warn("Cannon launch was cancelled because no collision-free muzzle position was available.");
                    return false;
                }

                float flightTime = Plugin.Instance.Config.CannonFlightTime;
                Vector3 destination = target.Position + (Vector3.up * 0.25f);
                Vector3 launchVelocity = ((destination - muzzlePosition) / flightTime)
                    - (0.5f * Physics.gravity * flightTime);

                projectile = Projectile.Create(grenadeType);
                if (!(projectile is TimeGrenadeProjectile timedGrenade))
                {
                    SafeDestroy(projectile);
                    return false;
                }

                timedGrenade.FuseTime = flightTime;
                Quaternion launchRotation = launchVelocity.sqrMagnitude < 0.0001f
                    ? _currentTurretRotation
                    : Quaternion.LookRotation(launchVelocity);
                projectile.Spawn(muzzlePosition, launchRotation, false, target);

                if (projectile.Rigidbody == null)
                {
                    SafeDestroy(projectile);
                    return false;
                }

                IgnoreTowerCollisions(projectile);

                _cannonGrenades[projectile.Serial] = new CannonGrenade
                {
                    Projectile = projectile,
                    Target = target,
                };

                Vector3 launchTorque = new Vector3(7f, 5f, 3f);
                projectile.Rigidbody.linearVelocity = launchVelocity;
                projectile.Rigidbody.angularVelocity = launchTorque;
                projectile.Base.ServerOnThrown(launchTorque, launchVelocity);
                projectile.Activate();

                if (Plugin.Instance.Config.Debug)
                    Log.Debug($"Cannon launched {grenadeType} at {target.Nickname} from {muzzlePosition} with velocity {launchVelocity} and fuse {flightTime:0.00}s.");

                return true;
            }
            catch (Exception exception)
            {
                if (projectile != null)
                {
                    _cannonGrenades.Remove(projectile.Serial);
                    SafeDestroy(projectile);
                }

                Log.Error($"Cannon launch failed: {exception}");
                return false;
            }
        }

        private bool TryFindCannonLaunchPosition(Vector3 launchDirection, out Vector3 launchPosition)
        {
            for (float offset = CannonMuzzleOffset; offset <= CannonMaximumClearanceOffset; offset += 0.25f)
            {
                launchPosition = _cannonBarrel.Position + (launchDirection * offset);
                Collider[] overlaps = Physics.OverlapSphere(
                    launchPosition,
                    CannonLaunchClearanceRadius,
                    BeamCollisionMask,
                    QueryTriggerInteraction.Ignore);

                if (overlaps.Length == 0)
                    return true;
            }

            launchPosition = Vector3.zero;
            return false;
        }

        private void IgnoreTowerCollisions(Projectile projectile)
        {
            Collider[] projectileColliders = projectile.GameObject.GetComponentsInChildren<Collider>(true);
            foreach (Collider projectileCollider in projectileColliders)
            {
                foreach (Primitive barricade in _barricades)
                    IgnorePrimitiveCollision(projectileCollider, barricade);

                IgnorePrimitiveCollision(projectileCollider, _turretBase);
                foreach (TurretPart part in _compoundParts)
                    IgnorePrimitiveCollision(projectileCollider, part.Primitive);
            }
        }

        private static void IgnorePrimitiveCollision(Collider projectileCollider, Primitive primitive)
        {
            if (projectileCollider == null || !IsToyAlive(primitive))
                return;

            foreach (Collider towerCollider in primitive.GameObject.GetComponentsInChildren<Collider>(true))
            {
                if (towerCollider != null)
                    Physics.IgnoreCollision(projectileCollider, towerCollider, true);
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

        private static void SafeDestroy(Projectile projectile)
        {
            if (projectile == null || projectile.Base == null)
                return;

            try
            {
                projectile.Destroy();
            }
            catch (Exception exception)
            {
                Log.Debug($"Failed to destroy a cannon projectile cleanly: {exception.Message}");
            }
        }
    }
}
