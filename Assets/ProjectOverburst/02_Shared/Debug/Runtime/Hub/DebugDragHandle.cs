using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Overburst.DebugTools
{
    /// <summary>창 제목줄·크기 조절 손잡이·오버레이 머리글을 끌 때 캔버스 단위 이동량을 넘긴다.</summary>
    public sealed class DebugDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<Vector2> Dragged;
        public Action Ended;
        private Canvas canvas;

        public void OnBeginDrag(PointerEventData eventData)
        {
            canvas = GetComponentInParent<Canvas>();
        }

        public void OnDrag(PointerEventData eventData)
        {
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            Dragged?.Invoke(eventData.delta / scale);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Ended?.Invoke();
        }
    }
}
