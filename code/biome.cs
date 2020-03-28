﻿using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// A biome represents the largest singly-generated area of the map
// and is used to generate quantities that exist on the largest length
// scales, such as the terrain.
public abstract class biome : MonoBehaviour
{
    // My neighbouring biomes, if they don't exist already
    // we will generate them.
    biome[,] _neihbours = new biome[3, 3];
    biome get_neighbour(int dx, int dz, bool generate_if_needed = true)
    {
        if (dx == 0 && dz == 0) return this;
        int i = dx + 1;
        int j = dz + 1;
        if (_neihbours[i, j] == null)
        {
            // This catches the case where the neighbouring
            // biome has been destroyed, but not set to null proper
            _neihbours[i, j] = null;

            // Attempt to find already existing biome, otherwise generate one
            var go = GameObject.Find("biome_" + (x + dx) + "_" + (z + dz));
            if (go != null) _neihbours[i, j] = go.GetComponent<biome>();
            else if (generate_if_needed)
                _neihbours[i, j] = load_or_generate(x + dx, z + dz);
        }

        return _neihbours[i, j];
    }

    // Extend the chunk grid indicies to include neighbouring biomes
    chunk extended_chunk_grid(int i, int j, bool generate_if_needed = true)
    {
        // Convert i, j to i, j coordinates in the (dx, dz)^th neighbour
        int dx = 0;
        if (i >= CHUNKS_PER_SIDE) { dx = 1; i -= CHUNKS_PER_SIDE; }
        else if (i < 0) { dx = -1; i += CHUNKS_PER_SIDE; }

        int dz = 0;
        if (j >= CHUNKS_PER_SIDE) { dz = 1; j -= CHUNKS_PER_SIDE; }
        else if (j < 0) { dz = -1; j += CHUNKS_PER_SIDE; }

        var b = get_neighbour(dx, dz, generate_if_needed);
        if (b == null) return null;
        return b.chunk_grid[i, j];
    }

    // Update the chunk neighbours in this biome, including
    // neighbours from neighbouring biomes (if they exist)
    public void update_chunk_neighbours(bool also_neighboring_biomes = true)
    {
        if (also_neighboring_biomes)
            for (int dx = -1; dx < 2; ++dx)
                for (int dz = -1; dz < 2; ++dz)
                    get_neighbour(dx, dz, false)?.update_chunk_neighbours(false);

        else
            for (int i = 0; i < CHUNKS_PER_SIDE; ++i)
                for (int j = 0; j < CHUNKS_PER_SIDE; ++j)
                {
                    chunk_grid[i, j]?.terrain?.SetNeighbors(
                        extended_chunk_grid(i - 1, j, false)?.terrain,
                        extended_chunk_grid(i, j + 1, false)?.terrain,
                        extended_chunk_grid(i + 1, j, false)?.terrain,
                        extended_chunk_grid(i, j - 1, false)?.terrain
                    );
                }
    }

    void get_blend_amounts(
        Vector3 position, // The point at which to evaluate the blend amounts 
        out float xamt,   // Abs(xamt) = amount to blend, Sign(xamt) = x direction to blend
        out float zamt    // Abs(zamt) = amount to blend, Sign(zamt) = z direction to blend
        )
    {
        // Set the blend distance to slightly less than
        // a chunk so we only need blend the outermost chunks
        // in a biome
        const int BLEND_DISTANCE = chunk.SIZE - 1;

        int i = Mathf.FloorToInt(position.x) - x * SIZE;
        int j = Mathf.FloorToInt(position.z) - z * SIZE;

        xamt = zamt = 0;

        // Create a linearly increasing blend at the edge of the biome
        if (i <= BLEND_DISTANCE) xamt = i / (float)BLEND_DISTANCE - 1f;
        else if (i >= SIZE - 1 - BLEND_DISTANCE) xamt = 1f - (SIZE - 1 - i) / (float)BLEND_DISTANCE;

        if (j <= BLEND_DISTANCE) zamt = j / (float)BLEND_DISTANCE - 1f;
        else if (j >= SIZE - 1 - BLEND_DISTANCE) zamt = 1f - (SIZE - 1 - j) / (float)BLEND_DISTANCE;

        // Smooth the linear blend out so it has zero gradient at the very edge of the biome
        if (xamt > 0) xamt = procmath.maps.smooth_max_cos(xamt);
        else if (xamt < 0) xamt = -procmath.maps.smooth_max_cos(-xamt);

        if (zamt > 0) zamt = procmath.maps.smooth_max_cos(zamt);
        else if (zamt < 0) zamt = -procmath.maps.smooth_max_cos(-zamt);
    }

