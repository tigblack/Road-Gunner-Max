using UnityEngine;
namespace RoadGunner
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        Rect last;int width,height;
        void Update(){Rect r=Screen.safeArea;if(r==last&&width==Screen.width&&height==Screen.height)return;last=r;width=Screen.width;height=Screen.height;if(width==0||height==0)return;var t=(RectTransform)transform;t.anchorMin=new Vector2(r.xMin/width,r.yMin/height);t.anchorMax=new Vector2(r.xMax/width,r.yMax/height);t.offsetMin=t.offsetMax=Vector2.zero;}
    }
}
