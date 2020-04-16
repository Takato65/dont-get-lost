﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// An object that the player can interact with
public class interactable : MonoBehaviour
{
    [System.Flags]
    public enum FLAGS
    {
        NONE = 0,
        DISALLOWS_MOVEMENT = 2,
        DISALLOWS_ROTATION = 4,
    };

    public virtual string cursor() { return cursors.DEFAULT_INTERACTION; }
    public virtual FLAGS player_interact() { return FLAGS.NONE; }
    public virtual void on_start_interaction(RaycastHit point_hit) { }
    public virtual void on_end_interaction() { }
    protected void stop_interaction() { player.current.interacting_with = null; }
}

public class player : MonoBehaviour
{
    //###########//
    // CONSTANTS //
    //###########//

    public const float HEIGHT = 1.8f;
    public const float WIDTH = 0.45f;
    public const float GRAVITY = 10f;
    public const float BOUYANCY = 5f;
    public const float WATER_DRAG = 1.5f;

    public const float SPEED = 10f;
    public const float ACCELERATION_TIME = 0.2f;
    public const float ACCELERATION = SPEED / ACCELERATION_TIME;
    public const float ROTATION_SPEED = 90f;
    public const float JUMP_VEL = 5f;

    public const float INTERACTION_RANGE = 3f;

    public const float MAP_CAMERA_ALT = world.MAX_ALTITUDE * 2;
    public const float MAP_CAMERA_CLIP = world.MAX_ALTITUDE * 3;
    public const float MAP_SHADOW_DISTANCE = world.MAX_ALTITUDE * 3;

    //###############//
    // SERIALIZATION //
    //###############//

    public void save()
    {
        var floats = new float[]
        {
            transform.position.x,
            transform.position.y,
            transform.position.z
        };

        using (var fs = new System.IO.FileStream(world.save_folder() + "/player",
            System.IO.FileMode.Create, System.IO.FileAccess.Write))
        {
            for (int i = 0; i < floats.Length; ++i)
            {
                var float_bytes = System.BitConverter.GetBytes(floats[i]);
                fs.Write(float_bytes, 0, float_bytes.Length);
            }
        }
    }

    void load()
    {
        if (!System.IO.File.Exists(world.save_folder() + "/player")) return;

        var floats = new float[3];

        using (var fs = new System.IO.FileStream(world.save_folder() + "/player",
            System.IO.FileMode.Open, System.IO.FileAccess.Read))
        {
            byte[] float_bytes = new byte[sizeof(float)];
            for (int i = 0; i < floats.Length; ++i)
            {
                fs.Read(float_bytes, 0, float_bytes.Length);
                floats[i] = System.BitConverter.ToSingle(float_bytes, 0);
            }
        }

        Vector3 pos = new Vector3(floats[0], floats[1], floats[2]);
        transform.position = pos;
    }

    //#################//
    // UNITY CALLBACKS //
    //#################//

    void Update()
    {
        // Toggle inventory on E
        if (Input.GetKeyDown(KeyCode.E))
            inventory_open = !inventory_open;
        Cursor.visible = inventory_open;
        Cursor.lockState = inventory_open ? CursorLockMode.None : CursorLockMode.Locked;
        if (inventory_open) return;

        var inter_flags = interact();

        // Toggle the map view
        if (Input.GetKeyDown(KeyCode.M))
            map_open = !map_open;

        if (map_open)
        {
            // Zoom the map
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0) game.render_range_target /= 1.2f;
            else if (scroll < 0) game.render_range_target *= 1.2f;
            camera.orthographicSize = game.render_range;
        }

        if (inter_flags.HasFlag(interactable.FLAGS.DISALLOWS_MOVEMENT))
            velocity = Vector3.zero;
        else move();

        if (!inter_flags.HasFlag(interactable.FLAGS.DISALLOWS_ROTATION))
            mouse_look();

