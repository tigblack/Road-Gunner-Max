using UnityEngine;

namespace RoadGunner
{
    /// <summary>
    /// ArcadeVfx world-space helpers: telegraph danger zones with a timed progress disc and a bobbing '!' alert, ground decals
    /// (explosion scorch, slam cracks), upgrade-station beacons, gold loot halos + twinkles, velocity-aligned speed streaks and
    /// victory confetti that never draws over the result card. All objects are pooled and created by ArcadeVfxInstaller.
    /// </summary>
    public sealed partial class ArcadeVfx
    {
        [Header("Telegraph zones (flat quads per slot: zone fill, progress disc, progress front ring, pulsing edge ring, '!' alert)")]
        public Renderer[] telegraphZones;
        public Renderer[] telegraphProgress, telegraphFronts, telegraphEdges;
        public Transform[] telegraphAlerts;
        [Tooltip("Ring radius as a fraction of the quad half-size (matches the generated ring textures).")] public float ringTextureRadius = .84f;
        [Tooltip("Disc radius as a fraction of the quad half-size (matches the generated disc texture).")] public float discTextureRadius = .94f;
        [Header("Ground decals")]
        public Renderer[] scorches;
        public Renderer[] cracks;
        [Header("Upgrade station beacons (runtime, one slot per ArenaRunStats.stations entry)")]
        public Renderer[] beaconRings;
        public Renderer[] beaconColumns, beaconIcons;
        public Material attackIconMaterial, healthIconMaterial;
        [Header("Loot")]
        [Tooltip("Pooled soft gold halo billboards that follow active gear pickups (HDR alpha-blend so they glow even on pale sand).")]
        public Renderer[] lootHalos;
        [Tooltip("_BaseColor tint written once (MaterialPropertyBlock) on the pooled gear visuals so loot reads gold.")] public Color gearTint = new Color(1.5f, 1.12f, .42f, 1);
        [Header("Speed streaks (velocity-aligned, near the car sides)")]
        public ParticleSystem streaks;
        [Range(0, 1)] public float streakSpeedFraction = .6f;
        [Min(0)] public float streakRate = 16;
        [Header("Victory confetti overlay (inactive until victory)")]
        public GameObject confettiRoot;
        public CanvasGroup confettiGroup;
        public RectTransform[] confetti;

        struct Warning { public CombatTarget attacker; public Vector3 point; public float radius, start, duration, alertHeight; public bool active; }
        struct DecalState { public float start, life; public Color color; public bool active; }
        struct Flake { public Vector2 position, velocity; public float angle, spin, flip, phase; }

        static readonly Color ZoneRed = new Color(1f, .23f, .19f), ProgressRed = new Color(.92f, .14f, .09f), EdgeHot = new Color(1f, .88f, .3f);
        static readonly Color EdgeRed = new Color(.85f, .13f, .08f), EdgeOrange = new Color(1f, .42f, .12f);
        static readonly Color BeaconGold = new Color(1f, .79f, .24f), BeaconMint = new Color(.31f, .88f, .64f);
        static readonly Color ScorchColor = new Color(.37f, .25f, .27f, .55f), CrackColor = new Color(.37f, .23f, .2f, .75f);
        static readonly Color[] ConfettiColors = { new Color(1f, .79f, .24f), new Color(.12f, .72f, .69f), new Color(.31f, .88f, .64f), new Color(.25f, .82f, 1f), new Color(1f, .48f, .1f), new Color(1f, .96f, .86f), new Color(.91f, .28f, .23f) };

        readonly Warning[] warnings = new Warning[2];
        DecalState[] scorchState, crackState;
        Flake[] flakes;
        readonly Vector3[] haloAt = new Vector3[16];
        MaterialPropertyBlock gearBlock;
        readonly Vector3[] corners = new Vector3[4];
        float confettiStart = -1, nextTwinkle, streakAccum;
        Collider roadSurface; bool roadSurfaceSearched;
        int twinkle;
        bool gearsTinted;

