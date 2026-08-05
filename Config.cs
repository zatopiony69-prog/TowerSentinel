using System.ComponentModel;
using Exiled.API.Interfaces;

namespace TowerSentinel
{
    public class Config : IConfig
    {
        [Description("Whether or not the plugin is enabled.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Whether or not to show debug messages.")]
        public bool Debug { get; set; } = false;

        [Description("The damage dealt by the laser turret.")]
        public float TurretDamage { get; set; } = 100f;

        [Description("How close an eligible player must be to the configured tower position to deploy the sentinel (in meters).")]
        public float TowerActivationRadius { get; set; } = 15f;

        [Description("Whether any alive role can activate and receive protection from the tower while inside its activation radius. When false, only Tutorial players can activate and receive protection.")]
        public bool AnyRoleCanActivateTower { get; set; } = true;

        [Description("Whether Tutorial players who attack the tower or a protected occupant can be targeted by retaliation.")]
        public bool CanTargetTutorialAttackers { get; set; } = false;

        [Description("The maximum distance at which a shot can hit the tower (in meters).")]
        public float ShootingDetectionRange { get; set; } = 200f;

        [Description("The radius used to determine whether a grenade explosion touched the tower (in meters).")]
        public float GrenadeDetectionRadius { get; set; } = 8f;

        [Description("How long a retaliatory cannon grenade stays in flight before detonating (in seconds).")]
        public float CannonFlightTime { get; set; } = 1.75f;

        [Description("The minimum delay between retaliation attacks (in seconds).")]
        public float RetaliationCooldown { get; set; } = 0.75f;

        [Description("The local X position of the admin tower relative to the Surface room.")]
        public float AdminTowerOffsetX { get; set; } = 39f;

        [Description("The local Y position of the admin tower relative to the Surface room.")]
        public float AdminTowerOffsetY { get; set; } = 14f;

        [Description("The local Z position of the admin tower relative to the Surface room.")]
        public float AdminTowerOffsetZ { get; set; } = -32f;

        [Description("The width of the barricade box (X axis).")]
        public float BarricadeWidth { get; set; } = 10f;

        [Description("The depth of the barricade box (Z axis).")]
        public float BarricadeDepth { get; set; } = 10f;

        [Description("The height of the barricade box (Y axis).")]
        public float BarricadeHeight { get; set; } = 3.4f;

        [Description("The X offset of the turret from the center of the roof.")]
        public float TurretOffsetX { get; set; } = -4.5f;

        [Description("The Z offset of the turret from the center of the roof.")]
        public float TurretOffsetZ { get; set; } = 0f;

        [Description("The damage dealt by a single minigun tracer bullet.")]
        public float MinigunDamage { get; set; } = 15f;

        [Description("The Y offset to apply to the entire structure (push it up or down).")]
        public float StructureYOffset { get; set; } = -1.5f;
    }
}
