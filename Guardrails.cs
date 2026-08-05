using System;

namespace TowerSentinel
{
    internal enum DirectRetaliation
    {
        None,
        Minigun,
        Laser,
    }

    internal static class Guardrails
    {
        public static float Normalize(float value, float minimum, float maximum, float fallback, out bool changed)
        {
            float normalized = float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Math.Max(minimum, Math.Min(maximum, value));

            changed = !normalized.Equals(value);
            return normalized;
        }

        public static bool IsWithinRadius(float squaredDistance, float radius)
        {
            return radius > 0f && squaredDistance <= radius * radius;
        }

        public static bool CanActivateTower(bool isTutorial, bool anyRoleCanActivateTower)
        {
            return anyRoleCanActivateTower || isTutorial;
        }

        public static bool CanTargetAttacker(bool isTutorial, bool canTargetTutorialAttackers)
        {
            return !isTutorial || canTargetTutorialAttackers;
        }

        public static DirectRetaliation SelectDirectRetaliation(bool isGrenadeDamage, bool isSpecialWeaponDamage)
        {
            if (isGrenadeDamage)
                return DirectRetaliation.None;

            return isSpecialWeaponDamage
                ? DirectRetaliation.Laser
                : DirectRetaliation.Minigun;
        }

        public static bool CanStartRetaliation(bool isActive, bool isFiring, bool hasValidTarget, float currentTime, float nextAllowedTime)
        {
            return isActive && !isFiring && hasValidTarget && currentTime >= nextAllowedTime;
        }
    }
}