        void AwakeWorld()
        {
            scorchState = new DecalState[scorches != null ? scorches.Length : 0];
            crackState = new DecalState[cracks != null ? cracks.Length : 0];
            flakes = new Flake[confetti != null ? confetti.Length : 0];
            gearBlock = new MaterialPropertyBlock();
        }

        // ---------- telegraphs ----------

        public override void AttackTelegraph(CombatTarget attacker, Vector3 point, float radius, float duration)
        {
            int slot = 0; float oldest = float.MaxValue;
            for (int i = 0; i < warnings.Length; i++)
            {
                if (!warnings[i].active || warnings[i].attacker == attacker) { slot = i; break; }
                if (warnings[i].start < oldest) { oldest = warnings[i].start; slot = i; }
            }
            float top = 2.6f;
            if (attacker)
            {
                // Once per telegraph (not per frame): the hitbox tells how tall the attacker is, so the '!' floats just above its head.
                var hitbox = attacker.GetComponent<Collider>();
                top = hitbox && hitbox.enabled ? hitbox.bounds.max.y - attacker.transform.position.y : 2.6f;
            }
            warnings[slot] = new Warning { attacker = attacker, point = point, radius = Mathf.Max(.3f, radius), start = clock, duration = Mathf.Max(.2f, duration), alertHeight = Mathf.Clamp(top, 1.2f, 6f) + 2.1f, active = true };
        }

        float EndWarningFor(CombatTarget attacker, float fallbackRadius)
        {
            float radius = fallbackRadius;
            for (int i = 0; i < warnings.Length; i++) if (warnings[i].active && warnings[i].attacker == attacker) { radius = warnings[i].radius; EndWarning(i); }
            return radius;
        }

        void AnimateWarnings()
        {
            for (int i = 0; i < warnings.Length; i++)
            {
                if (!warnings[i].active) continue;
                var w = warnings[i];
                float age = clock - w.start, k = age / w.duration;
                if (k >= 1.05f || (w.attacker && !w.attacker.Alive)) { EndWarning(i); continue; }
                k = Mathf.Clamp01(k);
                float ringQuad = 2 * w.radius / Mathf.Max(.1f, ringTextureRadius), discQuad = 2 * w.radius / Mathf.Max(.1f, discTextureRadius);
                Vector3 p = w.point + Vector3.up * .07f;
                float pulse = .5f + .5f * Mathf.Sin(age * Mathf.PI * 2 * 6);
                float appear = EaseOut(age / .12f);

                var zone = Slot(telegraphZones, i);
                if (zone)
                {
                    Show(zone, true);
                    zone.transform.SetPositionAndRotation(p, Quaternion.identity); zone.transform.localScale = new Vector3(discQuad, 1, discQuad) * Mathf.Lerp(.85f, 1, appear);
                    var c = ZoneRed; c.a = (.36f + .07f * pulse) * appear; Tint(zone, c);
                }
                // The progress disc reaches the edge exactly when the strike lands: linear in time so the timing can be read.
                var progress = Slot(telegraphProgress, i);
                if (progress)
                {
                    Show(progress, k > .01f);
                    progress.transform.SetPositionAndRotation(p + Vector3.up * .006f, Quaternion.identity); progress.transform.localScale = new Vector3(discQuad * k, 1, discQuad * k);
                    var c = ProgressRed; c.a = .38f + .14f * k; Tint(progress, c);
                }
                var front = Slot(telegraphFronts, i);
                if (front)
                {
                    Show(front, k > .01f);
                    front.transform.SetPositionAndRotation(p + Vector3.up * .012f, Quaternion.identity); front.transform.localScale = new Vector3(ringQuad * k, 1, ringQuad * k);
                    Tint(front, I(Color.Lerp(EdgeHot, Orange, .4f), 1.1f + .6f * k));
                }
                var edge = Slot(telegraphEdges, i);
                if (edge)
                {
                    Show(edge, true);
                    edge.transform.SetPositionAndRotation(p + Vector3.up * .018f, Quaternion.identity); edge.transform.localScale = new Vector3(ringQuad, 1, ringQuad) * Mathf.Lerp(1.15f, 1, appear);
                    // Alpha-blended saturated red band (reads on pale sand and on terracotta), pulsing toward hot orange at 6 Hz.
                    var c = Color.Lerp(EdgeRed, EdgeOrange, pulse); c.a = .95f * appear; Tint(edge, c);
                }
                var alert = Slot(telegraphAlerts, i);
                if (alert)
                {
                    Show(alert, true);
                    Vector3 anchor = w.attacker ? w.attacker.transform.position : w.point;
                    float bob = Mathf.Abs(Mathf.Sin(age * Mathf.PI * 2.6f)) * .28f;
                    alert.SetPositionAndRotation(anchor + Vector3.up * (w.alertHeight + bob), view ? view.rotation : Quaternion.identity);
                    float pop = age < .16f ? BackOut(age / .16f) : 1 + .08f * pulse;
                    alert.localScale = Vector3.one * (1.25f * Mathf.Max(0, pop));
                }
            }
        }

