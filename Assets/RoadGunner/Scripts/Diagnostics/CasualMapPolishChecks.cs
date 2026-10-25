#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    public sealed partial class Arena1PlayChecks
    {
        IEnumerator CircularMapAndWhiteRange()
        {
            Isolate();
            var map = s.minimap;
            var disc = map.mapRect.GetComponent<ArenaMinimapCircle>();
            Check("round map uses real stencil clipping", map.circularMask && map.circularMask.enabled &&
                !map.circularMask.showMaskGraphic && disc && disc.innerRadius == 0 && disc.softness == 0 &&
                Mathf.Abs(map.mapRect.rect.width - map.mapRect.rect.height) < .01f &&
                map.backgroundImage.maskable && map.backgroundImage.transform.IsChildOf(map.mapRect),
                "Solid circular Graphic + UGUI Mask, terrain RawImage and marker hierarchy inside; viewport=" + map.mapRect.rect.size);
            float farthest = 0;
            var corridor = layout.GetComponent<ArenaCorridorLayout>();
            var footprint = corridor ? corridor.mapFootprint : layout.ground.sharedMesh.vertices.Select(layout.ground.transform.TransformPoint).ToArray();
            foreach (var point in footprint)
                farthest = Mathf.Max(farthest, (map.Project(point) - map.mapRect.rect.center).magnitude);
            bool repairsInside = map.workshops.All(w => map.IsInsideCircle(map.Project(w.transform.position), 18));
            Check("whole arena and distant repair fit round map", farthest <= map.CircleRadius - 15 && repairsInside &&
                map.contentScale > .5f && map.contentScale <= 1,
                "Projected authored route/pocket footprint radius=" + farthest + "; viewport radius=" + map.CircleRadius +
                "; content scale=" + map.contentScale + "; all workshops inside with marker margin=" + repairsInside);
            // Verify actual GPU stencil output, including both terrain and custom marker geometry.
            // Magenta is an isolated diagnostic color absent from the arena HUD/terrain.
            var probe = new GameObject("Temporary stencil acceptance probe", typeof(RectTransform), typeof(CanvasRenderer));
            var rect = probe.GetComponent<RectTransform>(); rect.SetParent(map.mapRect, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = Vector2.one * 24;
            var raw = probe.AddComponent<RawImage>(); raw.texture = Texture2D.whiteTexture; raw.color = Color.magenta;
            raw.raycastTarget = false; raw.maskable = true;
            rect.anchoredPosition = Vector2.zero; yield return null;
            int rawInside = MagentaPixelCount();
            rect.anchoredPosition = Vector2.one * (map.CircleRadius * .80f); yield return null;
            int rawOutside = MagentaPixelCount();
            DestroyImmediate(raw);
            var icon = probe.AddComponent<ArenaMinimapIcon>(); icon.kind = MinimapMarkerKind.Buggy;
            icon.color = Color.magenta; icon.maskable = true; icon.raycastTarget = false;
            rect.anchoredPosition = Vector2.zero; icon.SetVerticesDirty(); yield return null;
            int iconInside = MagentaPixelCount();
            rect.anchoredPosition = Vector2.one * (map.CircleRadius * .80f); yield return null;
            int iconOutside = MagentaPixelCount();
            DestroyImmediate(probe);
            Check("terrain pixels are clipped outside circular mask", rawInside >= 4 && rawOutside == 0,
                "Actual portrait GPU readback magenta RawImage pixels: centre=" + rawInside + "; square corner outside circle=" + rawOutside);
            Check("custom icon pixels are clipped outside circular mask", iconInside >= 3 && iconOutside == 0,
                "Actual portrait GPU readback magenta Buggy marker pixels: centre=" + iconInside + "; square corner outside circle=" + iconOutside);
            var range = s.vehicle.GetComponent<AttackRangeIndicator>();
            Check("white boundary has no blue tint and stays subtle", range.outlineColor.r >= .95f &&
                Mathf.Abs(range.outlineColor.r - range.outlineColor.g) < .01f && Mathf.Abs(range.outlineColor.r - range.outlineColor.b) < .01f &&
                range.fillColor.a < .04f && range.contrastColor.r < .15f && range.contrastColor.b < .15f &&
                range.outlineWidth > range.contrastWidth && Mathf.Abs(range.Radius - s.turret.Range) < .001f,
                "White edge=" + range.outlineColor + "; neutral dark edge=" + range.contrastColor + "; fill=" + range.fillColor + "; combat radius=" + range.Radius);
            yield return null;
            Capture("round-map-white-range");
            yield return RangeTerrainCaptures();
        }

        IEnumerator RangeTerrainCaptures()
        {
            // Pick actual navigable bright/dark terrain from the baked scene, not synthetic swatches.
            var sample = new Texture2D(128, 128, TextureFormat.RGB24, false);
            var rt = RenderTexture.GetTemporary(128, 128, 0); var old = RenderTexture.active;
            try
            {
                Graphics.Blit(s.minimap.BackgroundTexture, rt); RenderTexture.active = rt;
                sample.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); sample.Apply();
            }
            finally { RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
            Bounds bounds = s.minimap.WorldBounds;
            float darkest = 2, brightest = -1; Vector3 darkPoint = testOrigin, brightPoint = testOrigin;
            for (int y = 12; y < 116; y += 4) for (int x = 12; x < 116; x += 4)
            {
                Vector3 point = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x / 127f), testOrigin.y,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, y / 127f));
                if (!NavMesh.SamplePosition(point, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                float luminance = sample.GetPixel(x, y).grayscale;
                if (luminance < darkest) { darkest = luminance; darkPoint = hit.position; }
                if (luminance > brightest) { brightest = luminance; brightPoint = hit.position; }
            }
            DestroyImmediate(sample);
            PlaceCar(Ground(testOrigin) + Vector3.up * .7f, testRotation); yield return null; Capture("white-range-sand");
            var corridor = layout.GetComponent<ArenaCorridorLayout>();
            bool roadFound = false;
            if (corridor && corridor.roadsRoot && corridor.mainRoad.Length > 2)
            {
                // Static batching may replace MeshFilter.sharedMesh with a combined mesh in Play.
                // Retain the authored road object and its actual sampled centre line as provenance.
                var road = corridor.roadsRoot.Find("Corridor road · Main north road");
                var renderer = road ? road.GetComponent<MeshRenderer>() : null;
                var filter = road ? road.GetComponent<MeshFilter>() : null;
                Vector3 point = corridor.mainRoad[corridor.mainRoad.Length / 2];
                if (road && filter && filter.sharedMesh && renderer && renderer.enabled && road.gameObject.activeInHierarchy &&
                    NavMesh.SamplePosition(point, out var hit, 2, NavMesh.AllAreas))
                { PlaceCar(Ground(hit.position) + Vector3.up * .7f, testRotation); roadFound = true; }
            }
            else
            {
                var road = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .FirstOrDefault(f => f.sharedMesh && (f.sharedMesh.name.StartsWith("Road ") || f.sharedMesh.name.Contains("worn-track")));
                if (road)
                {
                    var vertices = road.sharedMesh.vertices;
                    for (int i = 0; i + 1 < vertices.Length; i += 2)
                        if (NavMesh.SamplePosition(road.transform.TransformPoint((vertices[i] + vertices[i + 1]) * .5f), out var hit, 3, NavMesh.AllAreas))
                        { PlaceCar(Ground(hit.position) + Vector3.up * .7f, testRotation); roadFound = true; break; }
                }
            }
            yield return null; Capture("white-range-road");
            PlaceCar(Ground(darkPoint) + Vector3.up * .7f, testRotation); yield return null; Capture("white-range-dark-terrain");
            PlaceCar(Ground(brightPoint) + Vector3.up * .7f, testRotation); yield return null; Capture("white-range-bright-terrain");
            Check("white ring captured over varied actual terrain", roadFound && brightest - darkest > .15f,
                "Actual road mesh found=" + roadFound + "; navigable baked terrain luminance dark=" + darkest + "; bright=" + brightest +
                "; screenshots retain real terrain, lighting, white contour and thin dark contrast");
            PlaceCar(testOrigin, testRotation);
        }

        int MagentaPixelCount()
        {
            var camera = s.input.view; const int width = 540, height = 960;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasState { canvas = c, mode = c.renderMode, camera = c.worldCamera, plane = c.planeDistance }).ToArray();
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = target;
                foreach (var state in canvases)
                {
                    state.canvas.renderMode = RenderMode.ScreenSpaceCamera; state.canvas.worldCamera = camera;
                    state.canvas.planeDistance = Mathf.Max(camera.nearClipPlane + .5f, 1);
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                return texture.GetPixels32().Count(c => c.r > 215 && c.g < 40 && c.b > 215);
            }
            finally
            {
                foreach (var state in canvases) if (state.canvas)
                { state.canvas.renderMode = state.mode; state.canvas.worldCamera = state.camera; state.canvas.planeDistance = state.plane; }
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target); DestroyImmediate(texture); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
#endif
