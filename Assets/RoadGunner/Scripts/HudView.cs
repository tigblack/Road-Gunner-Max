using UnityEngine;
using UnityEngine.UI;
namespace RoadGunner
{
    public sealed class HudView : MonoBehaviour
    {
        public GameSession session;
        public Image healthFill;
        public Text healthText,scrapText,killsText,speedText,overlayTitle,overlayScore,debugText;
        public Text statsText,upgradeFeedbackText;
        public GameObject overlay;
        public Button pause,resume,restart,recover;
        float nextRefresh,frameTime,feedbackUntil;
        ArenaRunStats subscribedStats;
        void Start() {
            pause.onClick.AddListener(session.TogglePause);resume.onClick.AddListener(session.TogglePause);
            restart.onClick.AddListener(session.Restart);recover.onClick.AddListener(session.Recover);
            session.Changed+=Refresh;session.health.Changed+=Refresh;
            subscribedStats=session.runStats;
            if(subscribedStats){subscribedStats.Changed+=Refresh;subscribedStats.UpgradeCollected+=ShowUpgrade;}
            if(upgradeFeedbackText)upgradeFeedbackText.gameObject.SetActive(false);
            Refresh();
        }
        void OnDestroy(){
            if(session){session.Changed-=Refresh;if(session.health)session.health.Changed-=Refresh;}
            if(subscribedStats){subscribedStats.Changed-=Refresh;subscribedStats.UpgradeCollected-=ShowUpgrade;}
        }
        public void Refresh(){
            float h=session.health.Current,maximum=session.health.Maximum;
            healthFill.fillAmount=h/maximum;healthText.text=Mathf.CeilToInt(h)+" / "+Mathf.CeilToInt(maximum);
            scrapText.text=session.Scrap.ToString("000");killsText.text=session.Kills.ToString("000");
            if(statsText){var stats=session.runStats;statsText.text="ATK "+(stats?stats.AttackLevel:1)+"   HP "+(stats?stats.HealthLevel:1);}
            overlay.SetActive(!session.Playing);resume.gameObject.SetActive(session.State==SessionState.Paused);
            overlayTitle.text=session.State==SessionState.Completed?"ARENA CLEAR":session.State==SessionState.Defeated?"WRECKED":"PAUSED";
            overlayScore.text="SCRAP  "+session.Scrap+"     /     ENEMIES  "+session.Kills;
            recover.interactable=session.Playing;pause.interactable=session.State==SessionState.Playing || session.State==SessionState.Paused;
            if(session.Playing && session.runStats && session.runStats.AttackLevel==1 && session.runStats.HealthLevel==1 && upgradeFeedbackText)
                upgradeFeedbackText.gameObject.SetActive(false);
        }
        void ShowUpgrade(ArenaUpgradeKind kind){
            if(!upgradeFeedbackText)return;
            upgradeFeedbackText.text=kind==ArenaUpgradeKind.Attack?"ATTACK +1":"HEALTH +1";
            upgradeFeedbackText.color=kind==ArenaUpgradeKind.Attack?new Color(1,.8f,.27f):new Color(.4f,1,.55f);
            feedbackUntil=Time.unscaledTime+1.8f;upgradeFeedbackText.gameObject.SetActive(true);
        }
        void Update(){
            if(upgradeFeedbackText && upgradeFeedbackText.gameObject.activeSelf && Time.unscaledTime>=feedbackUntil)upgradeFeedbackText.gameObject.SetActive(false);
            frameTime=Mathf.Lerp(frameTime,Time.unscaledDeltaTime,.03f);if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.2f;
            speedText.text=(session.vehicle.ForwardSpeed<-.25f?"R ":"")+Mathf.RoundToInt(session.vehicle.Speed*3.6f).ToString("00")+" <size=20>KM/H</size>";
            if(debugText.gameObject.activeSelf)debugText.text="WHEELS "+session.vehicle.GroundedWheels+"/4  •  "+session.spawns.ActiveCount+" HOSTILES\nTARGET "+(session.turret.Target?session.turret.Target.name:"none")+"\n"+(frameTime*1000).ToString("F1")+" ms  •  SLIP "+session.vehicle.LargestSlip.ToString("F2");
        }
        public void ToggleDebug(){debugText.gameObject.SetActive(!debugText.gameObject.activeSelf);}
    }
}