        void EndWarning(int i)
        {
            warnings[i] = default;
            Show(Slot(telegraphZones, i), false); Show(Slot(telegraphProgress, i), false); Show(Slot(telegraphFronts, i), false);
            Show(Slot(telegraphEdges, i), false); Show(Slot(telegraphAlerts, i), false);
        }

        void HideWarnings() { for (int i = 0; i < warnings.Length; i++) EndWarning(i); }

        // ---------- ground decals ----------

        void Decal(Renderer[] pool, DecalState[] states, Vector3 position, float size, Color color, float life)
        {
            if (pool == null || states == null || pool.Length == 0 || states.Length != pool.Length) return;
            int slot = 0; float oldest = float.MaxValue;
            for (int i = 0; i < states.Length; i++)
            {
                if (!states[i].active) { slot = i; break; }
                if (states[i].start < oldest) { oldest = states[i].start; slot = i; }
            }
            var r = pool[slot]; if (!r) return;
            Vector3 p = position, normal = Vector3.up;
            // Prefer the terrain collider (never a roof or prop), then any default-layer surface below. The opaque
            // road union sits ~3.5 cm above the terrain on lanes and pockets, so take whichever surface is higher.
            var ray = new Ray(position + Vector3.up * 6, Vector3.down);
            var ground = session && session.pickups ? session.pickups.ground : null;
            if (!roadSurfaceSearched) { roadSurfaceSearched = true; var road = GameObject.Find("Physical connected roads and combat pockets"); if (road) roadSurface = road.GetComponent<Collider>(); }
            RaycastHit hit; bool found = false;
            if (ground && ground.Raycast(ray, out hit, 20)) { p = hit.point; normal = hit.normal; found = true; }
            if (roadSurface && roadSurface.Raycast(ray, out hit, 20) && (!found || hit.point.y > p.y)) { p = hit.point; normal = hit.normal; found = true; }
            if (!found && Physics.Raycast(ray, out hit, 20, 1, QueryTriggerInteraction.Ignore)) { p = hit.point; normal = hit.normal; }
            r.transform.SetPositionAndRotation(p + normal * (.06f + slot * .003f), Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Random.Range(0, 360f), 0));
            r.transform.localScale = new Vector3(size, 1, size);
            states[slot] = new DecalState { start = clock, life = life, color = color, active = true };
            var c = color; c.a = 0; Tint(r, c);
            Show(r, true);
        }

        void AnimateDecals() { AnimateDecals(scorches, scorchState); AnimateDecals(cracks, crackState); }

        void AnimateDecals(Renderer[] pool, DecalState[] states)
        {
            if (pool == null || states == null) return;
            for (int i = 0; i < states.Length && i < pool.Length; i++)
            {
                if (!states[i].active) continue;
                var r = pool[i];
                float age = clock - states[i].start, k = age / states[i].life;
                if (!r || k >= 1) { states[i].active = false; Show(r, false); continue; }
                float alpha = Mathf.Clamp01(age / .06f) * (1 - Smooth((k - .4f) / .6f));
                var c = states[i].color; c.a *= alpha; Tint(r, c);
            }
        }

