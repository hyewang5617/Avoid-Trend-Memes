using UnityEngine;

namespace MemeDodge
{
    [RequireComponent(typeof(Camera))]
    public sealed class AspectCamera : MonoBehaviour
    {
        Camera view;
        void Awake() => view = GetComponent<Camera>();
        void LateUpdate()
        {
            float relativeAspect = (float)Screen.width / Mathf.Max(1, Screen.height) / (16f / 9f);
            view.rect = relativeAspect < 1
                ? new Rect(0, (1 - relativeAspect) / 2, 1, relativeAspect)
                : new Rect((1 - 1 / relativeAspect) / 2, 0, 1 / relativeAspect, 1);
        }
    }
}
