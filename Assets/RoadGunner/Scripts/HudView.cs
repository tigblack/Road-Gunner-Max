using UnityEngine;
using UnityEngine.UI;
namespace RoadGunner
{
    public sealed class HudView : MonoBehaviour
    {
        public GameSession session;
        public Image healthFill;
        public Text healthText,scrapText,killsText,speedText,overlayTitle,overlayScore,debugText;
        public GameObject overlay;
        public Button pause,resume,restart,recover;
        float nextRefresh,frameTime;
        void Start() {pause.onClick.AddListener(session.TogglePause);resume.onClick.AddListener(session.TogglePause);restart.onClick.AddListener(session.Restart);recover.onClick.AddListener(session.Recover);session.Changed+=Refresh;session.health.Changed+=Refresh;Refresh();}
        void OnDestroy(){if(session){session.Changed-=Refresh;if(session.health)session.health.Changed-=Refresh;}}
        public void Refresh(){float h=session.health.Current;healthFill.fillAmount=h/session.health.tuning.vehicleHealth;healthText.text=Mathf.CeilToInt(h)+" / "+Mathf.CeilToInt(session.health.tuning.vehicleHealth);scrapText.text=session.Scrap.ToString("000");killsText.text=session.Kills.ToString("000");overlay.SetActive(!session.Playing);resume.gameObject.SetActive(session.State==SessionState.Paused);overlayTitle.text=session.State==SessionState.Defeated?"WRECKED":"PAUSED";overlayScore.text="SCRAP  "+session.Scrap+"     /     RAIDERS  "+session.Kills;recover.interactable=session.Playing;}
        void Update(){frameTime=Mathf.Lerp(frameTime,Time.unscaledDeltaTime,.03f);if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.2f;speedText.text=(session.vehicle.ForwardSpeed<-.25f?"R ":"")+Mathf.RoundToInt(session.vehicle.Speed*3.6f).ToString("00")+" <size=20>KM/H</size>";if(debugText.gameObject.activeSelf)debugText.text="WHEELS "+session.vehicle.GroundedWheels+"/4  •  "+session.spawns.ActiveCount+" HOSTILES\nTARGET "+(session.turret.Target?session.turret.Target.name:"none")+"\n"+(frameTime*1000).ToString("F1")+" ms  •  SLIP "+session.vehicle.LargestSlip.ToString("F2");}
        public void ToggleDebug(){debugText.gameObject.SetActive(!debugText.gameObject.activeSelf);}
    }
}
