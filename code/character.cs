﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(character_hitbox))]
public class character : networked
{
    // A character is considered to have arrived at a point
    // if they are within this distance of it.
    public const float ARRIVE_DISTANCE = 0.25f;

    // Character behaviour
    public float walk_speed = 1f;
    public float run_speed = 4f;
    public int max_health = 10;
    public float pathfinding_resolution = 0.5f;
    public float height = 2f;
    public float agro_range = 5f;
    public float reach = 0.5f;
    public FRIENDLINESS friendliness;
    public bool can_walk = true;
    public bool can_swim = false;

    public enum FRIENDLINESS
    {
        AGRESSIVE,
        FRIENDLY,
        AFRAID
    }

    // The character spawner that spawned me
    character_spawner spawned_by
    {
        get
        {
            if (_spawned_by == null)
            {
                _spawned_by = transform.parent.GetComponent<character_spawner>();
                if (_spawned_by == null)
                    throw new System.Exception("Character parent is not a character spawner!");
            }
            return _spawned_by;
        }
    }
    character_spawner _spawned_by;

    bool is_allowed_at(Vector3 v)
    {
        // Check we're in the right medium
        if (!can_swim && v.y < world.SEA_LEVEL) return false;
        if (!can_walk && v.y > world.SEA_LEVEL) return false;

        // Can't get too far from spawner
        if ((v - spawned_by.transform.position).magnitude > spawned_by.max_range) return false;

        return true;
    }

    //########//
    // SOUNDS //
    //########//

    Dictionary<character_sound.TYPE, List<character_sound>> sounds =
        new Dictionary<character_sound.TYPE, List<character_sound>>();

    AudioSource sound_source;

    void load_sounds()
    {
        // Record all of the sounds I can make
        Vector3 sound_centre = transform.position;
        foreach (var s in GetComponentsInChildren<character_sound>())
        {
            List<character_sound> type_sounds;
            if (!sounds.TryGetValue(s.type, out type_sounds))
            {
                type_sounds = new List<character_sound>();
                sounds[s.type] = type_sounds;
            }
            type_sounds.Add(s);

            sound_centre = s.transform.position;
        }

        // Normalize probabilities
        foreach (var kv in sounds)
        {
            var list = kv.Value;
            float total_prob = 0;
            foreach (var s in list) total_prob += s.probability;
            foreach (var s in list) s.probability /= total_prob;
        }

        if (sound_source == null)
        {
            sound_source = new GameObject("sound_source").AddComponent<AudioSource>();
            sound_source.transform.position = sound_centre;
            sound_source.transform.SetParent(transform);
            sound_source.spatialBlend = 1f; // 3D
        }
    }

    public void play_random_sound(character_sound.TYPE type)
    {
        List<character_sound> sound_list;
        if (!sounds.TryGetValue(type, out sound_list))
        {
            Debug.Log("No character sounds of type " + type + " for " + name);
            return;
        }

        character_sound chosen = sound_list[0];
        float rnd = Random.Range(0, 1f);
        float total = 0;
        foreach (var s in sound_list)
        {
            total += s.probability;
            if (total > rnd)
            {
                chosen = s;
                break;
            }
        }

        sound_source.Stop();
        sound_source.pitch = chosen.pitch_modifier * Random.Range(0.95f, 1.05f);
        sound_source.clip = chosen.clip;
        sound_source.volume = chosen.volume;
        sound_source.Play();
    }

    void play_idle_sounds()
    {
        // Play idle sounds
        if (!sound_source.isPlaying)
            if (Random.Range(0, 1f) < 0.1f)
                play_random_sound(character_sound.TYPE.IDLE);
    }

    //#############//
    // PATHFINDING //
    //#############//

    // The current path the character is walking
    path _path;
    path path
    {
        get { return _path; }
        set { _path = value; path_progress = 0; }
    }
    int path_progress = 0;

    void get_path(Vector3 target)
    {
        path = new path(
            transform.position, target, transform,
            height, pathfinding_resolution,
            constraint: is_allowed_at
        );
    }

