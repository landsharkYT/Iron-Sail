using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ShopDialogueLibrary", menuName = "The Iron Sail/Dialogue/Shop Dialogue Library")]
public class ShopDialogueLibrary : ScriptableObject
{
    public enum DialogueCategory
    {
        HelpfulIntermediate = 0,
        FinalReveal = 1,
        NonHelpful = 2,
        RepeatTalk = 3,
        PostReveal = 4
    }

    [Serializable]
    public struct DialoguePool
    {
        public DialogueCategory category;
        public ShopDialogue[] entries;

        public int Count => entries != null ? entries.Length : 0;

        public bool HasUsableEntries
        {
            get
            {
                if (entries == null || entries.Length == 0)
                    return false;

                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i] != null && entries[i].HasAnyContent)
                        return true;
                }

                return false;
            }
        }
    }

    [Header("Dialogue Pools")]
    [SerializeField] DialoguePool helpfulIntermediate = new DialoguePool
    {
        category = DialogueCategory.HelpfulIntermediate
    };
    [SerializeField] DialoguePool finalReveal = new DialoguePool
    {
        category = DialogueCategory.FinalReveal
    };
    [SerializeField] DialoguePool nonHelpful = new DialoguePool
    {
        category = DialogueCategory.NonHelpful
    };
    [SerializeField] DialoguePool repeatTalk = new DialoguePool
    {
        category = DialogueCategory.RepeatTalk
    };
    [SerializeField] DialoguePool postReveal = new DialoguePool
    {
        category = DialogueCategory.PostReveal
    };

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugHelpfulIntermediateCount;
    [SerializeField] int debugFinalRevealCount;
    [SerializeField] int debugNonHelpfulCount;
    [SerializeField] int debugRepeatTalkCount;
    [SerializeField] int debugPostRevealCount;

    public DialoguePool GetPool(DialogueCategory category)
    {
        return category switch
        {
            DialogueCategory.HelpfulIntermediate => helpfulIntermediate,
            DialogueCategory.FinalReveal => finalReveal,
            DialogueCategory.NonHelpful => nonHelpful,
            DialogueCategory.RepeatTalk => repeatTalk,
            DialogueCategory.PostReveal => postReveal,
            _ => helpfulIntermediate
        };
    }

    public bool TryGetDialogue(DialogueCategory category, int deterministicIndex, out ShopDialogue dialogue)
    {
        DialoguePool pool = GetPool(category);
        if (pool.entries == null || pool.entries.Length == 0)
        {
            dialogue = null;
            return false;
        }

        int usableCount = CountUsableEntries(pool.entries);
        if (usableCount == 0)
        {
            dialogue = null;
            return false;
        }

        int targetUsableIndex = Mathf.Abs(deterministicIndex) % usableCount;
        int currentUsableIndex = 0;
        for (int i = 0; i < pool.entries.Length; i++)
        {
            ShopDialogue candidate = pool.entries[i];
            if (candidate == null || !candidate.HasAnyContent)
                continue;

            if (currentUsableIndex == targetUsableIndex)
            {
                dialogue = candidate;
                return true;
            }

            currentUsableIndex++;
        }

        dialogue = null;
        return false;
    }

    void OnValidate()
    {
        debugHelpfulIntermediateCount = CountUsableEntries(helpfulIntermediate.entries);
        debugFinalRevealCount = CountUsableEntries(finalReveal.entries);
        debugNonHelpfulCount = CountUsableEntries(nonHelpful.entries);
        debugRepeatTalkCount = CountUsableEntries(repeatTalk.entries);
        debugPostRevealCount = CountUsableEntries(postReveal.entries);
    }

    static int CountUsableEntries(ShopDialogue[] entries)
    {
        if (entries == null)
            return 0;

        int count = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].HasAnyContent)
                count++;
        }

        return count;
    }
}
