using UnityEngine;
namespace RoadGunner {
 [CreateAssetMenu(menuName="Road Gunner/Wasteland Visual Preset")]
 public sealed class WastelandVisualPreset:ScriptableObject {
  public string visualVersion="Concept Wasteland / 2026-10-16";
  public Material[] sharedMaterials;
  public Color sand=new Color(.867f,.718f,.482f);
  public Color playerTeal=new Color(.224f,.486f,.482f);
  public Color ivory=new Color(.847f,.812f,.714f);
  public Color rust=new Color(.678f,.376f,.231f);
  public Color repairMint=new Color(.522f,.839f,.678f);
  public Vector3 sunlightEuler=new Vector3(52,-35,0);
  public float sunlightIntensity=1.65f;
  [Range(0,1)]public float shadowStrength=.83f;
  [TextArea]public string authoringNotes="Use the shared palette and ConceptArt builders. Keep visual assemblies separate from physics and aiming roots. Broad wear, moderately beveled forms, quiet sand; no photographic noise. Camera and gameplay tuning remain independent.";
 }
}
