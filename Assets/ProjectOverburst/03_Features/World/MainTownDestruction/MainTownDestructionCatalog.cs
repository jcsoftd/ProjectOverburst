using System;
using UnityEngine;

public enum EnvironmentBreakMaterial { Wood, Ceramic, Stone, Metal, Glass }

public sealed class MainTownDestructionCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Definition
    {
        public GameObject source;
        public GameObject debris;
        public Bounds bounds;
        public bool tree;
        public EnvironmentBreakMaterial material;
    }
    public AudioClip[] woodSounds, ceramicSounds, stoneSounds, metalSounds, glassSounds;
    public AudioClip PickBreakSound(EnvironmentBreakMaterial material)
    {
        AudioClip[] clips = material switch
        {
            EnvironmentBreakMaterial.Ceramic => ceramicSounds,
            EnvironmentBreakMaterial.Stone => stoneSounds,
            EnvironmentBreakMaterial.Metal => metalSounds,
            EnvironmentBreakMaterial.Glass => glassSounds,
            _ => woodSounds
        };
        return clips != null && clips.Length > 0 ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;
    }
    public Definition[] definitions = Array.Empty<Definition>();
}
