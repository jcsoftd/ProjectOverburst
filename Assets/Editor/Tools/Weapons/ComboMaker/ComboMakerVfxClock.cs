using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Overburst.EditorTools.ComboMaker
{
    // Unity 6000.3 queues VFX.Simulate work until a native Editor scene update.
    // QueuePlayerLoopUpdate alone can leave it pending in a background Editor.
    internal static class ComboMakerVfxClock
    {
        private static readonly Action UpdateScene=BindUpdate();
        private static Action BindUpdate()
        {
            var method=typeof(EditorApplication).GetMethod("UpdateSceneIfNeeded",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
            return method==null?null:(Action)Delegate.CreateDelegate(typeof(Action),method);
        }
        public static void Flush()
        {
            if(Application.isPlaying)return;
            if(UpdateScene==null)throw new NotSupportedException("이 Unity 버전에서는 혈흔 프리뷰 실행 경로를 사용할 수 없습니다.");
            EditorApplication.QueuePlayerLoopUpdate();
            UpdateScene();
            GL.Flush();
        }
    }
}