        // Float in water
        float amt_submerged = (world.SEA_LEVEL - transform.position.y) / HEIGHT;
        if (amt_submerged > 1.0f) amt_submerged = 1.0f;
        if (amt_submerged > 0)
        {
            // Bouyancy (sink if shift is held)
            if (!Input.GetKey(KeyCode.LeftShift))
                velocity.y += amt_submerged * (GRAVITY + BOUYANCY) * Time.deltaTime;

            // Drag
            velocity -= velocity * amt_submerged * WATER_DRAG * Time.deltaTime;
        }

        underwater_screen.SetActive(camera.transform.position.y < world.SEA_LEVEL && !map_open);

        // Use my tool
        if (interacting_with == null)
        {
            if (Input.GetMouseButtonDown(0))
                swing_tool();

            if (tool_swing_progress < 1f)
            {
                tool_swing_progress += Time.deltaTime / TOOL_SWING_TIME;

                float fw_amt = -Mathf.Sin(tool_swing_progress * Mathf.PI * 2f);
                hand.transform.localPosition = init_hand_local_position +
                    fw_amt * Vector3.forward * TOOL_SWING_DISTANGE -
                    fw_amt * Vector3.up * TOOL_SWING_DISTANGE -
                    fw_amt * Vector3.right * init_hand_local_position.x;

                Vector3 up = camera.transform.up * (1 - fw_amt) + camera.transform.forward * fw_amt;
                Vector3 fw = -Vector3.Cross(up, camera.transform.right);
                hand.transform.rotation = Quaternion.LookRotation(fw, up);
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, game.render_range);
    }

    //###########//
    // INVENTORY //
    //###########//

    inventory _inventory;
    inventory inventory
    {
        get
        {
            if (_inventory == null)
            {
                _inventory = Resources.Load<inventory>("ui/player_inventory").inst();
                _inventory.transform.SetParent(FindObjectOfType<Canvas>().transform);
                _inventory.transform.position = new Vector3(Screen.width / 2, Screen.height / 2, 0);
                inventory_open = false;
            }
            return _inventory;
        }
    }

    bool inventory_open
    {
        get { return inventory.gameObject.activeInHierarchy; }
        set { inventory.gameObject.SetActive(value); }
    }



    //##########//
    // TOOL USE //
    //##########//

    const float TOOL_SWING_TIME = 0.5f;
    const float TOOL_SWING_DISTANGE = 0.25f;
    float tool_swing_progress = 1f;
    Vector3 init_hand_local_position;

    // The hand which carries a tool
    Transform _hand;
    Transform hand
    {
        get
        {
            if (_hand == null)
            {
                // Set the hand location so it is one meter
                // away from the camera, 80% of the way across 
                // the screen and 10% of the way up the screen.
                _hand = new GameObject("hand").transform;
                _hand.SetParent(camera.transform);
                var r = camera.ScreenPointToRay(new Vector3(
                     Screen.width * 0.8f,
                     Screen.height * 0.1f
                     ));
                _hand.localPosition = r.direction * 0.75f;
                init_hand_local_position = _hand.localPosition;
            }
            return _hand;
        }
    }

    void swing_tool()
    {
        tool_swing_progress = 0f;
    }

    // The current equipped tool
    tool _equipped;
    public tool equipped
    {
        get { return _equipped; }
        private set
        {
            if (_equipped != null)
            {
                _equipped.transform.SetParent(null);
            }

            _equipped = value;
            if (value != null)
            {
                value.transform.SetParent(hand);
                value.transform.localPosition = Vector3.zero;
            }
        }
    }

    //##################//
    // ITEM INTERACTION //
    //##################//

    // The object we are currently interacting with
    RaycastHit last_interaction_hit;
    interactable _interacting_with;
    public interactable interacting_with
    {
        get { return _interacting_with; }
        set
        {
            if (_interacting_with != null)
                _interacting_with.on_end_interaction();

            _interacting_with = value;

            if (value != null)
                value.on_start_interaction(last_interaction_hit);
        }
    }

