using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Overburst.Persistence
{
    [Serializable] public sealed class AccountContentEntry
    {
        public string id;
        public UnityEngine.Object asset;
    }

    public sealed class AccountContentRegistry : ScriptableObject
    {
        public const string ResourcePath = "Persistence/AccountContentRegistry";
        [SerializeField] private List<AccountContentEntry> entries = new List<AccountContentEntry>();
        private Dictionary<string, UnityEngine.Object> byId;
        private Dictionary<UnityEngine.Object, string> byAsset;
        public IReadOnlyList<AccountContentEntry> Entries => entries;

        public string IdFor(UnityEngine.Object asset)
        {
            if (asset == null) throw new InvalidDataException("Missing content asset.");
            EnsureIndex();
            if (!byAsset.TryGetValue(asset, out string id))
                throw new InvalidDataException("Unregistered account content: " + asset.name);
            return id;
        }

        public T Resolve<T>(string id) where T : UnityEngine.Object
        {
            EnsureIndex();
            if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var asset) || !(asset is T result))
                throw new InvalidDataException("Missing account content ID: " + id + " (" + typeof(T).Name + ")");
            return result;
        }

        private void EnsureIndex()
        {
            if (byId != null) return;
            var ids = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
            var assets = new Dictionary<UnityEngine.Object, string>();
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id) || entry.asset == null || ids.ContainsKey(entry.id) || assets.ContainsKey(entry.asset))
                    throw new InvalidDataException("Invalid or duplicate account content registry entry.");
                ids.Add(entry.id, entry.asset);
                assets.Add(entry.asset, entry.id);
            }
            byId = ids;
            byAsset = assets;
        }

#if UNITY_EDITOR
        public void SetAuthoringEntries(List<AccountContentEntry> value)
        {
            entries = value ?? throw new ArgumentNullException(nameof(value));
            byId = null;
            byAsset = null;
            EnsureIndex();
        }
#endif
    }
}
