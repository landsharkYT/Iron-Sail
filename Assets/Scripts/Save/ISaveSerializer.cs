// The single swap point for serialization. Today this is JsonUtility; if faction
// migrations later justify Newtonsoft, only the implementation behind this
// interface changes — no capture/restore code is touched. See ADR 0003.
public interface ISaveSerializer
{
    string Serialize(SaveFile saveFile);
    SaveFile Deserialize(string text);
}