    public point blended_point(Vector3 world_position)
    {
        // Get the x and z amounts to blend
        float xamt, zamt;
        get_blend_amounts(world_position, out xamt, out zamt);

        // We blend at most 4 points:
        //   one from this biome (guaranteed)
        //   one from the neighbour in the +/- x direction
        //   one from the neighbour in the +/- z direction
        //   one from the diaonal neighbour between the above two
        var points = new point[4];
        var weights = new float[4];

        points[0] = clamped_grid(world_position);
        weights[0] = 1.0f;

        // Blend in the neihbour in the +/- x direction
        float abs_x = Mathf.Abs(xamt);
        if (abs_x > 0)
        {
            points[1] = get_neighbour(xamt > 0 ? 1 : -1, 0).clamped_grid(world_position);
            weights[1] = abs_x;
        }

        // Blend in the neihbour in the +/- z direction
        float abs_z = Mathf.Abs(zamt);
        if (abs_z > 0)
        {
            points[2] = get_neighbour(0, zamt > 0 ? 1 : -1).clamped_grid(world_position);
            weights[2] = abs_z;
        }

        // Blend the neihbour in the diagonal direction
        float damt = Mathf.Min(abs_x, abs_z);
        if (damt > 0)
        {
            points[3] = get_neighbour(xamt > 0 ? 1 : -1, zamt > 0 ? 1 : -1).clamped_grid(world_position);
            weights[3] = damt;
        }

        return point.average(points, weights);
    }

    // The grid of points defining the biome
    protected point[,] grid = new point[SIZE, SIZE];

    // Get a particular point in the biome grid in world
    // coordinates. Clamps the biome point values
    // outside the range of the biome.
    point clamped_grid(Vector3 world_position)
    {
        int i = Mathf.FloorToInt(world_position.x) - SIZE * x;
        int j = Mathf.FloorToInt(world_position.z) - SIZE * z;
        i = Mathf.Clamp(i, 0, SIZE - 1);
        j = Mathf.Clamp(j, 0, SIZE - 1);
        return grid[i, j];
    }

    // The size of a biome in chunks per side
    public const int CHUNKS_PER_SIDE = 4;
    public const int SIZE = CHUNKS_PER_SIDE * chunk.SIZE;

    // The grid of chunks within the biome
    chunk[,] chunk_grid = new chunk[CHUNKS_PER_SIDE, CHUNKS_PER_SIDE];

    // Returns true if this biome contains a chunk which
    // is active (i.e within render range)
    public bool contains_enabled_chunk()
    {
        for (int i = 0; i < CHUNKS_PER_SIDE; ++i)
            for (int j = 0; j < CHUNKS_PER_SIDE; ++j)
                if (chunk_grid[i, j].enabled)
                    return true;
        return false;
    }

    // Get the biome coords at a given location
    public static int[] coords(Vector3 location)
    {
        return new int[]
        {
            Mathf.FloorToInt(location.x / SIZE),
            Mathf.FloorToInt(location.z / SIZE)
        };
    }

    public Vector3 centre { get { return transform.position + new Vector3(1, 0, 1) * SIZE / 2; } }

    public chunk chunk_at(Vector3 world_position)
    {
        // Transform world position into biome local position
        int xib = (int)(world_position.x - x * SIZE);
        if (xib < 0) return null;
        if (xib >= SIZE) return null;

        int zib = (int)(world_position.z - z * SIZE);
        if (zib < 0) return null;
        if (zib >= SIZE) return null;

        // Get the chunk at that coordinate
        int cx = xib / chunk.SIZE;
        int cz = zib / chunk.SIZE;
        return chunk_grid[cx, cz];
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(
            transform.position + new Vector3(1, 0, 1) * SIZE / 2f,
            new Vector3(SIZE, 0.01f, SIZE));
    }

    public int x { get; private set; }
    public int z { get; private set; }

