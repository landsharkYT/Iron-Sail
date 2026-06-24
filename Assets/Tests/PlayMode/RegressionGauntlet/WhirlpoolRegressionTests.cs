using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class WhirlpoolRegressionTests
{
    const float Radius = 4f;

    [Test]
    public void VortexForceIsZeroOutsideRadiusAndAtEye()
    {
        // Outside the radius: no pull.
        Vector2 outside = InvokeVortexForce(new Vector2(Radius + 1f, 0f), Radius, 10f, 0.5f);
        Assert.That(outside, Is.EqualTo(Vector2.zero));

        // Exactly at the eye: no singularity, no force.
        Vector2 eye = InvokeVortexForce(Vector2.zero, Radius, 10f, 0.5f);
        Assert.That(eye, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void VortexForcePullsInwardAndSwirls()
    {
        Vector2 offset = new Vector2(2f, 0f); // east of the eye
        Vector2 force = InvokeVortexForce(offset, Radius, 10f, 0.6f);

        // Radial component points back toward the eye (negative x here).
        Assert.Less(force.x, 0f, "Whirlpool should pull the boat toward its centre.");
        // Swirl gives a tangential (y) component.
        Assert.AreNotEqual(0f, force.y, "A non-zero swirl ratio should add a tangential component.");
    }

    [Test]
    public void VortexPullIsStrongerNearerTheEye()
    {
        float nearRim = InvokeVortexForce(new Vector2(Radius * 0.9f, 0f), Radius, 10f, 0f).magnitude;
        float nearEye = InvokeVortexForce(new Vector2(Radius * 0.2f, 0f), Radius, 10f, 0f).magnitude;
        Assert.Greater(nearEye, nearRim, "Pull must ramp up toward the eye (rim-weak, centre-strong).");
    }

    [Test]
    public void DamageRateRampsFromRimToEye()
    {
        float rim = InvokeDamageRate(Radius, Radius, 1.5f, 6f);
        float eye = InvokeDamageRate(0f, Radius, 1.5f, 6f);
        float mid = InvokeDamageRate(Radius * 0.5f, Radius, 1.5f, 6f);

        Assert.That(rim, Is.EqualTo(1.5f).Within(0.001f), "Rim damage should equal the rim rate.");
        Assert.That(eye, Is.EqualTo(6f).Within(0.001f), "Eye damage should equal the eye rate.");
        Assert.Greater(mid, rim);
        Assert.Less(mid, eye);
    }

    [Test]
    public void StraitQualifiesWithinGapBandAndPlacesMidpointInGap()
    {
        // Islands r=2 at x=0 and x=20 -> edge gap = 16, within [14,40].
        bool ok = InvokeTryEvaluateStrait(
            new Vector2(0f, 0f), 2f, new Vector2(20f, 0f), 2f, 14f, 40f,
            out Vector2 midpoint, out float gap);

        Assert.IsTrue(ok);
        Assert.That(gap, Is.EqualTo(16f).Within(0.001f));
        // Midpoint sits halfway across the open gap: edge(2) + gap/2(8) = x=10.
        Assert.That(midpoint.x, Is.EqualTo(10f).Within(0.001f));
        Assert.That(midpoint.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void StraitRejectsGapsOutsideTheBand()
    {
        // Too narrow: gap = 1.
        Assert.IsFalse(InvokeTryEvaluateStrait(
            new Vector2(0f, 0f), 2f, new Vector2(5f, 0f), 2f, 14f, 40f, out _, out _),
            "Overlapping/too-narrow islands should not form a strait.");

        // Too wide: gap = 96.
        Assert.IsFalse(InvokeTryEvaluateStrait(
            new Vector2(0f, 0f), 2f, new Vector2(100f, 0f), 2f, 14f, 40f, out _, out _),
            "Islands beyond the gap band are open water, not a strait.");
    }

    // --- reflection helpers (match SaveStateRegressionTests' approach) --------

    static Vector2 InvokeVortexForce(Vector2 offset, float radius, float maxPull, float swirl)
    {
        MethodInfo method = ResolveType("WhirlpoolController")
            .GetMethod("EvaluateVortexForce", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, "WhirlpoolController.EvaluateVortexForce must exist.");
        return (Vector2)method.Invoke(null, new object[] { offset, radius, maxPull, swirl });
    }

    static float InvokeDamageRate(float r, float radius, float rimRate, float eyeRate)
    {
        MethodInfo method = ResolveType("WhirlpoolController")
            .GetMethod("EvaluateDamageRate", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, "WhirlpoolController.EvaluateDamageRate must exist.");
        return (float)method.Invoke(null, new object[] { r, radius, rimRate, eyeRate });
    }

    static bool InvokeTryEvaluateStrait(
        Vector2 centerA, float radiusA, Vector2 centerB, float radiusB,
        float minGap, float maxGap, out Vector2 midpoint, out float gap)
    {
        MethodInfo method = ResolveType("WhirlpoolSpawner")
            .GetMethod("TryEvaluateStrait", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, "WhirlpoolSpawner.TryEvaluateStrait must exist.");

        object[] args = { centerA, radiusA, centerB, radiusB, minGap, maxGap, null, null };
        bool result = (bool)method.Invoke(null, args);
        midpoint = (Vector2)args[6];
        gap = (float)args[7];
        return result;
    }

    static Type ResolveType(string typeName)
    {
        Type type = Type.GetType(typeName + ", Assembly-CSharp") ?? Type.GetType(typeName);
        Assert.NotNull(type, $"Could not resolve runtime type {typeName}.");
        return type;
    }
}
