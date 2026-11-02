using UnityEngine;
namespace RoadGunner
{
    public sealed class CombatEffects : MonoBehaviour
    {
        public LineRenderer[] tracers;
        public Transform[] sparks;
        public Transform flash;
        public AudioSource source;
        public AudioClip gun,impact,pickup;
        public AudioClip explosion;
        public VehicleExplosion[] explosions;
        public ArcadeFeedback feedback;
        [Header("Arcade look (HDR tints glow under bloom)")]
        [Tooltip("Optional camera transform: impact flashes face it.")] public Transform view;
        [ColorUsage(false,true)] public Color playerTracer=new Color(1,.72f,.2f), hostileTracer=new Color(1,.28f,.08f);
        [Min(.02f)] public float tracerTime=.055f, sparkTime=.11f, flashTime=.045f;
        [Tooltip("Tracer tail chases the head over its life (0 = static line).")] [Range(0,1)] public float tracerChase;
        [Tooltip("Flash mesh is a flame along the barrel (+Z) that stretches over its life; off = legacy spike.")] public bool flameFlash;
        bool LegacyAudio => source && (!feedback || !feedback.ownsAudio);
        public int ExplosionsPlayed { get; private set; }
        float[] tracerUntil,sparkUntil;
        Vector3[] tracerFrom,tracerTo;
        Vector3 sparkScale,flashScale;
        float flashUntil,nextImpact;
        int nextTracer,nextSpark;
        MaterialPropertyBlock tracerTint;
        int nextExplosion;
        static readonly int BaseColor=Shader.PropertyToID("_BaseColor");
        public void ResetPool() {
            tracerUntil=new float[tracers.Length];sparkUntil=new float[sparks.Length];tracerFrom=new Vector3[tracers.Length];tracerTo=new Vector3[tracers.Length];
            if(sparkScale==Vector3.zero&&sparks.Length>0&&sparks[0])sparkScale=sparks[0].localScale;if(flashScale==Vector3.zero&&flash)flashScale=flash.localScale;
            foreach(var l in tracers)l.enabled=false;foreach(var s in sparks){s.gameObject.SetActive(false);if(sparkScale!=Vector3.zero)s.localScale=sparkScale;}
            flash.gameObject.SetActive(false);if(flashScale!=Vector3.zero)flash.localScale=flashScale;
            if(explosions!=null)foreach(var e in explosions)if(e)e.Clear();if(source)source.Stop();nextTracer=nextSpark=nextExplosion=0;
        }
        public void Explode(Vector3 position) {
            if(explosions==null||explosions.Length==0)return;
            explosions[nextExplosion++%explosions.Length].PlayAt(position);ExplosionsPlayed++;
            if(LegacyAudio&&explosion)source.PlayOneShot(explosion,.35f);
            if(feedback)feedback.VehicleExploded(position);
        }
        public void Shot(Vector3 from,Vector3 to,Quaternion rotation,bool hostile=false) {
            if(tracerUntil==null)ResetPool();
            int i=nextTracer++%tracers.Length;tracers[i].SetPosition(0,from);tracers[i].SetPosition(1,to);tracers[i].enabled=true;tracerUntil[i]=Time.time+tracerTime;tracerFrom[i]=from;tracerTo[i]=to;
            if(tracerTint==null)tracerTint=new MaterialPropertyBlock();
            tracerTint.SetColor(BaseColor,hostile?hostileTracer:playerTracer);tracers[i].SetPropertyBlock(tracerTint);
            if(!hostile){
                // Flame tongues point along the barrel with a random roll; legacy spike meshes keep their old orientation.
                flash.SetPositionAndRotation(from,flameFlash?rotation*Quaternion.Euler(0,0,Random.Range(0,60f)):rotation*Quaternion.Euler(90,0,0));
                flash.gameObject.SetActive(true);flashUntil=Time.time+flashTime;
            }
            if(LegacyAudio&&gun)source.PlayOneShot(gun,hostile?.15f:.22f);
            if(feedback)feedback.Shot(from,to,hostile);
        }
        public void Spark(Vector3 position) {
            if(sparkUntil==null)ResetPool();
            if(LegacyAudio&&impact&&Time.time>=nextImpact){source.PlayOneShot(impact,.12f);nextImpact=Time.time+.08f;}if(feedback)feedback.Impact(position);
            int i=nextSpark++%sparks.Length;sparks[i].position=position;if(view)sparks[i].rotation=view.rotation*Quaternion.Euler(0,0,Random.Range(0,90f));
            sparks[i].gameObject.SetActive(true);sparkUntil[i]=Time.time+sparkTime;
        }
        public void Pickup(){if(LegacyAudio&&pickup)source.PlayOneShot(pickup,.25f);}
        void Update() {
            if(tracerUntil==null)return;float now=Time.time;
            for(int i=0;i<tracers.Length;i++){
                if(!tracers[i].enabled)continue;
                if(now>tracerUntil[i]){tracers[i].enabled=false;continue;}
                if(tracerChase>0){float k=1-(tracerUntil[i]-now)/tracerTime;tracers[i].SetPosition(0,Vector3.Lerp(tracerFrom[i],tracerTo[i],k*k*tracerChase));}
            }
            for(int i=0;i<sparks.Length;i++){
                if(!sparks[i].gameObject.activeSelf)continue;
                if(now>sparkUntil[i]){sparks[i].gameObject.SetActive(false);continue;}
                // Impact star: pop in, then collapse.
                if(view&&sparkScale!=Vector3.zero){float k=1-(sparkUntil[i]-now)/sparkTime;sparks[i].localScale=sparkScale*(k<.25f?Mathf.Lerp(.55f,1.2f,k/.25f):Mathf.Lerp(1.2f,.05f,(k-.25f)/.75f));}
            }
            if(flash.gameObject.activeSelf){
                if(now>flashUntil)flash.gameObject.SetActive(false);
                else if(flameFlash&&flashScale!=Vector3.zero){float k=1-(flashUntil-now)/flashTime;flash.localScale=new Vector3(flashScale.x,flashScale.y,flashScale.z*(k<.35f?Mathf.Lerp(.7f,1.15f,k/.35f):Mathf.Lerp(1.15f,.45f,(k-.35f)/.65f)));}
            }
        }
    }
}