    interactable.FLAGS interact()
    {
        // Interact with the current object
        if (interacting_with != null)
        {
            canvas.cursor = interacting_with.cursor();
            return interacting_with.player_interact();
        }

        // See if an interactable object is under the cursor
        var inter = utils.raycast_for_closest<interactable>(
            camera_ray(), out last_interaction_hit, INTERACTION_RANGE);

        if (inter == null)
        {
            canvas.cursor = cursors.DEFAULT;
            return interactable.FLAGS.NONE;
        }
        else canvas.cursor = cursors.DEFAULT_INTERACTION;

        // Set the interactable and cursor,
        // interact with the object
        if (Input.GetMouseButtonDown(0))
            interacting_with = inter;

        return interactable.FLAGS.NONE;
    }

    //###########//
    //  MOVEMENT //
    //###########//

    CharacterController controller;
    Vector3 velocity = Vector3.zero;

    void move()
    {
        if (controller.isGrounded)
        {
            if (Input.GetKeyDown(KeyCode.Space))
                velocity.y = JUMP_VEL;
        }
        else velocity.y -= GRAVITY * Time.deltaTime;

        if (Input.GetKey(KeyCode.W)) velocity += transform.forward * ACCELERATION * Time.deltaTime;
        else if (Input.GetKey(KeyCode.S)) velocity -= transform.forward * ACCELERATION * Time.deltaTime;
        else velocity -= Vector3.Project(velocity, transform.forward);

        if (Input.GetKey(KeyCode.D)) velocity += camera.transform.right * ACCELERATION * Time.deltaTime;
        else if (Input.GetKey(KeyCode.A)) velocity -= camera.transform.right * ACCELERATION * Time.deltaTime;
        else velocity -= Vector3.Project(velocity, camera.transform.right);

        float xz = new Vector3(velocity.x, 0, velocity.z).magnitude;
        if (xz > SPEED)
        {
            velocity.x *= SPEED / xz;
            velocity.z *= SPEED / xz;
        }

        Vector3 move = velocity * Time.deltaTime;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            move.x *= 10f;
            move.z *= 10f;
        }

