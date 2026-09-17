using UnityEngine;

public sealed class MultiplierAttribute : PropertyAttribute
{
    public readonly float Minimum;

    public MultiplierAttribute(float minimum = 0f)
    {
        Minimum = minimum;
    }
}
