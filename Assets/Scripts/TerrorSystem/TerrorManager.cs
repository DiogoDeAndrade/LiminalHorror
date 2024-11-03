using OkapiKit;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TerrorManager : MonoBehaviour
{
    [SerializeField] private FPSController player;
    [SerializeField] private CanvasGroup   gameOver;
    [SerializeField] private AudioClip     gameOverScream;

    WFCTilemap          tilemap;
    CharacterController charCtrl;
    Coroutine           gameoverCR;

    void Start()
    {
        tilemap = FindAnyObjectByType<WFCTilemap>();
        charCtrl = player.GetComponent<CharacterController>();

        gameOver.alpha = 0.0f;

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
        if (gameoverCR != null) return;
        gameoverCR = StartCoroutine(GameOverCR());
    }

    IEnumerator GameOverCR()
    {
        charCtrl.enabled = false;
        player.enabled = false;

        var terrorEvents = GetComponents<TerrorEvent>();
        foreach (var te in terrorEvents)
        {
            te.enabled = false;
        }

        SoundManager.PlaySound(gameOverScream);

        while (gameOver.alpha < 1.0f)
        {
            gameOver.alpha = Mathf.Clamp01(gameOver.alpha + Time.deltaTime * 4.0f);
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

        SceneManager.LoadScene(0);
    }
}
