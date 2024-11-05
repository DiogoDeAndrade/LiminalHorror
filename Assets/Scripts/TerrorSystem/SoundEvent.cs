using OkapiKit;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class SoundEvent : TerrorObject
{
    [SerializeField] private AudioSource       audioSource;
    [SerializeField] private AudioClipProbList sounds;

    protected override void Start()
    {
        base.Start();

        StartCoroutine(PlaySoundCR());
    }

    IEnumerator PlaySoundCR()
    {
        // Place sound at random
        float angle = Random.Range(0.0f, 360.0f) * Mathf.Deg2Rad;
        Vector3 pos = new Vector3(2.0f * Mathf.Sin(angle), 1.0f, 2.0f * Mathf.Cos(angle));

        audioSource.transform.position = player.transform.position + pos;

        var sndClip = sounds.Get();

        audioSource.clip = sndClip;
        audioSource.volume = 1.0f;
        audioSource.pitch = Random.Range(0.7f, 1.4f);
        audioSource.Play();

        yield return new WaitForSound(audioSource);

        Destroy(gameObject);
    }
}