        controller.Move(move);
        stay_above_terrain();
    }

    void stay_above_terrain()
    {
        Vector3 pos = transform.position;
        pos.y = world.MAX_ALTITUDE;
        RaycastHit hit;
        var tc = utils.raycast_for_closest<TerrainCollider>(new Ray(pos, Vector3.down), out hit);
        if (hit.point.y > transform.position.y)
            transform.position = hit.point;
    }

    //#####################//
    // VIEW/CAMERA CONTROL //
    //#####################//

    // Objects used to obscure player view
    public new Camera camera { get; private set; }
    GameObject obscurer;
    GameObject map_obscurer;
    GameObject underwater_screen;

    // Called when the render range changes
    public void update_render_range()
    {
        // Set the obscurer size to the render range
        obscurer.transform.localScale = Vector3.one * game.render_range * 0.99f;
        map_obscurer.transform.localScale = Vector3.one * game.render_range;

        if (!map_open)
        {
            // If in 3D mode, set the camera clipping plane range to
            // the same as render_range
            camera.farClipPlane = game.render_range;
            QualitySettings.shadowDistance = camera.farClipPlane;
        }
    }

    void mouse_look()
    {
        if (map_open)
        {
            // Rotate the player with A/D
            float xr = 0;
            if (Input.GetKey(KeyCode.A)) xr = -1f;
            else if (Input.GetKey(KeyCode.D)) xr = 1.0f;
            transform.Rotate(0, xr * Time.deltaTime * ROTATION_SPEED, 0);
            return;
        }

        // Rotate the view using the mouse
        // Note that horizontal moves rotate the player
        // vertical moves rotate the camera
        transform.Rotate(0, Input.GetAxis("Mouse X") * 5, 0);
        camera.transform.Rotate(-Input.GetAxis("Mouse Y") * 5, 0, 0);
    }

    // Saved rotation to restore when we return to the 3D view
    Quaternion saved_camera_rotation;

    // True if in map view
    public bool map_open
    {
        get { return camera.orthographic; }
        set
        {
            // Use the appropriate obscurer for
            // the map or 3D views
            map_obscurer.SetActive(value);
            obscurer.SetActive(!value);

            // Set the camera orthograpic if in 
            // map view, otherwise perspective
            camera.orthographic = value;

            if (value)
            {
                // Save camera rotation to restore later
                saved_camera_rotation = camera.transform.localRotation;

                // Setup the camera in map mode/position   
                camera.orthographicSize = game.render_range;
                camera.transform.localPosition = Vector3.up * (MAP_CAMERA_ALT - transform.position.y);
                camera.transform.localRotation = Quaternion.Euler(90, 0, 0);
                camera.farClipPlane = MAP_CAMERA_CLIP;

                // Render shadows further in map view
                QualitySettings.shadowDistance = MAP_SHADOW_DISTANCE;
            }
            else
            {
                // Restore 3D camera view
                camera.transform.localPosition = Vector3.up * (HEIGHT - WIDTH / 2f);
                camera.transform.localRotation = saved_camera_rotation;
            }
        }
    }

    // Return a ray going through the centre of the screen
    public Ray camera_ray()
    {
        return new Ray(camera.transform.position,
                       camera.transform.forward);
    }

    //################//
    // STATIC METHODS //
    //################//

    // The current player
    public static player current;

    // Create and return a player
    public static player create()
    {
        var p = new GameObject("player").AddComponent<player>();

        // Create the player camera 
        p.camera = FindObjectOfType<Camera>();
        p.camera.clearFlags = CameraClearFlags.SolidColor;
        p.camera.transform.SetParent(p.transform);
        p.camera.transform.localPosition = new Vector3(0, HEIGHT - WIDTH / 2f, 0);
        p.camera.nearClipPlane = 0.1f;
        //p.camera.gameObject.AddComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();


        // Create a short range light with no shadows to light up detail
        // on nearby objects to the player
        /*
        var point_light = new GameObject("point_light").AddComponent<Light>();
        point_light.type = LightType.Point;
        point_light.range = item.WELD_RANGE;
        point_light.transform.SetParent(p.camera.transform);
        point_light.transform.localPosition = Vector3.zero;
        point_light.intensity = 0.5f;
        */

        // Enforce the render limit with a sky-color object
        p.obscurer = Resources.Load<GameObject>("misc/obscurer").inst();
        p.obscurer.transform.SetParent(p.transform);
        p.obscurer.transform.localPosition = Vector3.zero;
        var sky_color = p.obscurer.GetComponentInChildren<Renderer>().material.color;

        p.map_obscurer = Resources.Load<GameObject>("misc/map_obscurer").inst();
        p.map_obscurer.transform.SetParent(p.camera.transform);
        p.map_obscurer.transform.localPosition = Vector3.forward;
        p.map_obscurer.transform.up = -p.camera.transform.forward;

        p.underwater_screen = Resources.Load<GameObject>("misc/underwater_screen").inst();
        p.underwater_screen.transform.SetParent(p.camera.transform);
        p.underwater_screen.transform.localPosition = Vector3.forward * p.camera.nearClipPlane * 1.1f;
        p.underwater_screen.transform.forward = p.camera.transform.forward;

        // Make the sky the same color as the obscuring object
        RenderSettings.skybox = null;
        p.camera.backgroundColor = sky_color;

        // Initialize the render range
        p.update_render_range();

        // Start with the map closed
        p.map_open = false;

        // Load the player state
        p.load();

        // Create the player controller
        p.controller = p.gameObject.AddComponent<CharacterController>();
        p.controller.height = HEIGHT;
        p.controller.radius = WIDTH / 2;
        p.controller.center = new Vector3(0, p.controller.height / 2f, 0);
        p.controller.skinWidth = p.controller.radius / 10f;

        p.equipped = tool.loop_up("axe").inst();

        current = p;
        return p;
    }
}