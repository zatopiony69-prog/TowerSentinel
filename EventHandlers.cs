using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Map;
using InventorySystem.Items.Jailbird;
using InventorySystem.Items.MicroHID.Modules;
using LabApi.Events.Arguments.PlayerEvents;
using MEC;
using PlayerRoles;
using PlayerStatsSystem;
using UnityEngine;

namespace TowerSentinel
{
    public class EventHandlers
    {
        private CoroutineHandle _monitorCoroutine;
        private SentinelController _sentinelController;
        private Vector3 _adminTowerPosition = Vector3.zero;

        public void OnRoundStarted()
        {
            Cleanup();

            var surfaceRoom = Room.List.FirstOrDefault(r => r.Type == RoomType.Surface);
            if (surfaceRoom == null)
            {
                Log.Error("Cannot deploy TowerSentinel because the Surface room was not found.");
                return;
            }

            Config config = Plugin.Instance.Config;
            _adminTowerPosition = surfaceRoom.WorldPosition(new Vector3(
                config.AdminTowerOffsetX,
                config.AdminTowerOffsetY,
                config.AdminTowerOffsetZ));

            _sentinelController = new SentinelController(_adminTowerPosition);
            _monitorCoroutine = Timing.RunCoroutine(MonitorTower());
        }

        public void OnRoundEnded(Exiled.Events.EventArgs.Server.RoundEndedEventArgs ev)
        {
            Cleanup();
        }

        public void Cleanup()
        {
            Timing.KillCoroutines(_monitorCoroutine);
            _monitorCoroutine = default(CoroutineHandle);

            if (_sentinelController != null)
            {
                _sentinelController.Deactivate();
                _sentinelController = null;
            }
        }

        private IEnumerator<float> MonitorTower()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(1f);

                if (_sentinelController == null) yield break;

                Config config = Plugin.Instance.Config;
                bool isActivatorInTower = false;
                foreach (var player in Player.List)
                {
                    if (player.IsConnected
                        && player.IsAlive
                        && Guardrails.CanActivateTower(
                            player.Role.Type == RoleTypeId.Tutorial,
                            config.AnyRoleCanActivateTower))
                    {
                        if (Guardrails.IsWithinRadius(
                            (player.Position - _adminTowerPosition).sqrMagnitude,
                            config.TowerActivationRadius))
                        {
                            isActivatorInTower = true;
                            break;
                        }
                    }
                }

