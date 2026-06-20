using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class SaveStateRegressionTests
{
    [Test]
    public void DayNightRestoreTimeStateRestoresDayCountAndNormalizedTime()
    {
        Type dayNightType = ResolveType("DayNightController");
        GameObject host = new GameObject("DayNightController Test Host");

        try
        {
            Component dayNight = host.AddComponent(dayNightType);
            dayNightType.GetMethod("RestoreTimeState")?.Invoke(dayNight, new object[] { 5, 1.25f });

            Assert.AreEqual(5, GetIntProperty(dayNight, "DayCount"));
            Assert.That(GetFloatProperty(dayNight, "NormalizedTimeOfDay"), Is.EqualTo(0.25f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void SaveDtosExposeDayCountAndPlaytimeFields()
    {
        Type worldStateType = ResolveType("WorldStateSaveData");
        Type progressType = ResolveType("ProgressSaveData");

        Assert.NotNull(worldStateType.GetField("dayCount"), "WorldStateSaveData must persist the internal elapsed-day counter.");
        Assert.NotNull(progressType.GetField("playtimeSeconds"), "ProgressSaveData must persist active gameplay playtime.");
    }

    [Test]
    public void PlaytimeRestoreClampsNegativeValues()
    {
        Type playtimeType = ResolveType("PlaytimeController");
        object playtime = playtimeType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        GameObject host = null;

        try
        {
            if (playtime == null)
            {
                host = new GameObject("PlaytimeController Test Host");
                playtime = host.AddComponent(playtimeType);
            }

            playtimeType.GetMethod("RestorePlaytimeSeconds")?.Invoke(playtime, new object[] { 125 });
            Assert.AreEqual(125, GetIntProperty(playtime, "TotalPlaytimeSeconds"));

            playtimeType.GetMethod("RestorePlaytimeSeconds")?.Invoke(playtime, new object[] { -7 });
            Assert.AreEqual(0, GetIntProperty(playtime, "TotalPlaytimeSeconds"));
        }
        finally
        {
            if (host != null)
                UnityEngine.Object.DestroyImmediate(host);
        }
    }

    static Type ResolveType(string typeName)
    {
        Type type = Type.GetType(typeName + ", Assembly-CSharp") ?? Type.GetType(typeName);
        Assert.NotNull(type, $"Could not resolve runtime type {typeName}.");
        return type;
    }

    static int GetIntProperty(object target, string propertyName)
    {
        return (int)target.GetType().GetProperty(propertyName).GetValue(target);
    }

    static float GetFloatProperty(object target, string propertyName)
    {
        return (float)target.GetType().GetProperty(propertyName).GetValue(target);
    }
}
