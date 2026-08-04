using System;
using Exiled.API.Features;
using PlayerEvents = Exiled.Events.Handlers.Player;
using MapEvents = Exiled.Events.Handlers.Map;
using ServerEvents = Exiled.Events.Handlers.Server;

namespace TowerSentinel
{
    public class Plugin : Plugin<Config>
    {
        public override string Name => "TowerSentinel";
        public override string Author => "zatopiony69-prog";
        public override string Prefix => "TowerSentinel";
        public override Version Version => new Version(1, 0, 0);
        public override Version RequiredExiledVersion => new Version(9, 14, 2);

        public static Plugin Instance { get; private set; }
        private EventHandlers _eventHandlers;

        public override void OnEnabled()
        {
            Instance = this;
            ValidateConfig();
            _eventHandlers = new EventHandlers();

            PlayerEvents.Shooting += _eventHandlers.OnShooting;
            PlayerEvents.Hurting += _eventHandlers.OnHurting;
            MapEvents.ExplodingGrenade += _eventHandlers.OnExplodingGrenade;
            ServerEvents.RoundStarted += _eventHandlers.OnRoundStarted;
            ServerEvents.RoundEnded += _eventHandlers.OnRoundEnded;

            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            if (_eventHandlers != null)
            {
                PlayerEvents.Shooting -= _eventHandlers.OnShooting;
                PlayerEvents.Hurting -= _eventHandlers.OnHurting;
                MapEvents.ExplodingGrenade -= _eventHandlers.OnExplodingGrenade;
                ServerEvents.RoundStarted -= _eventHandlers.OnRoundStarted;
                ServerEvents.RoundEnded -= _eventHandlers.OnRoundEnded;

                _eventHandlers.Cleanup();
            }

            _eventHandlers = null;
            Instance = null;

            base.OnDisabled();
        }

        private void ValidateConfig()
        {
            Config.TurretDamage = Normalize(nameof(Config.TurretDamage), Config.TurretDamage, 0f, 10000f, 100f);
            Config.MinigunDamage = Normalize(nameof(Config.MinigunDamage), Config.MinigunDamage, 0f, 10000f, 15f);
            Config.TowerActivationRadius = Normalize(nameof(Config.TowerActivationRadius), Config.TowerActivationRadius, 0.5f, 100f, 15f);
            Config.ShootingDetectionRange = Normalize(nameof(Config.ShootingDetectionRange), Config.ShootingDetectionRange, 1f, 500f, 200f);
            Config.GrenadeDetectionRadius = Normalize(nameof(Config.GrenadeDetectionRadius), Config.GrenadeDetectionRadius, 0.5f, 50f, 8f);
            Config.RetaliationCooldown = Normalize(nameof(Config.RetaliationCooldown), Config.RetaliationCooldown, 0.05f, 30f, 0.75f);
            Config.BarricadeWidth = Normalize(nameof(Config.BarricadeWidth), Config.BarricadeWidth, 1f, 50f, 10f);
            Config.BarricadeDepth = Normalize(nameof(Config.BarricadeDepth), Config.BarricadeDepth, 1f, 50f, 10f);
            Config.BarricadeHeight = Normalize(nameof(Config.BarricadeHeight), Config.BarricadeHeight, 1f, 25f, 3.4f);
            Config.TurretOffsetX = Normalize(nameof(Config.TurretOffsetX), Config.TurretOffsetX, -50f, 50f, -4.5f);
            Config.TurretOffsetZ = Normalize(nameof(Config.TurretOffsetZ), Config.TurretOffsetZ, -50f, 50f, 0f);
            Config.StructureYOffset = Normalize(nameof(Config.StructureYOffset), Config.StructureYOffset, -25f, 25f, -1.5f);
            Config.AdminTowerOffsetX = Normalize(nameof(Config.AdminTowerOffsetX), Config.AdminTowerOffsetX, -500f, 500f, 39f);
            Config.AdminTowerOffsetY = Normalize(nameof(Config.AdminTowerOffsetY), Config.AdminTowerOffsetY, -500f, 500f, 14f);
            Config.AdminTowerOffsetZ = Normalize(nameof(Config.AdminTowerOffsetZ), Config.AdminTowerOffsetZ, -500f, 500f, -32f);
        }

        private static float Normalize(string name, float value, float minimum, float maximum, float fallback)
        {
            float normalized = Guardrails.Normalize(value, minimum, maximum, fallback, out bool changed);

            if (changed)
                Log.Warn($"Invalid {name} value '{value}' was replaced with '{normalized}'.");

            return normalized;
        }
    }
}
