using System.Collections.Generic;
using UnityEngine;
using OkapiKit;

public class Level0_EventBlackout : TerrorObject
{
    [SerializeField] private float      radius = 20.0f;
    [SerializeField] private float      duration = 10.0f;
    [SerializeField] private float      maxDistance = 30.0f;
    [SerializeField] private Color      blackoutEnvColor = new Color(0.2f, 0.2f, 0.2f, 1.0f);
    [SerializeField] private AudioClip  soundFX;

    class ElemData
    {
        public WFCTile3d    tile;
        public Light        light;
        public AudioSource  audioSource;
        public Material     material;
        public float        lightIntensity;
        public Color        emissiveColor;
    }
    
    private ParticleSystem  smilerPS;
    private List<ElemData>  lights;
    private float           timer;
    private Color           originalEnvColor;
    private Vector3         playerLastPosition;
    private float           totalDistance;
    private float           psInitialRate;
    private float           psInitialRadius;

    protected override void Start()
    {
        base.Start();

        smilerPS = GetComponentInChildren<ParticleSystem>();

        lights = new();

        var tiles = FindObjectsByType<WFCTile3d>(FindObjectsSortMode.None);
        foreach (var tile in tiles)
        {
            if (Vector3.Distance(player.transform.position, tile.transform.position) < radius)
            {
                // Has a light
                Light light = tile.GetComponentInChildren<Light>();
                if (light)
                {
                    ElemData elem = new ElemData();
                    elem.light = light;
                    elem.lightIntensity = light.intensity;
                    elem.audioSource = light.GetComponentInChildren<AudioSource>();
                    elem.material = light.GetComponentInParent<MeshRenderer>().material;
                    elem.emissiveColor = elem.material.GetColor("_EmissionColor");
                    lights.Add(elem);
                }
            }
        }

        var lightList = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var light in lightList)
        {
            if (light.GetComponentInParent<WFCTile3d>() == null)
            {
                ElemData elem = new ElemData();
                elem.light = light;
                elem.lightIntensity = light.intensity;
                lights.Add(elem);
            }
        }

        timer = duration;

        originalEnvColor = RenderSettings.ambientLight;
        RenderSettings.ambientLight = blackoutEnvColor;

        if (soundFX) SoundManager.PlaySound(soundFX);

        foreach (var light in lights)
        {
            EnableLight(light, false);
        }
        
        if (smilerPS)
        {
            var emission = smilerPS.emission;
            psInitialRate = emission.rateOverTime.constant;
            var shape = smilerPS.shape;
            psInitialRadius = shape.radius;
        }

        playerLastPosition = player.transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        totalDistance += Vector3.Distance(player.transform.position, playerLastPosition);
        playerLastPosition = player.transform.position;

        if (smilerPS)
        {
            float t = Mathf.Clamp01(totalDistance / maxDistance);

            smilerPS.transform.position = player.transform.position;
            var emission = smilerPS.emission;
            emission.rateOverTime = Mathf.Lerp(psInitialRate, 30, t);
            var shape = smilerPS.shape;
            shape.radius = Mathf.Lerp(psInitialRadius, 1.0f, t);

            if (t == 1)
            {
                terrorManager.GameOver();
            }
        }

        timer -= Time.deltaTime;
        if (timer <= 0.0f)
        {
            if (soundFX) SoundManager.PlaySound(soundFX);
            RenderSettings.ambientLight = originalEnvColor;
            foreach (var light in lights)
            {
                EnableLight(light, true);
            }
            Destroy(gameObject);
        }
    }

    void EnableLight(ElemData elem, bool enable)
    {
        // _EMISSION, _EmissionColor
        if (enable)
        {
            if (elem.light != null)
            {
                elem.light.intensity = elem.lightIntensity;
            }
            if (elem.material != null)
            {
                elem.material.SetColor("_EmissionColor", elem.emissiveColor);
                elem.material.EnableKeyword("_EMISSION");
            }
            if (elem.audioSource != null)
            {
                elem.audioSource.volume = 1.0f;
            }
        }
        else
        {
            if (elem.light)
            {
                elem.light.intensity = 0.0f;
            }
            if (elem.material != null)
            {
                elem.material.SetColor("_EmissionColor", Color.black);
                elem.material.DisableKeyword("_EMISSION");
            }
            if (elem.audioSource != null)
            {
                elem.audioSource.volume = 0.0f;
            }
        }
    }
}
