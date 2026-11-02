using UnityEngine;

namespace RoadGunner
{
    /// <summary>
    /// Arcade styling for a world TextMesh label (TextMesh has no outline): up to eight dark outline copies and an
    /// optional rounded plate behind the text, both children of the label so they follow its activity and transform.
    /// Mirrors the label text when gameplay rewrites it (stations, caches) and resizes the plate from the font metrics.
    /// Legacy mode (older installs): when <see cref="source"/> is set this component sits on a single shadow copy and
    /// only mirrors the source text.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TextMesh))]
    public sealed class ArcadeHudWorldLabel : MonoBehaviour
    {
        [Tooltip("Legacy single-shadow mode only: the label this copy mirrors.")]
        public TextMesh source;
        public TextMesh[] outlines = new TextMesh[0];
        public SpriteRenderer plate;
        [Tooltip("Plate padding around the text block, in em units of the label.")]
        public Vector2 platePadding = new Vector2(.42f, .2f);
        public float plateDepth = .02f;

        TextMesh label;
        string shown;
        float nextPoll;

        void OnEnable()
        {
            label = GetComponent<TextMesh>();
            shown = null;
            Sync();
        }

        void LateUpdate()
        {
            // TextMesh.text returns a new managed string on every read: poll rarely and compare by value.
            // Gameplay only rewrites these labels on reset/claim, and re-activation goes through OnEnable.
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .3f;
            var from = source ? source : label;
            if (from && from.text != shown) Sync();
        }

        void Sync()
        {
            if (!label) label = GetComponent<TextMesh>();
            if (source)
            {
                shown = source.text;
                if (label) label.text = shown;
                return;
            }
            if (!label) return;
            shown = label.text;
            if (outlines != null)
                for (int i = 0; i < outlines.Length; i++)
                    if (outlines[i]) outlines[i].text = shown;
            ResizePlate(shown);
        }

        void ResizePlate(string text)
        {
            if (!plate || !label) return;
            var font = label.font;
            text ??= string.Empty;
            int size = label.fontSize > 0 ? label.fontSize : font ? Mathf.Max(1, font.fontSize) : 16;
            float pixel = label.characterSize * .1f; // TextMesh: one font pixel = characterSize / 10 local units.
            float em = size * pixel;
            float widest = 0, line = 0;
            int lines = 1;
            if (font && font.dynamic) font.RequestCharactersInTexture(text, size, label.fontStyle);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n') { widest = Mathf.Max(widest, line); line = 0; lines++; continue; }
                if (font && font.GetCharacterInfo(c, out var info, size, label.fontStyle)) line += info.advance;
                else line += size * .5f;
            }
            widest = Mathf.Max(widest, line) * pixel;
            float lineHeight = font && font.dynamic && font.fontSize > 0 ? font.lineHeight * size / (float)font.fontSize : size * 1.15f;
            float height = (lines * lineHeight * label.lineSpacing - (lineHeight - size) * .35f) * pixel;
            if (string.IsNullOrEmpty(text)) { widest = 0; height = em; }
            var plateSize = new Vector2(widest + em * platePadding.x * 2, height + em * platePadding.y * 2);
            plateSize.x = Mathf.Max(plateSize.x, plateSize.y * 1.1f);
            if (plate.drawMode != SpriteDrawMode.Sliced) plate.drawMode = SpriteDrawMode.Sliced;
            // Slice borders are fixed in sprite units: scale the renderer so the plate keeps fully rounded ends.
            float native = plate.sprite ? Mathf.Max(.01f, plate.sprite.bounds.size.y) : 1;
            float scale = Mathf.Max(.01f, plateSize.y / native);
            var t = plate.transform;
            t.localScale = new Vector3(scale, scale, 1);
            plate.size = plateSize / scale;
            Vector2 offset = Vector2.zero;
            switch (label.anchor)
            {
                case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: offset.x = widest * .5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: offset.x = -widest * .5f; break;
            }
            switch (label.anchor)
            {
                case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: offset.y = -height * .5f; break;
                case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: offset.y = height * .5f; break;
            }
            t.localPosition = new Vector3(offset.x, offset.y, plateDepth);
        }
    }
}
