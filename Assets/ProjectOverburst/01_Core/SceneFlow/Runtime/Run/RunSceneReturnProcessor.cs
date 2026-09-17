using System.Collections;
using System;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class RunSceneReturnProcessor : MonoBehaviour // 런 복귀
{
    private const float GroundSnapRayHeight = 30f; // 바닥 탐색 높이
    private const float GroundSnapRayDistance = 100f; // 바닥 탐색 거리
    private const float PlayerGroundOffset = 0.12f; // 지면 여유

    public static void Begin(RunSceneReturnContext context)
    {
        Begin(context, null); // 콜백 없음
    }

    public static void Begin(
        RunSceneReturnContext context,
        Action onComplete)
    {
        if (context == null)
            return;

        GameObject processorObject = new GameObject(
            "RunSceneReturnProcessor"); // 임시 처리기
        RunSceneReturnProcessor processor =
            processorObject.AddComponent<RunSceneReturnProcessor>();
        processor.StartCoroutine(processor.ProcessReturn(context, onComplete));
    }

    private IEnumerator ProcessReturn(
        RunSceneReturnContext context,
        Action onComplete)
    {
        RunWalkableContext.Clear(); // 런 grid 해제
        yield return null;

        Transform player = EnsureSinglePlayer("Player"); // Player 정리
        Camera keepCamera = EnsureSingleMainCameraWithoutCreating(); // Camera 정리
        EnsureSingleEventSystemWithoutCreating(); // EventSystem 정리
        RuntimeUIDiagnostics.DestroyRunTransientUiRoots(); // 런 UI 정리
        EnsureSingleAudioListener(keepCamera); // AudioListener 정리

        if (player == null)
        {
            Debug.LogWarning("[RunReturn] Preserved Player not found. Return teleport skipped.");
            onComplete?.Invoke();
            Destroy(gameObject);
            yield break;
        }

        ClearRunFallGuard(player); // 런 방어선 해제

        bool foundReturnPoint;
        Vector3 desiredPosition = ResolveDesiredPosition(
            context.ReturnPointId,
            player.position,
            out foundReturnPoint);

        bool groundHit;
        Vector3 groundHitPoint;
        int groundLayerMask = ResolveGroundLayerMask(player); // 지면 마스크

        if (!TrySnapPositionToGround(desiredPosition, groundLayerMask, out Vector3 spawnPosition, out groundHit, out groundHitPoint))
        {
            Debug.LogWarning("[RunReturn] Ground raycast failed under desiredPosition=" + desiredPosition + ". Player move aborted.");
            onComplete?.Invoke();
            Destroy(gameObject);
            yield break;
        }

        yield return MovePlayerSafely(player, spawnPosition);

        onComplete?.Invoke();
        Destroy(gameObject);
    }

    private static Transform EnsureSinglePlayer(string playerTag)
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);
        if (players == null || players.Length == 0)
            return null;

        if (players.Length > 1)
            Debug.LogWarning("[RunReturn] Multiple Player-tagged objects found: " + players.Length);

        GameObject keep = FindPreferredSceneObject(players); // 보존 Player
        for (int i = 0; i < players.Length; i++)
        {
            GameObject player = players[i];
            if (player == null || player == keep)
                continue;

            if (player.scene != keep.scene)
            {
                player.SetActive(false); // 중복 비활성
                Destroy(player); // 중복 제거
            }
        }

        return keep != null ? keep.transform : null;
    }

    private static GameObject FindPreferredSceneObject(GameObject[] objects)
    {
        GameObject fallback = null;
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject target = objects[i];
            if (target == null)
                continue;

            if (fallback == null)
                fallback = target;

            if (IsPreferredRuntimeObject(target, fallback))
                fallback = target;
        }

        return fallback;
    }

    private static bool IsPreferredRuntimeObject(GameObject candidate, GameObject current)
    {
        if (candidate == null)
            return false;

        if (current == null)
            return true;

        return GetRuntimeObjectScore(candidate) > GetRuntimeObjectScore(current);
    }

    private static int GetRuntimeObjectScore(GameObject target)
    {
        int score = 0;
            if (target.scene.name == PersistentSceneFlow.PersistentSceneName) // Persistent 우선
                score += 100;
        if (target.GetComponent<PlayerInventory>() != null) // 인벤토리 보유
            score += 50;
        if (target.GetComponent<PlayerMovement>() != null) // 이동 컴포넌트
            score += 40;
        if (target.activeInHierarchy) // 활성 상태
            score += 10;

        return score;
    }

    private static Camera EnsureSingleMainCameraWithoutCreating()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        Camera keepCamera = null;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || !camera.CompareTag("MainCamera"))
                continue;

            if (keepCamera == null || IsPreferredCamera(camera, keepCamera))
                keepCamera = camera; // 보존 Camera
        }

        if (keepCamera == null)
            return null;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera == keepCamera || !camera.CompareTag("MainCamera"))
                continue;

            camera.enabled = false; // 중복 비활성
            camera.tag = "Untagged"; // MainCamera 중복 방지
        }

        return keepCamera;
    }

    private static bool IsPreferredCamera(Camera candidate, Camera current)
    {
        return GetScenePreferenceScore(candidate != null ? candidate.gameObject.scene.name : string.Empty)
            > GetScenePreferenceScore(current != null ? current.gameObject.scene.name : string.Empty);
    }

    private static void EnsureSingleAudioListener(Camera keepCamera)
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        AudioListener keepListener = keepCamera != null ? keepCamera.GetComponent<AudioListener>() : null;

        if (keepListener == null)
        {
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null)
                    continue;

                if (keepListener == null
                    || GetScenePreferenceScore(listener.gameObject.scene.name) > GetScenePreferenceScore(keepListener.gameObject.scene.name))
                    keepListener = listener; // 보존 Listener
            }
        }

        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null)
                continue;

            listener.enabled = listener == keepListener; // 단일 Listener
        }
    }

    private static void EnsureSingleEventSystemWithoutCreating()
    {
        EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        EventSystem keepEventSystem = null;

        for (int i = 0; i < eventSystems.Length; i++)
        {
            EventSystem eventSystem = eventSystems[i];
            if (eventSystem == null)
                continue;

            if (keepEventSystem == null
                || GetScenePreferenceScore(eventSystem.gameObject.scene.name) > GetScenePreferenceScore(keepEventSystem.gameObject.scene.name))
                keepEventSystem = eventSystem; // 보존 EventSystem
        }

        if (keepEventSystem == null)
            return;

        keepEventSystem.gameObject.SetActive(true);
        EventSystem.current = keepEventSystem; // 현재 EventSystem

        for (int i = 0; i < eventSystems.Length; i++)
        {
            EventSystem eventSystem = eventSystems[i];
            if (eventSystem == null || eventSystem == keepEventSystem)
                continue;

            eventSystem.gameObject.SetActive(false); // 중복 비활성
        }
    }

    private static int GetScenePreferenceScore(string sceneName)
    {
        if (sceneName == PersistentSceneFlow.PersistentSceneName)
            return 100;
        return 0;
    }

    private static Vector3 ResolveDesiredPosition(
        string returnPointId,
        Vector3 playerFallbackPosition,
        out bool foundReturnPoint)
    {
        foundReturnPoint = false; // 반환점 결과

        if (TryGetReturnPointPosition(returnPointId, out Vector3 returnPointPosition, out foundReturnPoint))
            return returnPointPosition;

        Debug.LogWarning(
            "[RunReturn] HubReturnPoint was not found. "
            + "The current Player position is used.");
        return playerFallbackPosition;
    }

    private static bool TryGetReturnPointPosition(string returnPointId, out Vector3 position, out bool foundMatchingPoint)
    {
        HubReturnPoint[] points = FindObjectsByType<HubReturnPoint>(FindObjectsSortMode.None);
        HubReturnPoint fallback = null; // 첫 반환점
        foundMatchingPoint = false; // id 매칭

        for (int i = 0; i < points.Length; i++)
        {
            HubReturnPoint point = points[i];
            if (point == null)
                continue;

            if (fallback == null)
                fallback = point; // 기본 반환점

            if (point.ReturnPointId == returnPointId)
            {
                position = point.transform.position;
                foundMatchingPoint = true; // id 매칭
                return true;
            }
        }

        if (fallback != null)
        {
            position = fallback.transform.position;
            foundMatchingPoint = true; // fallback 매칭
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    private static int ResolveGroundLayerMask(Transform player)
    {
        PlayerMovement movementController = player != null ? player.GetComponent<PlayerMovement>() : null;
        if (movementController != null && movementController.GroundLayerMask.value != 0)
            return movementController.GroundLayerMask.value; // 플레이어 기준

        int groundLayer = LayerMask.NameToLayer("Ground");
        int mask = groundLayer >= 0 ? 1 << groundLayer : 0;
        mask |= 1 << 0; // Default 포함
        return mask;
    }

    private static bool TrySnapPositionToGround(Vector3 position, int groundLayerMask, out Vector3 spawnPosition, out bool hitGround, out Vector3 hitPoint)
    {
        Vector3 rayOrigin = position + Vector3.up * GroundSnapRayHeight; // 위쪽 시작

        if (TryRaycastGround(rayOrigin, groundLayerMask, out RaycastHit hit))
        {
            hitGround = true;
            hitPoint = hit.point;
            position.y = hit.point.y + PlayerGroundOffset; // 지면 보정
            spawnPosition = position;
            return true;
        }

        hitGround = false;
        hitPoint = Vector3.zero;
        spawnPosition = position;
        return false;
    }

    private static bool TryRaycastGround(Vector3 rayOrigin, int layerMask, out RaycastHit bestHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            GroundSnapRayDistance,
            layerMask,
            QueryTriggerInteraction.Ignore);
        bestHit = default;
        bool hasHit = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            Transform root = hit.collider.transform.root;
            if (root != null && root.CompareTag("Player")) // 자기 자신 제외
                continue;

            if (!hasHit || hit.distance < bestHit.distance)
            {
                bestHit = hit; // 최단 hit
                hasHit = true; // hit 있음
            }
        }

        return hasHit;
    }

    private static int ClearRunFallGuard(Transform player)
    {
        if (player == null)
            return 0;

        int removedCount = 0; // 제거 수
        RunFallGuard[] fallGuards =
            player.GetComponentsInChildren<RunFallGuard>(true);
        for (int i = 0; i < fallGuards.Length; i++)
        {
            RunFallGuard fallGuard = fallGuards[i];
            if (fallGuard == null)
                continue;

            fallGuard.enabled = false; // 복귀 중지
            Destroy(fallGuard); // 런 guard 제거
            removedCount++;
        }

        return removedCount;
    }

    private IEnumerator MovePlayerSafely(Transform player, Vector3 position)
    {
        PlayerMovement movementController = player.GetComponent<PlayerMovement>();
        CharacterController characterController = player.GetComponent<CharacterController>();
        Rigidbody rigidbody = player.GetComponent<Rigidbody>();

        bool movementWasEnabled = movementController != null && movementController.enabled;
        bool characterControllerWasEnabled = characterController != null && characterController.enabled;
        bool rigidbodyWasKinematic = rigidbody != null && rigidbody.isKinematic;

        GameplayInputBlocker.Block(this); // 입력 차단

        if (movementWasEnabled)
            movementController.enabled = false; // 이동 중지

        if (characterControllerWasEnabled)
            characterController.enabled = false; // 컨트롤러 중지

        if (rigidbody != null)
        {
            if (!rigidbody.isKinematic)
            {
                rigidbody.linearVelocity = Vector3.zero; // 이동 속도 정리
                rigidbody.angularVelocity = Vector3.zero; // 회전 속도 정리
            }

            rigidbody.isKinematic = true; // 물리 중지
            rigidbody.position = position;
            player.position = position;
            rigidbody.Sleep();
        }
        else
        {
            player.position = position;
        }

        Physics.SyncTransforms(); // 위치 동기화
        yield return null;

        if (rigidbody != null)
        {
            rigidbody.position = position;
            player.position = position;
            rigidbody.Sleep();
        }
        else
        {
            player.position = position;
        }

        Physics.SyncTransforms(); // 최종 동기화
        Vector3 spawnDelta = player.position - position;

        if (rigidbody != null)
        {
            rigidbody.isKinematic = rigidbodyWasKinematic; // 물리 복구
            if (!rigidbodyWasKinematic)
            {
                rigidbody.linearVelocity = Vector3.zero; // 복구 후 속도 정리
                rigidbody.angularVelocity = Vector3.zero; // 복구 후 회전 정리
                rigidbody.Sleep();
            }
        }

        if (spawnDelta.magnitude > 0.2f)
            Debug.LogWarning("[RunReturn] Player position differs from FinalSpawn by " + spawnDelta.magnitude);

        if (characterControllerWasEnabled)
            characterController.enabled = true; // 컨트롤러 복구

        if (movementWasEnabled)
            movementController.enabled = true; // 이동 복구

        GameplayInputBlocker.Unblock(this); // 입력 복구
        yield return null;
    }
}
