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
        public int ExplosionsPlayed { get; private set; }
        float[] tracerUntil,sparkUntil;
        float flashUntil,nextImpact;
        int nextTracer,nextSpark;
        MaterialPropertyBlock tracerTint;
        int nextExplosion;
        public void ResetPool() {tracerUntil=new float[tracers.Length];sparkUntil=new float[sparks.Length];foreach(var l in tracers)l.enabled=false;foreach(var s in sparks)s.gameObject.SetActive(false);flash.gameObject.SetActive(false);if(explosions!=null)foreach(var e in explosions)if(e)e.Clear();if(source)source.Stop();nextTracer=nextSpark=nextExplosion=0;}
        public void Explode(Vector3 position) {
            if(explosions==null||explosions.Length==0)return;
            explosions[nextExplosion++%explosions.Length].PlayAt(position);ExplosionsPlayed++;
            if(source&&explosion)source.PlayOneShot(explosion,.35f);
        }
        public void Shot(Vector3 from,Vector3 to,Quaternion rotation,bool hostile=false) {
            int i=nextTracer++%tracers.Length;tracers[i].SetPosition(0,from);tracers[i].SetPosition(1,to);tracers[i].enabled=true;tracerUntil[i]=Time.time+.055f;
            if(tracerTint==null)tracerTint=new MaterialPropertyBlock();
            tracerTint.SetColor("_BaseColor",hostile?new Color(1,.28f,.08f):new Color(1,.72f,.2f));tracers[i].SetPropertyBlock(tracerTint);
            if(!hostile){flash.SetPositionAndRotation(from,rotation*Quaternion.Euler(90,0,0));flash.gameObject.SetActive(true);flashUntil=Time.time+.045f;}
            if(source&&gun)source.PlayOneShot(gun,hostile?.15f:.22f);
        }
        public void Spark(Vector3 position) {if(source&&impact&&Time.time>=nextImpact){source.PlayOneShot(impact,.12f);nextImpact=Time.time+.08f;}int i=nextSpark++%sparks.Length;sparks[i].position=position;sparks[i].gameObject.SetActive(true);sparkUntil[i]=Time.time+.11f;}
        public void Pickup(){if(source&&pickup)source.PlayOneShot(pickup,.25f);}
        void Update() {if(tracerUntil==null)return;for(int i=0;i<tracers.Length;i++)if(Time.time>tracerUntil[i])tracers[i].enabled=false;for(int i=0;i<sparks.Length;i++)if(Time.time>sparkUntil[i])sparks[i].gameObject.SetActive(false);if(Time.time>flashUntil)flash.gameObject.SetActive(false);}
    }
}
