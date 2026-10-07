using UnityEngine;

public sealed class AppearanceStylistLabel:MonoBehaviour
{
    private void LateUpdate()
    {
        var camera=Camera.main;
        if(camera)transform.rotation=camera.transform.rotation;
    }
}
