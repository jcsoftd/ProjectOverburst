using UnityEngine;

public interface IMagicProjectile
{
    void Configure(MagicProjectileConfig config); // 설정 주입
    void Launch(Vector3 launchDirection); // 발사
}
