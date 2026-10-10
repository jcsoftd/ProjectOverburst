using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Caves
{
    // Authored source assets only. Layout, terrain heights, navigation and dressing are made per entry.
    public sealed class CaveGenerationAssets : ScriptableObject
    {
        [Serializable] public struct Model { public Mesh mesh; public string name; }
        public CavePlatformLibrary library;
        public CaveCatalog catalog;
        public TerrainData terrain;
        public Material terrainMaterial, mistMaterial;
        public float terrainY;
        public GameObject lighting, warmLight, blueLight, yellowLight, web;
        public GameObject[] rocks, eggs;
        public Model[] models;
        Dictionary<Mesh, string> modelNames;
        public string ModelName(MeshFilter filter)
        {
            if (!filter.sharedMesh) return "";
            if (modelNames == null)
            {
                modelNames = new Dictionary<Mesh, string>();
                foreach (var model in models) if (model.mesh) modelNames[model.mesh] = model.name;
            }
            return modelNames.TryGetValue(filter.sharedMesh, out var name) ? name : filter.sharedMesh.name;
        }
    }
}