                if (isActivatorInTower)
                {
                    if (!_sentinelController.IsActive)
                    {
                        _sentinelController.Activate();
                    }
                }
                else
                {
                    if (_sentinelController.IsActive)
                    {
                        _sentinelController.Deactivate();
                    }
                }
            }
        }

        public void OnShooting(ShootingEventArgs ev)
        {
            if (!ev.IsAllowed || _sentinelController == null || !_sentinelController.IsActive || ev.Player == null) return;

            Config config = Plugin.Instance.Config;
            if (!Guardrails.CanTargetAttacker(
                ev.Player.Role.Type == RoleTypeId.Tutorial,
                config.CanTargetTutorialAttackers)) return;

            Vector3 camPos = ev.Player.CameraTransform.position;
            if (_sentinelController.WasTowerHit(camPos, ev.Direction, config.ShootingDetectionRange, ev.Player))
            {
                if (ev.Firearm.Type == ItemType.ParticleDisruptor)
                    _sentinelController.RetaliateLaser(ev.Player);
                else
                    _sentinelController.RetaliateMinigun(ev.Player);
            }
        }

        public void OnUsingMicroHidEnergy(UsingMicroHIDEnergyEventArgs ev)
        {
            if (!ev.IsAllowed
                || ev.Drain <= 0f
                || ev.MicroHID == null
                || ev.MicroHID.State != MicroHidPhase.Firing
                || !CanTarget(ev.Player)) return;

            float firingRange = 10f;
            if (ev.MicroHID.TryGetLastFireController(out FiringModeControllerModule firingController))
                firingRange = firingController.FiringRange;

            bool towerHit = ev.MicroHID.LastFiringMode == MicroHidFiringMode.BrokenFire
                ? _sentinelController.IsTowerWithinSphere(ev.Player.Position, firingRange)
                : _sentinelController.WasTowerHit(
                    ev.Player.CameraTransform.position,
                    ev.Player.CameraTransform.forward,
                    firingRange,
                    ev.Player);

            if (towerHit)
                _sentinelController.RetaliateLaser(ev.Player);
        }

        public void OnProcessedJailbirdMessage(PlayerProcessedJailbirdMessageEventArgs ev)
        {
            if (ev == null || ev.Message != JailbirdMessageType.AttackPerformed || ev.Player == null) return;

            Player attacker = Player.Get(ev.Player.ReferenceHub);
            if (!CanTarget(attacker)) return;

            const float hitregOffset = 1.5f;
            const float hitregRadius = 1.5f;
            Vector3 detectionCenter = attacker.CameraTransform.position
                + (attacker.CameraTransform.forward * hitregOffset);

            if (_sentinelController.IsTowerWithinSphere(detectionCenter, hitregRadius))
                _sentinelController.RetaliateLaser(attacker);
        }

        public void OnHurting(HurtingEventArgs ev)
        {
            if (ev.IsAllowed && ev.Amount > 0f && _sentinelController != null && _sentinelController.IsActive && ev.Player != null)
            {
                Config config = Plugin.Instance.Config;
                if (!Guardrails.CanActivateTower(
                    ev.Player.Role.Type == RoleTypeId.Tutorial,
                    config.AnyRoleCanActivateTower)) return;

                if (Guardrails.IsWithinRadius(
                    (ev.Player.Position - _adminTowerPosition).sqrMagnitude,
                    config.TowerActivationRadius))
                {
                    if (ev.Attacker != null
                        && ev.Attacker != ev.Player
                        && ev.Attacker.IsConnected
                        && ev.Attacker.IsAlive
                        && Guardrails.CanTargetAttacker(
                            ev.Attacker.Role.Type == RoleTypeId.Tutorial,
                            config.CanTargetTutorialAttackers))
                    {
                        DamageType damageType = ev.DamageHandler.Type;
                        bool hasExplosionType = ev.DamageHandler.CustomBase.Is(
                            out ExplosionDamageHandler explosionDamageHandler);
                        bool isGrenadeDamage = hasExplosionType
                            && explosionDamageHandler.ExplosionType == ExplosionType.Grenade;
                        bool isSpecialExplosion = hasExplosionType
                            && (explosionDamageHandler.ExplosionType == ExplosionType.Disruptor
                                || explosionDamageHandler.ExplosionType == ExplosionType.Jailbird);
                        DirectRetaliation retaliation = Guardrails.SelectDirectRetaliation(
                            isGrenadeDamage,
                            damageType == DamageType.ParticleDisruptor
                                || damageType == DamageType.Jailbird
                                || damageType == DamageType.MicroHid
                                || isSpecialExplosion);

                        if (retaliation == DirectRetaliation.Laser)
                            _sentinelController.RetaliateLaser(ev.Attacker);
                        else if (retaliation == DirectRetaliation.Minigun)
                            _sentinelController.RetaliateMinigun(ev.Attacker);
                    }
                }
            }
        }

        public void OnExplodingGrenade(ExplodingGrenadeEventArgs ev)
        {
            if (_sentinelController == null || !_sentinelController.IsActive) return;

            if (_sentinelController.TryIsolateCannonExplosion(ev))
                return;

            if (!ev.IsAllowed) return;

            Config config = Plugin.Instance.Config;
            if (ev.Player != null
                && ev.Player.IsConnected
                && ev.Player.IsAlive
                && Guardrails.CanTargetAttacker(
                    ev.Player.Role.Type == RoleTypeId.Tutorial,
                    config.CanTargetTutorialAttackers))
            {
                if (_sentinelController.IsExplosionTouchingTower(ev.Position, config.GrenadeDetectionRadius))
                    _sentinelController.RetaliateCannon(ev.Player, ev.Projectile.ProjectileType);
            }
        }

        private bool CanTarget(Player player)
        {
            return _sentinelController != null
                && _sentinelController.IsActive
                && player != null
                && player.IsConnected
                && player.IsAlive
                && Guardrails.CanTargetAttacker(
                    player.Role.Type == RoleTypeId.Tutorial,
                    Plugin.Instance.Config.CanTargetTutorialAttackers);
        }
    }
}