    // Coordinate transforms 
    protected float grid_to_world_x(int i) { return (x - 0.5f) * SIZE + i; }
    protected float grid_to_world_z(int j) { return (z - 0.5f) * SIZE + j; }

    protected abstract void generate_grid();

    public static string filename(int x, int z) { return world.save_folder() + "/biome_" + x + "_" + z; }

    // Loads a biome if possible, otherwise generates one
    public static biome load_or_generate(int x, int z)
    {
        if (System.IO.File.Exists(filename(x, z)))
        {
            var loaded = create<loaded_biome>(x, z);
            loaded.load();
            return loaded;
        }

        // Select a random biome
        int i = Random.Range(0, biome_creators.Count);
        var generated = (biome)biome_creators[i].Invoke(null, new object[] { x, z });

        // Generate the biome
        var sw = System.Diagnostics.Stopwatch.StartNew();
        generated.generate_grid();
        utils.log("Generated biome " + x + ", " + z +
                  " (" + generated.GetType().Name + ") in " +
                  sw.ElapsedMilliseconds + " ms", "generation");
        return generated;
    }

    // Creation methods for all enabled biome types
    public static string biome_override = "";
    static List<MethodInfo> _biome_creators;
    static List<MethodInfo> biome_creators
    {
        get
        {
            if (_biome_creators == null)
            {
                // Find the biome types
                _biome_creators = new List<MethodInfo>();
                var asem = Assembly.GetAssembly(typeof(biome));
                var types = asem.GetTypes();

                foreach (var t in types)
                {
                    // Check if this type is a valid biome
                    if (!t.IsSubclassOf(typeof(biome))) continue;
                    if (t.IsAbstract) continue;

                    // Get the create method
                    var method = typeof(biome).GetMethod("create", BindingFlags.NonPublic | BindingFlags.Static);
                    var create_method = method.MakeGenericMethod(t);

                    if (t.Name == biome_override)
                    {
                        // Enforce the biome override
                        _biome_creators = new List<MethodInfo> { create_method };
                        break;
                    }

                    // Get biome info, if it exists
                    var bi = (biome_info)t.GetCustomAttribute(typeof(biome_info));
                    if (bi != null)
                    {
                        if (!bi.generation_enabled)
                            continue; // Skip allowing this biome
                    }

                    _biome_creators.Add(create_method);
                }
            }
            return _biome_creators;
        }
    }

    // Create a blank biome of the given type, ready for generation or loading
    static T create<T>(int x, int z) where T : biome
    {
        var b = new GameObject("biome_" + x + "_" + z).AddComponent<T>();
        b.transform.position = new Vector3(x, 0, z) * SIZE;

        b.x = x;
        b.z = z;

        // Initialize the chunk grid
        for (int i = 0; i < CHUNKS_PER_SIDE; ++i)
            for (int j = 0; j < CHUNKS_PER_SIDE; ++j)
            {
                // Get the chunk coordinates from my
                // coordinates and the chunk grid coordinates
                int cx = x * CHUNKS_PER_SIDE + i;
                int cz = z * CHUNKS_PER_SIDE + j;
                b.chunk_grid[i, j] = chunk.create(cx, cz);
                b.chunk_grid[i, j].transform.SetParent(b.transform);
            }

        return b;
    }

    private void Update()
    {
        // Load neighbours if they are in range
        for (int dx = -1; dx < 2; ++dx)
            for (int dz = -1; dz < 2; ++dz)
                if (in_range(x + dx, z + dz))
                    get_neighbour(dx, dz, true);

        // Offload to disk if possible
        if (can_offload()) offload_to_disk();
    }

    // Check if this biome is no longer required in-game
    bool can_offload()
    {
        // If biome is in range, it's definately needed
        if (in_range(x, z)) return false;

        // If there is an enabled chunk in an adjacent
        // biome, we could still be needed for biome blending
        for (int dx = -1; dx < 2; ++dx)
            for (int dz = -1; dz < 2; ++dz)
                if (get_neighbour(dx, dz, false)?.contains_enabled_chunk() ?? false)
                    return false;

        // Definately not needed
        return true;
    }

