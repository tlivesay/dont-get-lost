﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class fog_distances
{
    public const float EXTEREMELY_CLOSE = 1f;
    public const float VERY_CLOSE = 3f;
    public const float CLOSE = 8f;
    public const float MEDIUM = 20f;
    public const float FAR = 50f;
    public const float VERY_FAR = 100f;
    public const float OFF = 5000f;
}

public class lighting : MonoBehaviour
{
    public Light sun;

    public static Color sky_color
    {
        get
        {
            if (player.current == null)
                return Color.black;
            return player.current.sky_color;
        }

        set
        {
            if (player.current == null)
                return;
            player.current.sky_color = value;
        }
    }

    public static Color sky_color_daytime;
    public static float fog_distance;

    void Start()
    {
        // Allow static access
        manager = this;

        // Set the initial sun position + orientation
        sun.transform.position = Vector3.zero;
        sun.transform.LookAt(new Vector3(1, -2, 1));
    }

    void Update()
    {
        float t = time_manager.get_time();
        float b = brightness_from_time(t);
        sun.enabled = b != 0f;
        sun.color = new Color(b, b, b);

        /*
        // Sun moves in sky - looks kinda fast/does wierd things to the shadows
        sun.transform.forward = new Vector3(
            Mathf.Cos(Mathf.PI * t),
            -Mathf.Sin(Mathf.PI * t) - 0.5f,
            0
        );
        */

        UnityEngine.Rendering.HighDefinition.GradientSky sky;
        if (options_menu.global_volume.profile.TryGet(out sky))
        {
            sky.multiplier.value = b * 0.2f + 0.1f;
            options_menu.global_volume.profile.isDirty = true;
        }

        Color target_sky_color = sky_color_daytime;
        target_sky_color.r *= b;
        target_sky_color.g *= b;
        target_sky_color.b *= b;
        sky_color = Color.Lerp(sky_color, target_sky_color, Time.deltaTime * 5f);


        if (options_menu.get_bool("fog"))
        {
            UnityEngine.Rendering.HighDefinition.Fog fog;
            if (options_menu.global_volume.profile.TryGet(out fog))
            {
                fog.color.value = sky_color;
                fog.albedo.value = sky_color;
                fog.meanFreePath.value = fog_distance;
            }
        }
    }

    float brightness_from_time(float time)
    {
        const float DAWN_FRACTION = 0.1f;
        const float DUSK_FRACTION = 0.1f;

        // Dawn (end of night)
        if (time > 2f - DAWN_FRACTION)
            return 1f - (2f - time) / DAWN_FRACTION;

        // Night
        if (time > 1f)
            return 0f;

        // Dusk (end of day)
        if (time > 1f - DUSK_FRACTION)
            return (1f - time) / DUSK_FRACTION;

        // Day
        return 1f;
    }

    //##################//
    // STATIC INTERFACE //
    //##################//

    static lighting manager;
}
