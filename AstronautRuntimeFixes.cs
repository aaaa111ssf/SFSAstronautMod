using SFS.World;
using UnityEngine;

public sealed class EVAControlRecovery : MonoBehaviour
{
    private const float GracePeriodSeconds = 0.35f;
    private Astronaut_EVA astronaut;
    private float selectedSince = -1f;

    public static void Attach(Astronaut_EVA eva)
    {
        if (eva == null) return;
        if (eva.GetComponent<EVAControlRecovery>() == null)
            eva.gameObject.AddComponent<EVAControlRecovery>();
    }

    private void Awake()
    {
        astronaut = GetComponent<Astronaut_EVA>();
    }

    private void Update()
    {
        if (astronaut == null || PlayerController.main == null) return;
        if (PlayerController.main.player.Value != astronaut)
        {
            selectedSince = -1f;
            return;
        }

        if (selectedSince < 0f) selectedSince = Time.realtimeSinceStartup;
        if (astronaut.ragdoll || astronaut.astronaut == null || !astronaut.astronaut.alive) return;
        if (astronaut.hasControl.Value || Time.realtimeSinceStartup - selectedSince < GracePeriodSeconds) return;

        astronaut.hasControl.Value = true;
    }
}