    bool move_towards(Vector3 point, float speed)
    {
        // Work out how far to the point
        Vector3 delta = point - transform.position;
        float dis = Time.deltaTime * speed;

        Vector3 new_pos = transform.position;

        if (delta.magnitude < dis) new_pos += delta;
        else new_pos += delta.normalized * dis;

        if (!is_allowed_at(new_pos)) return false;
        transform.position = new_pos;

        // Look in the direction of travel
        delta.y = 0;
        if (delta.magnitude > 10e-4)
        {
            // Lerp forward look direction
            Vector3 new_forward = Vector3.Lerp(transform.forward,
                delta.normalized, speed * Time.deltaTime);

            if (new_forward.magnitude > 10e-4)
                transform.forward = new_forward;
        }
        return true;
    }

    void move_along_path(float speed)
    {
        switch (path.state)
        {

            case path.STATE.SEARCHING:
                // Run pathfinding
                path.pathfind(1);
                return;

            case path.STATE.FAILED:
                // Path failed, diffuse around a little to try and fix it
                path = null;
                transform.position += Random.onUnitSphere * 0.05f;
                return;

            case path.STATE.COMPLETE:

                if (path.length <= path_progress)
                {
                    // Path complete, reset
                    path = null;
                    return;
                }

                // Move towards the next path point
                if (!move_towards(path[path_progress], speed))
                {
                    // Couldn't walk along the path
                    path = null;
                    return;
                }

                Vector3 delta = path[path_progress] - transform.position;

                // Increment progress if we've arrived at the next path point
                if (delta.magnitude < ARRIVE_DISTANCE) ++path_progress;
                return;

            default:
                throw new System.Exception("Unkown path state!");
        }
    }

    // Just idly wonder around
    void idle_walk()
    {
        if (path == null)
        {
            // Search for a new walk target
            Vector3 location = spawned_by.transform.position +
                Random.insideUnitSphere * spawned_by.max_range;

            RaycastHit hit;
            if (Physics.Raycast(location, -Vector3.up, out hit, 10f))
                get_path(hit.point);
        }
        else move_along_path(walk_speed);
    }

    // Run from the given transform
    void flee(Transform fleeing_from)
    {
        play_idle_sounds();

        if (path == null)
        {
            Vector3 delta = (transform.position - fleeing_from.position).normalized;
            Vector3 flee_to = transform.position + delta * 5f;

            if (is_allowed_at(flee_to))
                get_path(flee_to); // Flee away
            else
                get_path(fleeing_from.position - delta * 5f); // Flee back the way we came
        }
        else move_along_path(run_speed);
    }

    void chase(Transform chasing)
    {
        play_idle_sounds();

        if (path == null)
        {
            Vector3 delta = chasing.position - transform.position;
            Vector3 chase_to = transform.position + delta;

            if (delta.magnitude < pathfinding_resolution)
            {
                if (delta.magnitude > reach)
                    move_towards(chasing.position, run_speed);

                return;
            }

            if (is_allowed_at(chase_to))
                get_path(chase_to);
            else
                idle_walk();
        }
        else move_along_path(run_speed);
    }

    //#################//
    // UNITY CALLBACKS //
    //#################//

    private void Start()
    {
        load_sounds();
        InvokeRepeating("slow_update", Random.Range(0, 1f), 1f);
    }

    void slow_update()
    {
        play_idle_sounds();
    }

    private void Update()
    {
        if (!has_authority) return;

        if ((transform.position - player.current.transform.position).magnitude < agro_range)
        {
            switch (friendliness)
            {
                case FRIENDLINESS.AGRESSIVE:
                    chase(player.current.transform);
                    break;

                case FRIENDLINESS.AFRAID:
                    flee(player.current.transform);
                    break;

                default:
                    idle_walk();
                    break;
            }
        }
        else
            idle_walk();
    }

