﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class settler_field_spot : networked, IPlayerInteractable
{
    /// <summary> Only modify networked variables if this much progress has been made. </summary>
    const float GROWTH_RESOLUTION = 0.05f;

    public bool grown => networked_progress.value >= 1f;
    public GameObject to_grow;
    public product[] products => GetComponents<product>();
    public float growth_time = 30f;
    public float min_scale = 0.2f;

    /// <summary> Local progress in [0, GROWTH_RESOLUTION], will be added to 
    /// networked_progress if set to a value above GROWTH_RESOLUTION. </summary>
    float local_progress
    {
        get => _local_progress;
        set
        {
            _local_progress = value;
            if (_local_progress > GROWTH_RESOLUTION)
            {
                networked_progress.value += _local_progress;
                _local_progress = 0;
            }
        }
    }
    float _local_progress;

    /// <summary> How far through the growth process are we. </summary>
    networked_variables.net_float networked_progress;
    float progress_scale => networked_progress.value * (1f - min_scale) + min_scale;

    public override void on_init_network_variables()
    {
        networked_progress = new networked_variables.net_float(
            resolution: GROWTH_RESOLUTION * 0.99f,
            min_value: 0f,
            max_value: 1f);

        networked_progress.on_change = () =>
        {
            // If progress has been reduced, that means we've been harvested
            if (progress_scale < grown_object.transform.localScale.x)
            {
                var field = GetComponentInParent<settler_field>();
                if (field != null)
                    foreach (var p in products)
                        p.create_in_node(field.output, true);
            }

            grown_object.transform.localScale = Vector3.one * progress_scale;
        };
    }

    GameObject grown_object
    {
        get
        {
            if (_grown_object == null)
            {
                // Create the grown object
                _grown_object = to_grow.inst();
                _grown_object.transform.SetParent(transform);
                _grown_object.transform.localPosition = Vector3.zero;
                _grown_object.transform.localRotation = Quaternion.identity;
            }
            return _grown_object;
        }
    }
    GameObject _grown_object;

    private void Update()
    {
        if (!has_authority) return;
        local_progress += Time.deltaTime / growth_time;
    }

    public void harvest()
    {
        local_progress = 0f;
        networked_progress.value = 0f;
    }

    public void tend()
    {
        local_progress += 10f / growth_time;
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.Lerp(Color.red, Color.green, networked_progress.value);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0.05f, 1f));
    }

    //#####################//
    // IPlayerInteractable //
    //#####################//

    public player_interaction[] player_interactions()
    {
        return new player_interaction[]
        {
            new player_inspectable(transform)
            {
                text = () => Mathf.Round(networked_progress.value * 100f) + "% grown"
            }
        };
    }
}