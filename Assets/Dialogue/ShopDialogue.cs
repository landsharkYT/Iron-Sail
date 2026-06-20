using System;
using UnityEngine;

[Serializable]
public class ShopDialogue
{
    [Tooltip("Optional label for authoring/debugging so variants are easier to spot in the inspector.")]
    public string debugName;

    [Tooltip("White dialogue lines shown one at a time in order. Leave blank entries out.")]
    [TextArea(2, 4)]
    public string[] whiteLines;

    [Tooltip("Optional yellow line shown after the white dialogue finishes, usually for map updates.")]
    [TextArea(2, 3)]
    public string yellowUpdateLine;

    public bool HasYellowUpdateLine => !string.IsNullOrWhiteSpace(yellowUpdateLine);

    public int WhiteLineCount
    {
        get
        {
            if (whiteLines == null)
                return 0;

            int count = 0;
            for (int i = 0; i < whiteLines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(whiteLines[i]))
                    count++;
            }

            return count;
        }
    }

    public bool HasAnyContent => WhiteLineCount > 0 || HasYellowUpdateLine;

    public string GetWhiteLine(int index)
    {
        if (whiteLines == null || index < 0 || index >= whiteLines.Length)
            return string.Empty;

        return whiteLines[index] ?? string.Empty;
    }
}
