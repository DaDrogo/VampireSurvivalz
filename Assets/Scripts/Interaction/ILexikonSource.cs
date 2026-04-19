using System.Collections.Generic;

[System.Serializable]
public struct StatLine
{
    public string label;
    public string value;
    public StatLine(string label, string value) { this.label = label; this.value = value; }
}

public interface ILexikonSource
{
    List<StatLine> GetLexikonStats();
}
