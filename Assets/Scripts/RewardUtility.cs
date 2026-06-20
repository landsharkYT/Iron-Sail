using UnityEngine;

public static class RewardUtility
{
    static ItemDefinition cachedCannonballItem;

    public static ItemDefinition ResolveCannonballItem()
    {
        if (cachedCannonballItem != null)
            return cachedCannonballItem;

        cachedCannonballItem = Resources.Load<ItemDefinition>("CannonballItem");
        return cachedCannonballItem;
    }

    public static int RollWeightedRewardAmount(float twoWeight, float threeWeight, float fourWeight)
    {
        float totalWeight = Mathf.Max(0f, twoWeight) + Mathf.Max(0f, threeWeight) + Mathf.Max(0f, fourWeight);
        if (totalWeight <= 0f)
            return 0;

        float roll = Random.value * totalWeight;
        roll -= Mathf.Max(0f, twoWeight);
        if (roll <= 0f)
            return 2;

        roll -= Mathf.Max(0f, threeWeight);
        if (roll <= 0f)
            return 3;

        return 4;
    }

    public static void ShowRewardPrompt(string message)
    {
        ShowPrompt(message);
    }

    public static void ShowPrompt(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        FishingInteractionController activeFishing = FishingInteractionController.ActiveInstance;
        if (activeFishing != null)
        {
            activeFishing.ShowExternalPrompt(message);
            return;
        }

        Debug.Log(message);
    }
}