    // Check if this biome is within render range
    // (essentially testing if the render range circle 
    //  intersects the biome square)
    static bool in_range(int x, int z)
    {
        Vector2 player_xz = new Vector2(
            player.current.transform.position.x,
            player.current.transform.position.z
        );

        Vector2 this_xz = new Vector2(
            SIZE * (x + 0.5f),
            SIZE * (z + 0.5f)
        );

        return utils.circle_intersects_square(player_xz, game.render_range, this_xz, SIZE, SIZE);
    }

    public void offload_to_disk()
    {
        // Save the biome to file
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var stream = new System.IO.FileStream(filename(x, z), System.IO.FileMode.Create))
        {
            // Write the points to the file
            for (int i = 0; i < SIZE; ++i)
                for (int j = 0; j < SIZE; ++j)
                {
                    var bytes = grid[i, j].serialize();
                    stream.Write(bytes, 0, bytes.Length);
                }

            // Write all the world objects to the file
            for (int i = 0; i < SIZE; ++i)
                for (int j = 0; j < SIZE; ++j)
                {
                    // No world object here
                    if (grid[i, j].world_object_gen == null)
                        continue;

                    // Store the i, j coordinates of this world_object_generator
                    stream.Write(System.BitConverter.GetBytes(i), 0, sizeof(int));
                    stream.Write(System.BitConverter.GetBytes(j), 0, sizeof(int));

                    var wos = grid[i, j].world_object_gen;

                    // World object still hasn't loaded => simply save it again
                    if (wos.to_load != null)
                    {
                        stream.Write(wos.to_load, 0, wos.to_load.Length);
                    }

                    else if (wos.generated)
                    {
                        // World object has been generated, serialize it
                        var bytes = wos.generated.serialize();
                        stream.Write(bytes, 0, bytes.Length);
                    }

                    else if (wos.to_generate != null)
                    {
                        // World object is scheduled for generation
                        // serialize the "generation required" version
                        var bytes = wos.to_generate.serialize_generate_required();
                        stream.Write(bytes, 0, bytes.Length);
                    }

                    else Debug.LogError("Don't know how to save this world object status!");
                }
        }

        // Remove this biome from the world
        Destroy(gameObject);

