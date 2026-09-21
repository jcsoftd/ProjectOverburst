using UnityEngine;

// Put this on a map root. Child zones inherit the default or explicitly override it.
[DisallowMultipleComponent]
public sealed class EnemyMapTheme : MonoBehaviour
{
    [SerializeField] private EnemyThemeTable defaultTable;
    public EnemyThemeTable DefaultTable => defaultTable;
    public void Configure(EnemyThemeTable table) { defaultTable = table; }
    public static EnemyThemeTable Resolve(Transform zone, EnemyThemeTable zoneOverride)
    {
        if (zoneOverride != null) return zoneOverride;
        var map = zone != null ? zone.GetComponentInParent<EnemyMapTheme>() : null;
        return map != null ? map.DefaultTable : null;
    }
}
