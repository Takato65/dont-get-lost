﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class water_reflections : MonoBehaviour
{
    UnityEngine.Rendering.HighDefinition.PlanarReflectionProbe probe;
    Renderer water_renderer;

    /// <summary> The water quad is centred this distance from the player, so that
    /// it appears behind transparent objects that are nearer. </summary>
    const float WATER_CENTRE_OFFSET = 64f;

    void Start()
    {
        // Create the water reflection probe
        probe = Resources.Load
            <UnityEngine.Rendering.HighDefinition.PlanarReflectionProbe>
            ("misc/water_reflection_probe").inst();
        probe.transform.position = transform.position + transform.forward * 16f;
        probe.transform.rotation = transform.rotation;
        probe.transform.SetParent(transform);
        probe.influenceVolume.shape = UnityEngine.Rendering.HighDefinition.InfluenceShape.Sphere;
        probe.influenceVolume.sphereRadius = 64f;
        probe.influenceVolume.sphereBlendDistance = 32f;

        // Create the water
        var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(water.GetComponent<Collider>());
        water.transform.SetParent(transform);

        water.transform.localRotation = Quaternion.identity;
        water.transform.localPosition = new Vector3(0, 0, WATER_CENTRE_OFFSET);
        water.transform.forward = -Vector3.up;
        water_renderer = water.gameObject.GetComponent<MeshRenderer>();

        water_renderer.material = Resources.Load<Material>("materials/standard_shader/water_reflective");
        water_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Needs to be fiddled with on startup to work for some reason
        fiddle_count = 2;
        last_fiddled = player.current.transform.position;
    }

    int fiddle_count;
    Vector3 last_fiddled;

    void Update()
    {
        // Workaround to stop the reflections from just randomly disapearing
        // if the player moves too far.
        if ((last_fiddled - player.current.transform.position).magnitude >
             probe.influenceVolume.sphereRadius)
        {
            fiddle_count += 2;
            last_fiddled = player.current.transform.position;
        }

        if (fiddle_count > 0)
        {
            --fiddle_count;
            reflections_enabled = !reflections_enabled;
        }

        bool should_reflect = reflections_enabled && !player.current.map_open;

        if (probe.enabled != should_reflect)
        {
            probe.enabled = should_reflect;
            water_renderer.material = Resources.Load<Material>(
                should_reflect ?
                "materials/standard_shader/water_reflective" :
                "materials/standard_shader/water_normal"
                );
        }

        // Ensure probe stays at water level
        Vector3 pos = transform.position;
        pos.y = world.SEA_LEVEL;
        transform.position = pos;

        water_renderer.transform.localScale = Vector3.one *
            (water_range * 2 + WATER_CENTRE_OFFSET);
    }

    public static float water_range = 256;
    public static bool reflections_enabled = true;
}