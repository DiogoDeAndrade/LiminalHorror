using OkapiKit;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TerrorManager : MonoBehaviour
{
    [SerializeField] private FPSController player;
    [SerializeField] private CanvasGroup   gameOver;
    [SerializeField] private CanvasGroup   toBeContinued;
    [SerializeField] private AudioClip     gameOverScream;

    WFCTilemap          tilemap;
    CharacterController charCtrl;
    Coroutine           fadeCR;

    public bool isFading => (fadeCR != null) || (gameOver.alpha > 0.0f) || (toBeContinued.alpha > 0.0f);

    void Start()
    {
        tilemap = FindAnyObjectByType<WFCTilemap>();
        charCtrl = player.GetComponent<CharacterController>();

        gameOver.alpha = 0.0f;
        toBeContinued.alpha = 0.0f;

        StartCoroutine(StartCR());
    }

    IEnumerator StartCR()
    {
        tilemap.GenerateMap(() =>
        {
            // Clear the area in the middle
            var worldTilePos = tilemap.WorldToTilePos(player.transform.position);
            for (int z = -3; z <= 3; z++)
            {
                for (int x = -3; x <= 3; x++)
                {
                    tilemap.ObserveTile(new Vector3Int(worldTilePos.x + x, worldTilePos.y, worldTilePos.z + z), (byte)(((x == 0) && (z == 0)) ? (2) : (1)), 0);
                }
            }
        });

        yield return new WaitForSeconds(1.0f);
        charCtrl.enabled = true;
        player.enabled = true;
    }

    public void GameOver()
    {
        if (fadeCR != null) return;
        fadeCR = StartCoroutine(FadeEndCR(gameOverScream, gameOver, 0));
    }
    public void ToBeContinued()
    {
        if (fadeCR != null) return;
        fadeCR = StartCoroutine(FadeEndCR(null, toBeContinued, -1));
    }

    IEnumerator FadeEndCR(AudioClip sound, CanvasGroup canvasGroup, int nextScene)
    {
        charCtrl.enabled = false;
        player.enabled = false;

        var terrorEvents = GetComponents<TerrorEvent>();
        foreach (var te in terrorEvents)
        {
            te.enabled = false;
        }

        if (sound) SoundManager.PlaySound(sound);

        while (canvasGroup.alpha < 1.0f)
        {
            canvasGroup.alpha = Mathf.Clamp01(canvasGroup.alpha + Time.deltaTime * 4.0f);
            yield return null;
        }

        while (true)
        {
            if (!Input.anyKey) break;
            yield return null;
        }

        while (true)
        {
            if (Input.anyKeyDown) break;
            yield return null;
        }

        if (nextScene >= 0) SceneManager.LoadScene(nextScene);
        else Application.Quit();
    }
}
