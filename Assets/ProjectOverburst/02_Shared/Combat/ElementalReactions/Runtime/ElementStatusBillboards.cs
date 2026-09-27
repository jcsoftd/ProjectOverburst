using UnityEngine;
using UnityEngine.Rendering;

// The visible overflow of the detailed aura pool is rendered in two instanced draws.
// No per-enemy ParticleSystem, material instance, coroutine or GameObject is needed.
public sealed class ElementStatusBillboards
{
    private readonly Matrix4x4[] fire = new Matrix4x4[1023], shock = new Matrix4x4[1023];
    private Mesh quad;
    private Material fireMaterial, shockMaterial;
    private int fireCount, shockCount;
    public int LastCount => fireCount + shockCount;
    public void Begin() { fireCount = shockCount = 0; }
    public void Add(Vector3 position, Quaternion rotation, bool burning, bool shocked)
    {
        Matrix4x4 pose = Matrix4x4.TRS(position + Vector3.up * .15f, rotation, new Vector3(.8f, 1.2f, 1f));
        if (burning && fireCount < fire.Length) fire[fireCount++] = pose;
        if (shocked && shockCount < shock.Length) shock[shockCount++] = pose;
    }
    public void Draw(Camera camera)
    {
        if (camera == null || fireCount + shockCount == 0 || !SystemInfo.supportsInstancing) return;
        if (quad == null)
        {
            Shader shader = Resources.Load<Shader>("Combat/VFX/ElementStatusBillboard");
            if (shader == null) return;
            quad = new Mesh { name = "ElementStatusBillboard", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new[] { new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(.5f,.5f),new Vector3(-.5f,.5f) };
            quad.uv = new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
            quad.triangles = new[] { 0,1,2,0,2,3 }; quad.RecalculateBounds();
            fireMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            fireMaterial.SetColor("_Tint", new Color(2.8f,.65f,.06f,1f));
            shockMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            shockMaterial.SetColor("_Tint", new Color(.35f,.9f,2.8f,1f));
        }
        if (fireCount > 0) Graphics.DrawMeshInstanced(quad,0,fireMaterial,fire,fireCount,null,ShadowCastingMode.Off,false,0,camera,LightProbeUsage.Off);
        if (shockCount > 0) Graphics.DrawMeshInstanced(quad,0,shockMaterial,shock,shockCount,null,ShadowCastingMode.Off,false,0,camera,LightProbeUsage.Off);
    }
    public void Dispose()
    {
        if (Application.isPlaying) { Object.Destroy(quad); Object.Destroy(fireMaterial); Object.Destroy(shockMaterial); }
        else { Object.DestroyImmediate(quad); Object.DestroyImmediate(fireMaterial); Object.DestroyImmediate(shockMaterial); }
        quad = null; fireMaterial = shockMaterial = null;
    }
}
