using System;
using UnityEngine;

public sealed class MainTownDestructionCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Definition
    {
        public GameObject source;
        public GameObject debris;
        public Bounds bounds;
        public bool tree;
    }
    public Definition[] definitions = Array.Empty<Definition>();
}