    private void OnDrawGizmosSelected()
    {
        // Draw path gizmos
        if (path != null)
            path.draw_gizmos();

        Vector3 f = transform.forward * pathfinding_resolution * 0.5f;
        Vector3 r = transform.right * pathfinding_resolution * 0.5f;
        Vector3[] square = new Vector3[]
        {
            transform.position + f + r,
            transform.position + f - r,
            transform.position - f - r,
            transform.position - f + r,
            transform.position + f + r
        };

        Gizmos.color = Color.green;
        for (int i = 1; i < square.Length; ++i)
            Gizmos.DrawLine(square[i - 1], square[i]);

        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * height);
    }

    //############//
    // NETWORKING //
    //############//

    networked_variables.net_float y_rotation;
    public networked_variables.net_int health;

    healthbar healthbar
    {
        get
        {
            if (_healthbar == null)
            {
                _healthbar = new GameObject("healthbar").AddComponent<healthbar>();
                _healthbar.transform.SetParent(transform);
                _healthbar.transform.position = transform.position + Vector3.up;
                _healthbar.belongs_to = this;
            }
            return _healthbar;
        }
    }
    healthbar _healthbar;

    public override void on_init_network_variables()
    {
        y_rotation = new networked_variables.net_float(resolution: 5f);
        y_rotation.on_change = () =>
        {
            var ea = transform.rotation.eulerAngles;
            ea.y = y_rotation.value;
            transform.rotation = Quaternion.Euler(ea);
        };

        health = new networked_variables.net_int();
        health.value = max_health;

        health.on_change = () =>
        {
            if (health.value >= max_health)
            {
                if (healthbar != null)
                    Destroy(healthbar.gameObject);
            }
            else
                healthbar.belongs_to = this;

            if (health.value <= 0 && has_authority)
            {
                foreach (var p in GetComponents<product>())
                    p.create_in_inventory(player.current.inventory.contents);
                delete();
            }
        };
    }

    public override void on_network_update()
    {
        if (has_authority)
        {
            networked_position = transform.position;
            y_rotation.value = transform.rotation.eulerAngles.y;
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(character), true)]
    new public class editor : networked.editor
    {
        string last_path = "Last path info";
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var c = (character)target;
            if (c.path != null)
                last_path = "Last path info\n" + c.path.info();
            UnityEditor.EditorGUILayout.TextArea(last_path);
        }
    }
#endif
}

class healthbar : MonoBehaviour
{
    public const int WIDTH = 100;
    public const int HEIGHT = 10;

    Canvas canv;
    RectTransform canv_rect;
    public character belongs_to;

    UnityEngine.UI.Image background;
    UnityEngine.UI.Image foreground;
    RectTransform background_rect;
    RectTransform foreground_rect;

    private void Start()
    {
        canv = gameObject.AddComponent<Canvas>();
        canv.renderMode = RenderMode.WorldSpace;
        canv.worldCamera = player.current.camera;
        canv_rect = canv.GetComponent<RectTransform>();
        canv_rect.SetParent(transform);
        canv_rect.localRotation = Quaternion.identity;
        canv_rect.sizeDelta = new Vector2(WIDTH, HEIGHT);

        background = new GameObject("background").AddComponent<UnityEngine.UI.Image>();
        foreground = new GameObject("foreground").AddComponent<UnityEngine.UI.Image>();
        background.color = Color.red;
        foreground.color = Color.green;
        background_rect = background.GetComponent<RectTransform>();
        foreground_rect = foreground.GetComponent<RectTransform>();

        background_rect.SetParent(canv_rect);
        foreground_rect.SetParent(canv_rect);

        background_rect.sizeDelta = canv_rect.sizeDelta;
        foreground_rect.sizeDelta = canv_rect.sizeDelta;

        background_rect.localPosition = Vector3.zero;
        foreground_rect.localPosition = Vector3.zero;

        background_rect.anchoredPosition = Vector2.zero;
        foreground_rect.anchoredPosition = Vector2.zero;

        background_rect.localRotation = Quaternion.identity;
        foreground_rect.localRotation = Quaternion.identity;

        canv_rect.transform.localScale = new Vector3(1f, 1f, 1f) / (float)WIDTH;
    }

    private void Update()
    {
        foreground.GetComponent<RectTransform>().sizeDelta = new Vector2(
            canv_rect.sizeDelta.x * belongs_to.health.value / belongs_to.max_health,
            canv_rect.sizeDelta.y
        );
    }
}