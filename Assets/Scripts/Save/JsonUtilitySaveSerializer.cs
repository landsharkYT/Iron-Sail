using UnityEngine;

// JsonUtility-backed serializer. prettyPrint keeps exports human-readable and
// hand-editable, which is a stated goal of the Save File.
public sealed class JsonUtilitySaveSerializer : ISaveSerializer
{
    public string Serialize(SaveFile saveFile) => JsonUtility.ToJson(saveFile, true);

    public SaveFile Deserialize(string text) => JsonUtility.FromJson<SaveFile>(text);
}
