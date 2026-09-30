using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Local authoring aid. Green circles are world-space radii, never renderer bounds.
public static class HeavyRadiusPreview
{
    public static string Render(bool calibrated = false, bool quarter = false)
    {
        var h = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(SelectedElementVfxBuilder.HeavyAsset);
        var prefabs = new[] { h.elementVfx.fireImpact, h.elementVfx.electricImpact, h.elementVfx.FireChainExplosion };
        var preview = new PreviewRenderUtility();
        const int size = 400;
        var sheet = new Texture2D(size * 3, size * 3, TextureFormat.RGB24, false);
        float[] references = { h.elementVfx.fireImpactRadius, h.elementVfx.electricImpactRadius, h.elementVfx.FireChainReferenceRadius };
        try
        {
            var camera = preview.camera;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .035f, .035f, 1);
            camera.transform.position = quarter ? new Vector3(0, 12, -12) : new Vector3(0, 20, 0);
            camera.transform.LookAt(Vector3.zero, Vector3.forward);
            if (quarter) camera.transform.LookAt(Vector3.zero, Vector3.up);
            camera.nearClipPlane = .01f; camera.farClipPlane = 80;
            for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
            {
                float radius = row == 2 ? 1 + col * .4f : 1.5f + col * 1.25f;
                float time = calibrated ? (row == 0 ? .2f : row == 1 ? .5f : .2f) : new[] { .07f, .2f, .5f }[col];
                camera.orthographicSize = calibrated ? 6 : row == 2 ? 4 : 9;
                var go = UnityEngine.Object.Instantiate(prefabs[row]); preview.AddSingleGO(go);
                go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
                go.transform.localScale = prefabs[row].transform.localScale * (calibrated ? radius / references[row] : 1);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.useAutoRandomSeed = false; ps.randomSeed = 42;
                    if (ps.gameObject.activeInHierarchy) ps.Simulate(time, false, true, true);
                }
                preview.BeginStaticPreview(new Rect(0, 0, size, size)); preview.Render(true);
                var tex = preview.EndStaticPreview();
                for (int r = 1; r <= (calibrated ? 1 : row == 2 ? 3 : 8); r++)
                {
                    float rr = calibrated ? radius : r;
                    for (int i = 0; i < 1440; i++)
                    {
                        float a = i * Mathf.PI * 2 / 1440;
                        var p = camera.WorldToViewportPoint(new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr));
                        int x = Mathf.RoundToInt(p.x * (size - 1)), y = Mathf.RoundToInt(p.y * (size - 1));
                        if (x >= 0 && x < size && y >= 0 && y < size)
                            tex.SetPixel(x, y, calibrated ? Color.green : new Color(0, .4f, .16f));
                    }
                }
                sheet.SetPixels(col * size, (2 - row) * size, size, size, tex.GetPixels());
                UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(go);
            }
            sheet.Apply();
            string path = SelectedElementVfxBuilder.Output + (calibrated ? quarter ? "/RadiusQuarter.png" : "/RadiusTop.png" : "/RadiusSurvey.png");
            File.WriteAllBytes(path, sheet.EncodeToPNG()); return path;
        }
        finally { preview.Cleanup(); UnityEngine.Object.DestroyImmediate(sheet); }
    }
}
