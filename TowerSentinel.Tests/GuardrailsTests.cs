using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TowerSentinel.Tests
{
    [TestClass]
    public class GuardrailsTests
    {
        [TestMethod]
        [DataRow(float.NaN)]
        [DataRow(float.PositiveInfinity)]
        [DataRow(float.NegativeInfinity)]
        public void NormalizeReplacesNonFiniteValues(float value)
        {
            float result = Guardrails.Normalize(value, 1f, 10f, 5f, out bool changed);

            Assert.AreEqual(5f, result);
            Assert.IsTrue(changed);
        }

        [TestMethod]
        [DataRow(-1f, 1f)]
        [DataRow(20f, 10f)]
        [DataRow(4f, 4f)]
        public void NormalizeClampsToConfiguredRange(float value, float expected)
        {
            float result = Guardrails.Normalize(value, 1f, 10f, 5f, out bool changed);

            Assert.AreEqual(expected, result);
            Assert.AreEqual(value != expected, changed);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void NormalizeTextReplacesMissingValues(string value)
        {
            string result = Guardrails.NormalizeText(value, "Heavy Laser", out bool changed);

            Assert.AreEqual("Heavy Laser", result);
            Assert.IsTrue(changed);
        }

        [TestMethod]
        public void NormalizeTextTrimsButKeepsConfiguredValue()
        {
            string trimmed = Guardrails.NormalizeText("Dual Minigun", "Fallback", out bool unchanged);
            Assert.AreEqual("Dual Minigun", trimmed);
            Assert.IsFalse(unchanged);

            string padded = Guardrails.NormalizeText("  Dual Minigun  ", "Fallback", out bool changed);
            Assert.AreEqual("Dual Minigun", padded);
            Assert.IsTrue(changed);
        }

        [TestMethod]
        public void CanSendHitMessageRespectsTypeContentAndCooldown()
        {
            Assert.IsTrue(Guardrails.CanSendHitMessage(NotificationType.Hint, "Hit", 10f, 10f));
            Assert.IsTrue(Guardrails.CanSendHitMessage(NotificationType.Broadcast, "Hit", 11f, 10f));
            Assert.IsFalse(Guardrails.CanSendHitMessage(NotificationType.None, "Hit", 10f, 10f));
            Assert.IsFalse(Guardrails.CanSendHitMessage(NotificationType.Hint, " ", 10f, 10f));
            Assert.IsFalse(Guardrails.CanSendHitMessage(NotificationType.Hint, null!, 10f, 10f));
            Assert.IsFalse(Guardrails.CanSendHitMessage(NotificationType.Hint, "Hit", 9.99f, 10f));
        }

        [TestMethod]
        public void IsWithinRadiusIncludesBoundaryAndRejectsOutsidePoint()
        {
            Assert.IsTrue(Guardrails.IsWithinRadius(225f, 15f));
            Assert.IsFalse(Guardrails.IsWithinRadius(225.01f, 15f));
            Assert.IsFalse(Guardrails.IsWithinRadius(0f, 0f));
        }

        [TestMethod]
        public void CanActivateTowerAllowsAnyRoleByDefaultPolicy()
        {
            Assert.IsTrue(Guardrails.CanActivateTower(false, true));
            Assert.IsTrue(Guardrails.CanActivateTower(true, true));
            Assert.IsTrue(Guardrails.CanActivateTower(true, false));
            Assert.IsFalse(Guardrails.CanActivateTower(false, false));
        }

        [TestMethod]
        public void CanTargetAttackerKeepsTutorialExemptByDefaultPolicy()
        {
            Assert.IsTrue(Guardrails.CanTargetAttacker(false, false));
            Assert.IsTrue(Guardrails.CanTargetAttacker(false, true));
            Assert.IsTrue(Guardrails.CanTargetAttacker(true, true));
            Assert.IsFalse(Guardrails.CanTargetAttacker(true, false));
        }

        [TestMethod]
        public void SelectDirectRetaliationReservesLaserForSpecialWeapons()
        {
            Assert.AreEqual(DirectRetaliation.Laser, Guardrails.SelectDirectRetaliation(false, true));
            Assert.AreEqual(DirectRetaliation.Minigun, Guardrails.SelectDirectRetaliation(false, false));
            Assert.AreEqual(DirectRetaliation.None, Guardrails.SelectDirectRetaliation(true, true));
            Assert.AreEqual(DirectRetaliation.None, Guardrails.SelectDirectRetaliation(true, false));
        }

        [TestMethod]
        public void CanStartRetaliationRequiresAllSafetyConditions()
        {
            Assert.IsTrue(Guardrails.CanStartRetaliation(true, false, true, 10f, 10f));
            Assert.IsFalse(Guardrails.CanStartRetaliation(false, false, true, 10f, 10f));
            Assert.IsFalse(Guardrails.CanStartRetaliation(true, true, true, 10f, 10f));
            Assert.IsFalse(Guardrails.CanStartRetaliation(true, false, false, 10f, 10f));
            Assert.IsFalse(Guardrails.CanStartRetaliation(true, false, true, 9.99f, 10f));
        }
    }
}