        utils.log("Saved biome " + x + ", " + z + " in " + sw.ElapsedMilliseconds + " ms", "io");
    }

    bool load()
    {
        // Read the biome from file
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!System.IO.File.Exists(filename(x, z))) return false;
        using (var stream = new System.IO.FileStream(filename(x, z), System.IO.FileMode.Open, System.IO.FileAccess.Read))
        {
            // Read the points from the file
            byte[] pt_bytes = new byte[new point().serialize().Length];
            for (int i = 0; i < SIZE; ++i)
                for (int j = 0; j < SIZE; ++j)
                {
                    stream.Read(pt_bytes, 0, pt_bytes.Length);
                    grid[i, j] = new point(pt_bytes);
                }

            // Read the world objects from the file
            byte[] int_bytes = new byte[sizeof(int) * 2];
            while (true)
            {
                // Load the position
                if (stream.Read(int_bytes, 0, int_bytes.Length) == 0) break;
                int i = System.BitConverter.ToInt32(int_bytes, 0);
                int j = System.BitConverter.ToInt32(int_bytes, sizeof(int));

                // Load the world object
                byte[] wo_bytes = new byte[world_object.serialize_length()];
                if (stream.Read(wo_bytes, 0, wo_bytes.Length) == 0) break;

                if (world_object.generation_required(wo_bytes))
                {
                    // This object was saved before generation, schedule generation
                    int id = System.BitConverter.ToInt32(wo_bytes, 0);
                    grid[i, j].world_object_gen = new world_object_generator(id);
                }
                else
                    // Load as normal
                    grid[i, j].world_object_gen = new world_object_generator(wo_bytes);
            }
        }
        utils.log("Loaded biome " + x + ", " + z + " in " + sw.ElapsedMilliseconds + " ms", "io");
        return true;
    }

    // Represetnts a world object in a biome
    public class world_object_generator
    {
        public float additional_scale_factor = 1.0f;

        public world_object gen_or_load(Vector3 terrain_normal, point point)
        {
            // Already generated
            if (generated != null)
            {
                // May have been already generated, but moved to
                // the inactive pile (if the chunk it was in was destroyed)
                if (!generated.gameObject.activeInHierarchy)
                    generated.gameObject.SetActive(true);

                return generated;
            }

            // Needs to be loaded from bytes
            if (to_load != null)
            {
                generated = world_object.deserialize(to_load);
                generated.on_load(terrain_normal, point);
                to_load = null;
                return generated;
            }

            // Needs to be generated from prefab
            generated = to_generate.inst();
            to_generate = null;
            generated.on_generation(terrain_normal, point);
            generated.transform.localScale *= additional_scale_factor;
            return generated;
        }

        // A world_object_generator can only be created 
        // with a prefab that needs generating, or a
        // byte array that needs loading.
        public world_object_generator(string name)
        {
            to_generate = world_object.look_up(name);
            if (to_generate == null)
                Debug.LogError("Tried to create a world object generator with a null world object!");
        }

        public world_object_generator(int id)
        {
            to_generate = world_object.look_up(id);
            if (to_generate == null)
                Debug.LogError("Tried to create a world object generator with a null world object!");
        }

        public world_object_generator(byte[] bytes) { to_load = bytes; }

        public world_object to_generate { get; private set; }
        public world_object generated { get; private set; }
        public byte[] to_load { get; private set; }
    }

    // A particular point in the biome has these
    // properties. They are either loaded from disk,
    // or generated.
    public class point
    {
        public float altitude;
        public Color terrain_color;
        public world_object_generator world_object_gen;

        // Serialise a point into bytes
        public byte[] serialize()
        {
            float[] floats = new float[]
            {
                altitude, terrain_color.r, terrain_color.g, terrain_color.b
            };

            int float_bytes = sizeof(float) * floats.Length;
            byte[] bytes = new byte[float_bytes];
            System.Buffer.BlockCopy(floats, 0, bytes, 0, float_bytes);
            return bytes;
        }

        public point() { }

        // Construct a point from it's serialization
        public point(byte[] bytes)
        {
            altitude = System.BitConverter.ToSingle(bytes, 0);
            terrain_color.r = System.BitConverter.ToSingle(bytes, sizeof(float));
            terrain_color.g = System.BitConverter.ToSingle(bytes, 2 * sizeof(float));
            terrain_color.b = System.BitConverter.ToSingle(bytes, 3 * sizeof(float));
            terrain_color.a = 0;
        }

        // Computea a weighted average of a list of points
        public static point average(point[] pts, float[] wts)
        {
            point ret = new point
            {
                altitude = 0,
                terrain_color = new Color(0, 0, 0, 0)
            };

            float total_weight = 0;
            foreach (var f in wts) total_weight += f;

            int max_i = 0;
            float max_w = 0;
            for (int i = 0; i < wts.Length; ++i)
            {
                float w = wts[i] / total_weight;
                var p = pts[i];
                if (p == null) continue;

                ret.altitude += p.altitude * w;
                ret.terrain_color.r += p.terrain_color.r * w;
                ret.terrain_color.g += p.terrain_color.g * w;
                ret.terrain_color.b += p.terrain_color.b * w;

                if (wts[i] > max_w)
                {
                    max_w = wts[i];
                    max_i = i;
                }
            }

            if (pts[max_i] != null)
                ret.world_object_gen = pts[max_i].world_object_gen;

            return ret;
        }

        public void apply_global_rules()
        {
            // Enforce beach color
            const float SAND_START = world.SEA_LEVEL + 1f;
            const float SAND_END = world.SEA_LEVEL + 2f;

            if (altitude < SAND_START)
                terrain_color = colors.sand;
            else if (altitude < SAND_END)
            {
                float s = 1 - procmath.maps.linear_turn_on(altitude, SAND_START, SAND_END);
                terrain_color = Color.Lerp(terrain_color, colors.sand, s);
            }
        }

        public string info()
        {
            return "Altitude: " + altitude + " Terrain color: " + terrain_color;
        }
    }
}

// Attribute info for biomes
public class biome_info : System.Attribute
{
    public bool generation_enabled { get; private set; }

    public biome_info(bool generation_enabled)
    {
        this.generation_enabled = generation_enabled;
    }
}

[biome_info(false)]
public class loaded_biome : biome
{
    protected override void generate_grid()
    {
        throw new System.Exception("Loaded biomes should not call generate()!");
    }
}

