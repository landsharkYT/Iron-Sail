using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MapTruthRegressionTests
{
    const int FixedSeed = 385;
    const string GameplaySceneName = "SampleScene";

    Type islandGenerationType;
    Type islandSourceType;
    Type mapDiscoveryType;
    Type boatControllerType;

    FieldInfo sourceCenterField;
    FieldInfo sourceMaxRadiusField;
    FieldInfo sourceIsTreasureField;
    FieldInfo sourceDeterministicKeyField;

    [UnityTest]
    public IEnumerator DiscoveredIslandTilesNearBoatResolveToRealWorldTruth()
    {
        ResolveRuntimeTypes();
        islandGenerationType.GetMethod("SetDiagnosticPlaySeedOverride", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, new object[] { FixedSeed, false });

        yield return SceneManager.LoadSceneAsync(GameplaySceneName, LoadSceneMode.Single);
        yield return WaitForSceneStartup();

        object islandGeneration = FindRequiredObject(islandGenerationType, "IslandGenerationController");
        object mapDiscovery = FindRequiredObject(mapDiscoveryType, "MapDiscoveryController");
        object boat = FindRequiredObject(boatControllerType, "BoatController");

        Assert.True(TryFindNearbyIslandSource(islandGeneration, GetFloatProperty(mapDiscovery, "WorldOuterRadiusTiles"), out object source),
            $"No accepted island source found for seed {GetIntProperty(islandGeneration, "Seed")}.");

        Vector2 samplePosition = GetDiscoveryPositionNearSource(source, mapDiscovery);
        MoveBoatTo(boat, samplePosition);
        SnapCameraNear(samplePosition);

        yield return WaitForDiscoveryToSettle();

        int sampledTruthCells = (int)mapDiscoveryType
            .GetMethod("CountMapTruthCategoriesNearWorldPosition")
            .Invoke(mapDiscovery, new object[] { samplePosition, 2 });

        IList truthFailures = CreateGenericList(mapDiscoveryType.GetNestedType("ChartTruthDiagnostic"));
        int failureCount = (int)mapDiscoveryType
            .GetMethod("CollectChartTruthFailuresNearWorldPosition")
            .Invoke(mapDiscovery, new object[] { samplePosition, truthFailures, 8, 2 });

        Assert.Greater(sampledTruthCells, 0, BuildNoIslandDiagnostics(islandGeneration, source, samplePosition));
        Assert.Zero(failureCount, BuildFailureDiagnostics(islandGeneration, source, samplePosition, truthFailures));
    }

    void ResolveRuntimeTypes()
    {
        islandGenerationType = ResolveType("IslandGenerationController");
        islandSourceType = islandGenerationType.GetNestedType("IslandSourceDescriptor");
        mapDiscoveryType = ResolveType("MapDiscoveryController");
        boatControllerType = ResolveType("BoatController");

        sourceCenterField = islandSourceType.GetField("center");
        sourceMaxRadiusField = islandSourceType.GetField("maxRadius");
        sourceIsTreasureField = islandSourceType.GetField("isTreasure");
        sourceDeterministicKeyField = islandSourceType.GetField("deterministicKey");
    }

    static Type ResolveType(string typeName)
    {
        Type type = Type.GetType(typeName + ", Assembly-CSharp") ?? Type.GetType(typeName);
        Assert.NotNull(type, $"Could not resolve runtime type {typeName}.");
        return type;
    }

    static object FindRequiredObject(Type type, string name)
    {
        UnityEngine.Object found = UnityEngine.Object.FindAnyObjectByType(type);
        Assert.NotNull(found, $"SampleScene must contain a {name}.");
        return found;
    }

    static IEnumerator WaitForSceneStartup()
    {
        for (int i = 0; i < 8; i++)
            yield return null;
    }

    static IEnumerator WaitForDiscoveryToSettle()
    {
        for (int i = 0; i < 90; i++)
            yield return null;
    }

    bool TryFindNearbyIslandSource(object islandGeneration, float worldRadius, out object selectedSource)
    {
        selectedSource = null;
        IList sources = CreateGenericList(islandSourceType);
        MethodInfo collect = islandGenerationType.GetMethod("CollectAcceptedIslandSourcesNearWorldPosition");

        float searchRadius = 512f;
        float maxSearchRadius = Mathf.Max(searchRadius, worldRadius);
        while (searchRadius <= maxSearchRadius)
        {
            sources.Clear();
            collect.Invoke(islandGeneration, new object[] { Vector2.zero, searchRadius, sources });
            if (TrySelectSource(sources, out selectedSource))
                return true;

            searchRadius *= 2f;
        }

        sources.Clear();
        collect.Invoke(islandGeneration, new object[] { Vector2.zero, maxSearchRadius, sources });
        return TrySelectSource(sources, out selectedSource);
    }

    bool TrySelectSource(IList sources, out object selectedSource)
    {
        selectedSource = null;
        float selectedDistanceSqr = float.MaxValue;

        foreach (object source in sources)
        {
            if ((bool)sourceIsTreasureField.GetValue(source))
                continue;

            Vector2 center = (Vector2)sourceCenterField.GetValue(source);
            float distanceSqr = center.sqrMagnitude;
            if (distanceSqr >= selectedDistanceSqr)
                continue;

            selectedSource = source;
            selectedDistanceSqr = distanceSqr;
        }

        return selectedSource != null;
    }

    Vector2 GetDiscoveryPositionNearSource(object source, object mapDiscovery)
    {
        Vector2 center = (Vector2)sourceCenterField.GetValue(source);
        float maxRadius = (float)sourceMaxRadiusField.GetValue(source);
        float revealRadius = GetFloatProperty(mapDiscovery, "RevealRadiusWorld");

        Vector2 direction = center.sqrMagnitude > 0.01f
            ? center.normalized
            : Vector2.right;

        float distanceFromCenter = Mathf.Min(
            Mathf.Max(maxRadius + 70f, maxRadius + 45f),
            Mathf.Max(maxRadius + 8f, revealRadius - 12f));

        return (Vector2)mapDiscoveryType
            .GetMethod("ClampWorldPositionToBounds")
            .Invoke(mapDiscovery, new object[] { center + direction * distanceFromCenter });
    }

    static void MoveBoatTo(object boat, Vector2 worldPosition)
    {
        Component component = (Component)boat;
        Transform boatTransform = component.transform;
        boatTransform.position = new Vector3(worldPosition.x, worldPosition.y, boatTransform.position.z);

        Rigidbody2D rigidbody = component.GetComponent<Rigidbody2D>();
        if (rigidbody == null)
            return;

        rigidbody.position = worldPosition;
        rigidbody.linearVelocity = Vector2.zero;
        rigidbody.angularVelocity = 0f;
    }

    static void SnapCameraNear(Vector2 worldPosition)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            mainCamera.transform.position = new Vector3(worldPosition.x, worldPosition.y, mainCamera.transform.position.z);

        GameObject cameraTarget = GameObject.Find("CameraTarget");
        if (cameraTarget != null)
            cameraTarget.transform.position = new Vector3(worldPosition.x, worldPosition.y, cameraTarget.transform.position.z);
    }

    string BuildNoIslandDiagnostics(object islandGeneration, object source, Vector2 samplePosition)
    {
        return $"Map Truth sampled no island/dock/treasure chart cells near boat. seed={GetIntProperty(islandGeneration, "Seed")} sample={samplePosition} sourceKey={sourceDeterministicKeyField.GetValue(source)} sourceCenter={sourceCenterField.GetValue(source)} sourceRadius={(float)sourceMaxRadiusField.GetValue(source):0.0}";
    }

    string BuildFailureDiagnostics(object islandGeneration, object source, Vector2 samplePosition, IList failures)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"Map Truth found fake chart cells. seed={GetIntProperty(islandGeneration, "Seed")} sample={samplePosition} sourceKey={sourceDeterministicKeyField.GetValue(source)} sourceCenter={sourceCenterField.GetValue(source)} sourceRadius={(float)sourceMaxRadiusField.GetValue(source):0.0}");
        foreach (object failure in failures)
            builder.AppendLine(failure.ToString());

        return builder.ToString();
    }

    static IList CreateGenericList(Type itemType)
    {
        return (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
    }

    static float GetFloatProperty(object target, string propertyName)
    {
        return (float)target.GetType().GetProperty(propertyName).GetValue(target);
    }

    static int GetIntProperty(object target, string propertyName)
    {
        return (int)target.GetType().GetProperty(propertyName).GetValue(target);
    }
}
