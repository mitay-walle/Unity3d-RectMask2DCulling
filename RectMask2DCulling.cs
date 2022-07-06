using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Plugins.UI
{
#if UNITY_EDITOR
    [CustomEditor(typeof(RectMask2DCulling))]
    public class RectMask2DCullingEditor : Editor
    {
    }
#endif
    public class RectMask2DCulling : RectMask2D
    {
        private static FieldInfo maskablesField = typeof(RectMask2D).GetField("m_MaskableTargets", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo ClipTargetsField = typeof(RectMask2D).GetField("m_ClipTargets", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo ClippersField = typeof(RectMask2D).GetField("m_Clippers", BindingFlags.Instance | BindingFlags.NonPublic);

        [SerializeField] private bool m_useCulling;
        [SerializeField] private bool m_ForceClip = true;
        [SerializeField] private bool m_UseSoftness;

        [NonSerialized] private bool m_ShouldRecalculateClipRects;
        [NonSerialized] private Rect m_LastClipRectCanvasSpace;
        [NonSerialized] private Canvas m_Canvas;

        private HashSet<MaskableGraphic> maskableTargets = new HashSet<MaskableGraphic>();
        private HashSet<IClippable> clipTargets = new HashSet<IClippable>();
        private List<RectMask2D> clippers = new List<RectMask2D>();

        private Canvas Canvas
        {
            get
            {
                if (m_Canvas == null)
                {
                    var list = ListPool<Canvas>.Get();
                    gameObject.GetComponentsInParent(false, list);
                    if (list.Count > 0)
                        m_Canvas = list[list.Count - 1];
                    else
                        m_Canvas = null;
                    ListPool<Canvas>.Release(list);
                }

                return m_Canvas;
            }
        }

        private Vector3[] _corners = new Vector3[4];

        private Rect rootCanvasRect
        {
            get
            {
                rectTransform.GetWorldCorners(_corners);

                if (!ReferenceEquals(Canvas, null))
                {
                    Canvas rootCanvas = Canvas.rootCanvas;
                    for (int i = 0; i < 4; ++i)
                        _corners[i] = rootCanvas.transform.InverseTransformPoint(_corners[i]);
                }

                return new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y);
            }
        }

        protected RectMask2DCulling()
        {
        }

        private void ActualizeFields()
        {
            maskableTargets = maskablesField.GetValue(this) as HashSet<MaskableGraphic>;
            clipTargets = ClipTargetsField.GetValue(this) as HashSet<IClippable>;
            clippers = ClippersField.GetValue(this) as List<RectMask2D>;
            if (!clippers.Contains(this)) clippers.Add(this);
        }

        public override void UpdateClipSoftness()
        {
            if (!m_UseSoftness) return;

            if (ReferenceEquals(Canvas, null))
            {
                return;
            }

            foreach (IClippable clipTarget in clipTargets)
            {
                clipTarget.SetClipSoftness(softness);
            }

            foreach (MaskableGraphic maskableTarget in maskableTargets)
            {
                maskableTarget.SetClipSoftness(softness);
            }
        }

        public override void PerformClipping()
        {
            if (ReferenceEquals(Canvas, null))
            {
                return;
            }

            ActualizeFields();

            // if the parents are changed
            // or something similar we
            // do a recalculate here
            if (m_ShouldRecalculateClipRects)
            {
                GetRectMasksForClip(clippers);
                m_ShouldRecalculateClipRects = false;
            }

            // get the compound rects from
            // the clippers that are valid
            bool validRect = true;
            Rect clipRect = Clipping.FindCullAndClipWorldRect(clippers, out validRect);

            // If the mask is in ScreenSpaceOverlay/Camera render mode, its content is only rendered when its rect
            // overlaps that of the root canvas.
            RenderMode renderMode = Canvas.rootCanvas.renderMode;
            bool maskIsCulled =
                (renderMode == RenderMode.ScreenSpaceCamera || renderMode == RenderMode.ScreenSpaceOverlay) &&
                !clipRect.Overlaps(rootCanvasRect, true);

            if (maskIsCulled)
            {
                // Children are only displayed when inside the mask. If the mask is culled, then the children
                // inside the mask are also culled. In that situation, we pass an invalid rect to allow callees
                // to avoid some processing.
                clipRect = Rect.zero;
                validRect = false;
            }

            if (clipRect != m_LastClipRectCanvasSpace)
            {
                foreach (IClippable clipTarget in clipTargets)
                {
                    clipTarget.SetClipRect(clipRect, validRect);
                }

                foreach (MaskableGraphic maskableTarget in maskableTargets)
                {
                    maskableTarget.SetClipRect(clipRect, validRect);
                    if (m_useCulling) maskableTarget.Cull(clipRect, validRect);
                }
            }
            else if (m_ForceClip)
            {
                foreach (IClippable clipTarget in clipTargets)
                {
                    clipTarget.SetClipRect(clipRect, validRect);
                }

                foreach (MaskableGraphic maskableTarget in maskableTargets)
                {
                    maskableTarget.SetClipRect(clipRect, validRect);

                    if (m_useCulling && maskableTarget.canvasRenderer.hasMoved)
                        maskableTarget.Cull(clipRect, validRect);
                }
            }
            else
            {
                foreach (MaskableGraphic maskableTarget in maskableTargets)
                {
                    //Case 1170399 - hasMoved is not a valid check when animating on pivot of the object
                    if (m_useCulling) maskableTarget.Cull(clipRect, validRect);
                }
            }

            m_LastClipRectCanvasSpace = clipRect;
            //m_ForceClip = false;

            UpdateClipSoftness();
        }

        /// <summary>
        /// Search for all RectMask2D that apply to the given RectMask2D (includes self).
        /// </summary>
        /// <param name="clipper">Starting clipping object.</param>
        /// <param name="masks">The list of Rect masks</param>
        private void GetRectMasksForClip(List<RectMask2D> masks)
        {
            masks.Clear();

            List<Canvas> canvasComponents = ListPool<Canvas>.Get();
            List<RectMask2D> rectMaskComponents = ListPool<RectMask2D>.Get();
            transform.GetComponentsInParent(false, rectMaskComponents);

            if (rectMaskComponents.Count > 0)
            {
                transform.GetComponentsInParent(false, canvasComponents);
                for (int i = rectMaskComponents.Count - 1; i >= 0; i--)
                {
                    if (!rectMaskComponents[i].IsActive())
                        continue;
                    bool shouldAdd = true;
                    for (int j = canvasComponents.Count - 1; j >= 0; j--)
                    {
                        if (!IsDescendantOrSelf(canvasComponents[j].transform, rectMaskComponents[i].transform) && canvasComponents[j].overrideSorting)
                        {
                            shouldAdd = false;
                            break;
                        }
                    }

                    if (shouldAdd)
                        masks.Add(rectMaskComponents[i]);
                }
            }

            ListPool<RectMask2D>.Release(rectMaskComponents);
            ListPool<Canvas>.Release(canvasComponents);
        }

        /// <summary>
        /// Helper function to determine if the child is a descendant of father or is father.
        /// </summary>
        /// <param name="father">The transform to compare against.</param>
        /// <param name="child">The starting transform to search up the hierarchy.</param>
        /// <returns>Is child equal to father or is a descendant.</returns>
        public static bool IsDescendantOrSelf(Transform father, Transform child)
        {
            if (father == null || child == null)
                return false;

            if (father == child)
                return true;

            while (child.parent != null)
            {
                if (child.parent == father)
                    return true;

                child = child.parent;
            }

            return false;
        }
    }

    #region Helper Classes

    internal static class ListPool<T>
    {
        // Object pool to avoid allocations.
        private static readonly ObjectPool<List<T>> s_ListPool = new ObjectPool<List<T>>(null, Clear);
        static void Clear(List<T> l)
        {
            l.Clear();
        }

        public static List<T> Get()
        {
            return s_ListPool.Get();
        }

        public static void Release(List<T> toRelease)
        {
            s_ListPool.Release(toRelease);
        }
    }

    internal class ObjectPool<T> where T : new()
    {
        private readonly Stack<T> m_Stack = new Stack<T>();
        private readonly UnityAction<T> m_ActionOnGet;
        private readonly UnityAction<T> m_ActionOnRelease;

        public int countAll { get; private set; }

        public int countActive
        {
            get { return countAll - countInactive; }
        }

        public int countInactive
        {
            get { return m_Stack.Count; }
        }

        public ObjectPool(UnityAction<T> actionOnGet, UnityAction<T> actionOnRelease)
        {
            m_ActionOnGet = actionOnGet;
            m_ActionOnRelease = actionOnRelease;
        }

        public T Get()
        {
            T element;
            if (m_Stack.Count == 0)
            {
                element = new T();
                countAll++;
            }
            else
            {
                element = m_Stack.Pop();
            }

            if (m_ActionOnGet != null)
                m_ActionOnGet(element);
            return element;
        }

        public void Release(T element)
        {
            if (m_Stack.Count > 0 && ReferenceEquals(m_Stack.Peek(), element))
                Debug.LogError("Internal error. Trying to destroy object that is already released to pool.");
            if (m_ActionOnRelease != null)
                m_ActionOnRelease(element);
            m_Stack.Push(element);
        }
    }

    #endregion
}
