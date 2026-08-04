using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Map;
using MEC;
using PlayerRoles;
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
                _sentinelController.RetaliateMinigun(ev.Player);
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
                        _sentinelController.RetaliateLaser(ev.Attacker);
                }
            }
        }

        public void OnExplodingGrenade(ExplodingGrenadeEventArgs ev)
        {
            if (!ev.IsAllowed || _sentinelController == null || !_sentinelController.IsActive) return;

            Config config = Plugin.Instance.Config;
            if (ev.Player != null
                && ev.Player.IsConnected
                && ev.Player.IsAlive
                && Guardrails.CanTargetAttacker(
                    ev.Player.Role.Type == RoleTypeId.Tutorial,
                    config.CanTargetTutorialAttackers))
            {
                if (_sentinelController.IsExplosionTouchingTower(ev.Position, config.GrenadeDetectionRadius))
                    _sentinelController.RetaliateLaser(ev.Player);
            }
        }
    }
}
