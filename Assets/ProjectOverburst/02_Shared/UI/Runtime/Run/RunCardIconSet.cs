using System;
using UnityEngine;

/// <summary>Presentation-only lookup. Explicit Icon overrides the catalog.</summary>
public sealed class RunCardIconSet : ScriptableObject
{
    [Serializable] public sealed class Entry { public string Key; public string Title; public string Description; public Sprite Icon; }
    public Entry[] Entries;
    public static string Describe(string keyOrTitle,string fallback)
    {
        var set=Resources.Load<RunCardIconSet>("OverburstUI/Run/CardIcons");
        if(set!=null && set.Entries!=null)foreach(var entry in set.Entries)
            if(entry.Key==keyOrTitle || entry.Title==keyOrTitle)return entry.Description;
        return fallback;
    }
    public static Sprite Resolve(string keyOrTitle)
    {
        var set=Resources.Load<RunCardIconSet>("OverburstUI/Run/CardIcons");
        if(set==null || set.Entries==null)return null;
        foreach(var entry in set.Entries)
            if(entry.Key==keyOrTitle || entry.Title==keyOrTitle)return entry.Icon;
        return set.Entries.Length>0?set.Entries[0].Icon:null;
    }
}