        void HideDecals()
        {
            if (scorchState != null) for (int i = 0; i < scorchState.Length; i++) { scorchState[i].active = false; Show(Slot(scorches, i), false); }
            if (crackState != null) for (int i = 0; i < crackState.Length; i++) { crackState[i].active = false; Show(Slot(cracks, i), false); }
        }

        // ---------- upgrade station beacons ----------

        void AnimateBeacons()
        {
            if (beaconRings == null) return;
            var stations = session && session.runStats ? session.runStats.stations : null;
            for (int i = 0; i < beaconRings.Length; i++)
            {
                var station = stations != null && i < stations.Length ? stations[i] : null;
                bool on = station && !station.Consumed && station.isActiveAndEnabled && !finale;
                var ring = beaconRings[i]; var column = Slot(beaconColumns, i); var icon = Slot(beaconIcons, i);
                Show(ring, on); Show(column, on); Show(icon, on);
                if (!on) continue;
                bool attack = station.kind == ArenaUpgradeKind.Attack;
                Color c = attack ? BeaconGold : BeaconMint;
                float t = clock + i * .37f, pulse = .5f + .5f * Mathf.Sin(t * Mathf.PI * 2 / 1.2f);
                Vector3 basePos = station.transform.position;
                float radius = Mathf.Clamp(station.radius * .7f, 1.6f, 2.8f), quad = 2 * radius / Mathf.Max(.1f, ringTextureRadius);
                if (ring)
                {
                    ring.transform.SetPositionAndRotation(basePos + Vector3.up * .08f, Quaternion.Euler(0, t * 25, 0));
                    ring.transform.localScale = new Vector3(quad, 1, quad) * (1 + .04f * pulse);
                    var rc = I(c, 1.35f); rc.a = .45f + .3f * pulse; Tint(ring, rc);
                }
                if (column)
                {
                    Vector3 toCam = view ? Vector3.ProjectOnPlane(-view.forward, Vector3.up) : Vector3.back;
                    column.transform.SetPositionAndRotation(basePos + Vector3.up * 2.1f, toCam.sqrMagnitude > .001f ? Quaternion.LookRotation(-toCam) : Quaternion.identity);
                    column.transform.localScale = new Vector3(radius * .9f, 4.4f, 1);
                    var cc = I(c, 1.7f); cc.a = .26f + .12f * pulse; Tint(column, cc);
                }
                if (icon)
                {
                    var wanted = attack ? attackIconMaterial : healthIconMaterial;
                    if (wanted && icon.sharedMaterial != wanted) icon.sharedMaterial = wanted;
                    float bob = Mathf.Sin(t * Mathf.PI * 2 / 1.2f) * .2f;
                    icon.transform.SetPositionAndRotation(basePos + Vector3.up * (4.4f + bob), (view ? view.rotation : Quaternion.identity) * Quaternion.Euler(0, Mathf.Sin(t * 1.3f) * 35, 0));
                    icon.transform.localScale = Vector3.one * (1.75f + .08f * pulse);
                }
            }
        }

        void HideBeacons()
        {
            if (beaconRings == null) return;
            for (int i = 0; i < beaconRings.Length; i++) { Show(beaconRings[i], false); Show(Slot(beaconColumns, i), false); Show(Slot(beaconIcons, i), false); }
        }

        // ---------- loot ----------

