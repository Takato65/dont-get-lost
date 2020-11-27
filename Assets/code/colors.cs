﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Useful color definitions
public static class terrain_colors
{
    public static readonly Color snow = new Color(0.9f, 0.9f, 0.9f, 0);
    public static readonly Color rock = new Color(0.5f, 0.5f, 0.5f, 0);
    public static readonly Color grass = new Color(0.4f, 0.6f, 0.2f, 0);
    public static readonly Color sand = new Color(0.8f, 0.65f, 0.35f, 0);
    public static readonly Color sand_dune = new Color(0.88f, 0.67f, 0.33f, 0);
    public static readonly Color desert_rock = new Color(0.51f, 0.31f, 0.14f, 0);
    public static readonly Color dirt = new Color(0.6f, 0.45f, 0.27f, 0);
    public static readonly Color stone = new Color(0.6f, 0.6f, 0.6f, 0);
    public static readonly Color charred_earth = new Color(0, 0, 0, 0);
    public static readonly Color crystal_light = new Color(0.77f, 0.47f, 0.82f);
    public static readonly Color crystal_dark = new Color(0.62f, 0.14f, 0.76f);
    public static readonly Color jungle_moss = new Color(0.18f, 0.30f, 0.11f);
    public static readonly Color marshy_grass = new Color(0.3f, 0.5f, 0.1f, 0);
}

public static class sky_colors
{
    public static readonly Color light_blue = new Color(0.58f, 0.89f, 0.89f, 1f);
    public static readonly Color smoke_grey = new Color(0.2f, 0.2f, 0.2f, 1f);
    public static readonly Color crystal_purple = new Color(0.55f, 0.21f, 0.49f);
    public static readonly Color jungle_green = new Color(0.38f, 0.57f, 0.29f);
    public static readonly Color underground_darkness = new Color(0.2f, 0.2f, 0.2f);
}

public static class water_colors
{
    public static readonly Color cyan = new Color(0.5f, 1f, 0.95f);
    public static readonly Color swampy_green = new Color(0.41f, 0.51f, 0.10f);
}

public static class character_colors
{
    public static Color random_skin_color()
    {
        return Color.HSVToRGB(
            Random.Range(10f / 360f, 46f / 360f),
            utils.random_normal(0.4f, 0.2f),
            Random.Range(0.23f, 1f)
        );
    }
}