public class mangroves : biome
{
    public const float ISLAND_SIZE = 27.2f;
    public const float MANGROVE_START_ALT = world.SEA_LEVEL - 3;
    public const float MANGROVE_DECAY_ALT = 3f;
    public const float MANGROVE_PROB = 0.2f;

    protected override void generate_grid()
    {
        float xrand = Random.Range(0, 1f);
        float zrand = Random.Range(0, 1f);

        for (int i = 0; i < SIZE; ++i)
            for (int j = 0; j < SIZE; ++j)
            {
                var p = new point();

                p.altitude = ISLAND_SIZE * Mathf.PerlinNoise(
                    xrand + i / ISLAND_SIZE, zrand + j / ISLAND_SIZE);
                p.terrain_color = colors.grass;

                float man_amt = 0;
                if (p.altitude > MANGROVE_START_ALT)
                    man_amt = Mathf.Exp(
                        -(p.altitude - MANGROVE_START_ALT) / MANGROVE_DECAY_ALT);

                if (Random.Range(0, 1f) < man_amt * MANGROVE_PROB)
                    p.world_object_gen = new world_object_generator("mangroves");

                grid[i, j] = p;
            }
    }
}

public class ocean : biome
{
    const int MAX_ISLAND_ALT = 8; // The maximum altitude of an island above sea level
    const int ISLAND_PERIOD = 32; // The range over which an island extends on the seabed
    const int MIN_ISLANDS = 1;    // Min number of islands
    const int MAX_ISLANDS = 3;    // Max number of islands

    protected override void generate_grid()
    {
        // Generate the altitude
        float[,] alt = new float[SIZE, SIZE];

        // Start with some perlin noise
        float xrand = Random.Range(0, 1f);
        float zrand = Random.Range(0, 1f);
        for (int i = 0; i < SIZE; ++i)
            for (int j = 0; j < SIZE; ++j)
                alt[i, j] += 0.5f * world.SEA_LEVEL * Mathf.PerlinNoise(
                    xrand + x / 16f, zrand + z / 16f);

        // Add a bunch of guassians to create desert islands
        // (also reduce the amount of perlin noise far from the islands
        //  to create a smooth seabed)
        int islands = Random.Range(MIN_ISLANDS, MAX_ISLANDS);
        for (int n = 0; n < islands; ++n)
            procmath.float_2D_tools.apply_guassian(ref alt,
                Random.Range(ISLAND_PERIOD, SIZE - ISLAND_PERIOD),
                Random.Range(ISLAND_PERIOD, SIZE - ISLAND_PERIOD),
                ISLAND_PERIOD, (f, g) =>
                    f * (0.5f + 0.5f * g) + // Reduced perlin noise
                    g * (world.SEA_LEVEL / 2f + MAX_ISLAND_ALT) // Added guassian
                );

        // Generate the point grid
        for (int i = 0; i < SIZE; ++i)
            for (int j = 0; j < SIZE; ++j)
            {
                point p = new point
                {
                    terrain_color = colors.sand,
                    altitude = alt[i, j]
                };

                if (p.altitude > world.SEA_LEVEL)
                    if (Random.Range(0, 100) == 0)
                        p.world_object_gen = new world_object_generator("palm_tree");

                grid[i, j] = p;
            }
    }
}

public class mountains : biome
{
    const int MIN_MOUNTAIN_WIDTH = 64;
    const int MAX_MOUNTAIN_WIDTH = 128;
    const float MAX_MOUNTAIN_HEIGHT = 128f;
    const float MIN_MOUNTAIN_HEIGHT = 0f;
    const float SNOW_START = 80f;
    const float SNOW_END = 100f;
    const float ROCK_START = 50f;
    const float ROCK_END = 70f;
    const float MOUNTAIN_DENSITY = 0.0008f;