        void AnimateLoot()
        {
            var pool = session ? session.pickups : null;
            if (!pool || pool.items == null) { HideHalos(); return; }
            if (!gearsTinted) TintGears(pool);
            var items = pool.items; var gears = pool.gearVisuals;
            int slots = lootHalos != null ? lootHalos.Length : 0, n = 0, found = 0;
            Quaternion facing = view ? view.rotation : Quaternion.identity;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (!item || !item.gameObject.activeInHierarchy) continue;
                if (gears != null && i < gears.Length && gears[i] && !gears[i].gameObject.activeSelf) continue;
                if (found < haloAt.Length) haloAt[found] = item.position;
                found++;
                if (n >= slots) continue;
                var halo = lootHalos[n++]; if (!halo) continue;
                Show(halo, true);
                float pulse = .5f + .5f * Mathf.Sin(clock * 5.5f + i * 1.7f);
                halo.transform.SetPositionAndRotation(item.position - (view ? view.forward : Vector3.down) * .4f, facing);
                halo.transform.localScale = Vector3.one * (1.5f + .3f * pulse);
            }
            for (int i = n; i < slots; i++) Show(lootHalos[i], false);
            int count = Mathf.Min(found, haloAt.Length);
            if (count > 0 && clock >= nextTwinkle && sparkles)
            {
                nextTwinkle = clock + .14f;
                var at = haloAt[twinkle++ % count];
                Emit(sparkles, at + new Vector3(Random.Range(-.35f, .35f), Random.Range(.15f, .5f), Random.Range(-.35f, .35f)), 1, GoldHot, Cream, R(.42f, .62f), R(0, .3f), R(.3f, .42f));
            }
        }

        void HideHalos() { if (lootHalos != null) for (int i = 0; i < lootHalos.Length; i++) Show(lootHalos[i], false); }

        void TintGears(PickupPool pool)
        {
            gearsTinted = true;
            if (pool.gearVisuals == null) return;
            gearBlock.Clear(); gearBlock.SetColor(BaseColorId, gearTint);
            foreach (var gear in pool.gearVisuals)
            {
                if (!gear) continue;
                var r = gear.GetComponent<Renderer>();
                if (r) r.SetPropertyBlock(gearBlock);
            }
        }

        // ---------- speed streaks ----------

        void AnimateStreaks()
        {
            if (!streaks || !vehicle || !vehicle.Body) return;
            float max = vehicle.tuning ? Mathf.Max(1, vehicle.tuning.maxSpeed) : 12;
            float speed = vehicle.HorizontalSpeed, from = max * streakSpeedFraction;
            bool on = !finale && session && session.Playing && vehicle.GroundedWheels > 0 && speed > from && Time.deltaTime > 0;
            if (!on) { streakAccum = 0; return; }
            Vector3 velocity = Vector3.ProjectOnPlane(vehicle.Body.linearVelocity, Vector3.up);
            if (velocity.sqrMagnitude < .01f) return;
            Vector3 dir = velocity.normalized, side = Vector3.Cross(Vector3.up, dir);
            float k = Mathf.InverseLerp(from, max, speed);
            streakAccum += Time.deltaTime * streakRate * (.35f + .65f * k);
            Play(streaks);
            Vector3 car = vehicle.transform.position;
            while (streakAccum >= 1)
            {
                streakAccum -= 1;
                float s = Random.value < .5f ? -1 : 1;
                emit.position = car + side * (s * Random.Range(1.3f, 1.9f)) + dir * Random.Range(-1.8f, .5f) + Vector3.up * Random.Range(.25f, .7f);
                // Moving with the car but slower: the streak slides back along the car's flank and stretches along its velocity.
                emit.velocity = velocity * Random.Range(.45f, .6f);
                emit.startLifetime = Random.Range(.18f, .26f);
                emit.startSize = Random.Range(.1f, .14f);
                emit.startColor = new Color(1f, .98f, .93f, .3f + .05f * k);
                streaks.Emit(emit, 1);
            }
            emit.ResetPosition(); emit.ResetVelocity(); emit.ResetStartLifetime(); emit.ResetStartSize(); emit.ResetStartColor();
        }

        void ClearStreaks() { streakAccum = 0; if (streaks) streaks.Clear(true); }

        // ---------- victory confetti ----------

        void StartConfetti()
        {
            if (!confettiRoot || flakes == null || flakes.Length == 0 || confetti == null) return;
            confettiRoot.SetActive(true); confettiStart = clock;
            if (confettiGroup) confettiGroup.alpha = 1;
            var area = ((RectTransform)confettiRoot.transform).rect.size;
            if (area.x < 10 || area.y < 10) area = new Vector2(1080, 1920);
            for (int i = 0; i < flakes.Length && i < confetti.Length; i++)
            {
                // Two cannons in the top corners fire inward and down, plus a light curtain along the top edge;
                // pieces that drift over the result card are hidden there, so they read as falling behind it.
                float side = i % 2 == 0 ? -1 : 1;
                bool curtain = i % 4 == 3;
                float angle = Random.Range(-25f, 35f) * Mathf.Deg2Rad, speed = Random.Range(900f, 1700f) * area.y / 1920f;
                flakes[i] = new Flake
                {
                    position = curtain ? new Vector2(Random.Range(-.5f, .5f) * area.x, area.y * .5f + Random.Range(10f, 160f))
                        : new Vector2(side * area.x * .5f, area.y * (.5f - Random.Range(.02f, .1f))),
                    velocity = curtain ? new Vector2(Random.Range(-90f, 90f), -Random.Range(120f, 320f)) : new Vector2(-side * Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed),
                    angle = Random.Range(0, 360f), spin = Random.Range(-540f, 540f), flip = Random.Range(5f, 11f), phase = Random.Range(0, 6.3f)
                };
                var piece = confetti[i];
                if (!piece) continue;
                var image = piece.GetComponent<UnityEngine.UI.Graphic>();
                if (image) image.color = ConfettiColors[i % ConfettiColors.Length];
                piece.anchoredPosition = flakes[i].position;
            }
        }

        void AnimateConfetti()
        {
            if (confettiStart < 0 || !confettiRoot || confetti == null) return;
            float age = clock - confettiStart, dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
            if (age > 3.6f) { confettiStart = -1; confettiRoot.SetActive(false); return; }
            if (confettiGroup) confettiGroup.alpha = 1 - Mathf.Clamp01((age - 2.9f) / .7f);
            bool masked = CardRect(out Rect card);
            for (int i = 0; i < flakes.Length && i < confetti.Length; i++)
            {
                var piece = confetti[i]; if (!piece) continue;
                ref var f = ref flakes[i];
                f.velocity.y -= 1500f * dt; f.velocity *= 1 - Mathf.Min(.9f, 1.4f * dt);
                if (f.velocity.y < -380f) f.velocity.y = -380f;
                f.position += f.velocity * dt; f.angle += f.spin * dt;
                Vector2 at = f.position + new Vector2(Mathf.Sin(f.phase + age * 7) * 24, 0);
                piece.anchoredPosition = at;
                piece.localRotation = Quaternion.Euler(0, 0, f.angle);
                bool hidden = masked && card.Contains(at);
                piece.localScale = hidden ? Vector3.zero : new Vector3(Mathf.Cos(f.phase + age * f.flip), 1, 1);
            }
        }

        /// <summary>Result card (+ its title ribbon) in confetti-canvas local units; false when the HUD is not available.</summary>
        bool CardRect(out Rect rect)
        {
            rect = default;
            var hud = session ? session.hud : null;
            var root = confettiRoot ? confettiRoot.transform as RectTransform : null;
            if (!hud || !root || !hud.overlayCard) return false;
            if (!CornersRect(hud.overlayCard.rectTransform, root, ref rect, false)) return false;
            if (hud.overlayKicker) CornersRect(hud.overlayKicker.rectTransform, root, ref rect, true);
            rect.xMin -= 12; rect.xMax += 12; rect.yMin -= 12; rect.yMax += 12;
            return true;
        }

        bool CornersRect(RectTransform target, RectTransform root, ref Rect rect, bool merge)
        {
            if (!target || !target.gameObject.activeInHierarchy) return false;
            var canvas = target.GetComponentInParent<Canvas>();
            Camera cam = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            target.GetWorldCorners(corners);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local)) return false;
                xMin = Mathf.Min(xMin, local.x); yMin = Mathf.Min(yMin, local.y); xMax = Mathf.Max(xMax, local.x); yMax = Mathf.Max(yMax, local.y);
            }
            var r = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            if (merge) r = Rect.MinMaxRect(Mathf.Min(r.xMin, rect.xMin), Mathf.Min(r.yMin, rect.yMin), Mathf.Max(r.xMax, rect.xMax), Mathf.Max(r.yMax, rect.yMax));
            rect = r;
            return true;
        }
    }
}