    protected override void generate_grid()
    {
        var alt = new float[SIZE, SIZE];

        const int MOUNTAIN_COUNT = (int)(MOUNTAIN_DENSITY * SIZE * SIZE);
        for (int n = 0; n < MOUNTAIN_COUNT; ++n)
        {
            int xm = Random.Range(0, SIZE);
            int zm = Random.Range(0, SIZE);
            int width = Random.Range(MIN_MOUNTAIN_WIDTH, MAX_MOUNTAIN_WIDTH);
            float rot = Random.Range(0, 360f);
            procmath.float_2D_tools.add_pyramid(ref alt, xm, zm, width, 1, rot);
        }

        procmath.float_2D_tools.rescale(ref alt, MIN_MOUNTAIN_HEIGHT, MAX_MOUNTAIN_HEIGHT);

        for (int i = 0; i < SIZE; ++i)
            for (int j = 0; j < SIZE; ++j)
            {
                float xf = grid_to_world_x(i);
                float zf = grid_to_world_z(j);

                point p = new point
                {
                    altitude = alt[i, j],
                    terrain_color = colors.grass
                };

                if (p.altitude > SNOW_START)
                {
                    float s = procmath.maps.linear_turn_on(p.altitude, SNOW_START, SNOW_END);
                    p.terrain_color = Color.Lerp(colors.rock, colors.snow, s);
                }
                else if (p.altitude > ROCK_START)
                {
                    float r = procmath.maps.linear_turn_on(p.altitude, ROCK_START, ROCK_END);
                    p.terrain_color = Color.Lerp(colors.grass, colors.rock, r);
                }

                if (p.altitude < ROCK_START &&
                    p.altitude > world.SEA_LEVEL)
                    if (Random.Range(0, 40) == 0)
                        p.world_object_gen = new world_object_generator("pine_tree");

                grid[i, j] = p;
            }
    }
}

public class terraced_hills : biome
{
    public const float HILL_HEIGHT = 50f;
    public const float HILL_SIZE = 64f;

    protected override void generate_grid()
    {
        float xrand = Random.Range(0, 1f);
        float zrand = Random.Range(0, 1f);
        for (int i = 0; i < SIZE; ++i)
            for (int j = 0; j < SIZE; ++j)
            {
                point p = new point
                {
                    altitude = HILL_HEIGHT * Mathf.PerlinNoise(
                        xrand + i / HILL_SIZE, zrand + j / HILL_SIZE),
                    terrain_color = colors.grass
                };

                if (p.altitude < HILL_HEIGHT / 2)
                {
                    if ((i % 25 == 0) && (j % 25 == 0))
                    {
                        p.world_object_gen = new world_object_generator("flat_outcrop");
                        p.world_object_gen.additional_scale_factor = 4f;
                    }
                }
                else if (Random.Range(0, 200) == 0)
                    p.world_object_gen = new world_object_generator("tree");

                grid[i, j] = p;
            }
    }
}

[biome_info(generation_enabled: false)]
public class cliffs : biome
{
    public const float CLIFF_HEIGHT = 10f;
    public const float CLIFF_PERIOD = 32f;

    public const float HILL_HEIGHT = 52f;
    public const float HILL_PERIOD = 64f;

    protected override void generate_grid()
    {
        // Generate the altitudes
        float xrand = Random.Range(0, 1f);
        float zrand = Random.Range(0, 1f);
        float[,] alt = new float[SIZE, SIZE];
        for (int i = 0; i < SIZE; ++i)
            for (int j = 0; j < SIZE; ++j)
            {
                // Work out how much cliff there is
                float cliffness = Mathf.PerlinNoise(
                    xrand + i / CLIFF_PERIOD, zrand + j / CLIFF_PERIOD);
                if (cliffness > 0.5f) cliffness = 1;
                else cliffness *= 2;

                // Work out the smooth altitude
                float asmooth = HILL_HEIGHT * Mathf.PerlinNoise(
                    xrand + i / HILL_PERIOD, zrand + j / HILL_PERIOD);

                // Work out the cliffy altitdue
                float acliff = CLIFF_HEIGHT *
                    procmath.maps.smoothed_floor(asmooth / CLIFF_HEIGHT, 0.75f);

                // Mix them
                alt[i, j] = asmooth * (1 - cliffness) + acliff * cliffness;
            }

        var grad = procmath.float_2D_tools.get_gradients(alt);
        for (int i = 0; i < SIZE; ++i)
            for (int j = 0; j < SIZE; ++j)
            {
                float dirt_amt = grad[i, j].magnitude;
                dirt_amt = procmath.maps.linear_turn_on(dirt_amt, 0.2f, 0.5f);

                grid[i, j] = new point()
                {
                    altitude = alt[i, j],
                    terrain_color = Color.Lerp(colors.grass, colors.dirt, dirt_amt)
                };
            }
    }
}