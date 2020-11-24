﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary> An object that can be connected to other 
/// objects of the same kind via road_links. </summary>
public class settler_path_element : MonoBehaviour, IAddsToInspectionText
{
    public settler_interactable interactable => GetComponentInParent<settler_interactable>();

    public settler_path_link[] links
    {
        get
        {
            if (_links == null)
                _links = GetComponentsInChildren<settler_path_link>();
            return _links;
        }
    }
    settler_path_link[] _links;

    public List<settler_path_element> linked_elements()
    {
        List<settler_path_element> ret = new List<settler_path_element>();
        foreach (var l in links)
            if (l.linked_to != null)
            {
                var rl = l.linked_to.GetComponentInParent<settler_path_element>();
                if (rl != null)
                    ret.Add(rl);
            }
        return ret;
    }

    public int group
    {
        get; private set;
    }

    public string added_inspection_text()
    {
        return "Group " + group;
    }

    bool registered = false;
    private void Start()
    {
        // Don't register this path element if we are 
        // part of an unplaced building material
        var bm = GetComponentInParent<building_material>();
        if (bm != null && (bm.is_equpped || bm.is_blueprint))
            return;

        // Register this element, if neccassary
        registered = true;
        register_element(this);
    }

    private void OnDestroy()
    {
        // Unregister this element, if neccassary
        if (registered)
            forget_element(this);
    }

    void try_link(settler_path_element other)
    {
        // Can't link to self
        if (other == this) return;

        foreach (var l in links)
        {
            // L already linked
            if (l.linked_to != null) continue;

            foreach (var l2 in other.links)
            {
                // L2 already linked
                if (l2.linked_to != null) continue;

                if ((l.transform.position - l2.transform.position).magnitude <
                    settler_path_link.LINK_DISTANCE)
                {
                    // Make link both ways
                    l.linked_to = l2;
                    l2.linked_to = l;
                    break;
                }
            }
        }
    }

    void break_links()
    {
        foreach (var l in links)
        {
            if (l.linked_to != null)
            {
                l.linked_to.linked_to = null;
                l.linked_to = null;
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        foreach (var l in links)
            if (l.linked_to != null)
            {
                Gizmos.DrawLine(transform.position, l.transform.position);
                Gizmos.DrawLine(l.transform.position, l.linked_to.transform.position);
            }
    }

    float heuristic(settler_path_element other)
    {
        return (transform.position - other.transform.position).magnitude;
    }

    //##############//
    // STATIC STUFF //
    //##############//

    static HashSet<settler_path_element> all_elements;
    static Dictionary<int, HashSet<settler_path_element>> grouped_elements;

    public static settler_path_element nearest_element(Vector3 v)
    {
        return utils.find_to_min(all_elements,
            (e) => (e.transform.position - v).sqrMagnitude);
    }

    public static HashSet<settler_path_element> element_group(int group)
    {
        if (grouped_elements.TryGetValue(group, out HashSet<settler_path_element> elms))
            return elms;
        return new HashSet<settler_path_element>();
    }

    public static settler_path_element find_nearest(Vector3 position)
    {
        return utils.find_to_min(all_elements,
            (r) => (r.transform.position - position).sqrMagnitude);
    }

    public static void initialize()
    {
        // Initialize theelements collection
        all_elements = new HashSet<settler_path_element>();
        grouped_elements = new Dictionary<int, HashSet<settler_path_element>>();
    }

    static void evaluate_groups()
    {
        int group = 0;

        HashSet<settler_path_element> ungrouped = new HashSet<settler_path_element>(all_elements);

        while (ungrouped.Count > 0)
        {
            // Create an open set from the first ungrouped element
            HashSet<settler_path_element> open = new HashSet<settler_path_element> { ungrouped.First() };
            HashSet<settler_path_element> closed = new HashSet<settler_path_element>();

            while (open.Count > 0)
            {
                // Get the first open element
                var to_expand = open.First();
                closed.Add(to_expand);
                open.Remove(to_expand);

                // Add all linked elements to the open set 
                // (if they arent closed set)
                foreach (var n in to_expand.linked_elements())
                    if (!closed.Contains(n))
                        open.Add(n);
            }

            grouped_elements[group] = closed;
            foreach (var e in closed)
            {
                ungrouped.Remove(e);
                e.group = group;
            }
            ++group;
        }

        settler.update_all_groups();
    }

    static void validate_links(settler_path_element r)
    {
        // Re-make all links to/from r
        r.break_links();
        foreach (var r2 in all_elements)
            r.try_link(r2);

        evaluate_groups();
    }

    static void register_element(settler_path_element r)
    {
        // Create links to/from r, add r to the collection of elements.
        if (!all_elements.Add(r))
            throw new System.Exception("Tried to register element twice!");

        validate_links(r);
    }

    static void forget_element(settler_path_element r)
    {
        // Forget all the links to/from r, remove r from the collection of elements
        if (!all_elements.Remove(r))
            throw new System.Exception("Tried to forget unregistered element!");

        r.break_links();
        evaluate_groups();
    }

    /// <summary> Set to true to draw objects representing 
    /// links between path elements. </summary>
    public static bool draw_links
    {
        get => _draw_links;
        set
        {
            _draw_links = value;
            foreach (var e in all_elements)
                foreach (var l in e.links)
                    l.display_enabled = value;
        }
    }
    static bool _draw_links;

    public static List<settler_path_element> path(Vector3 v, settler_path_element goal)
    {
        return path(nearest_element(v), goal);
    }

    /// <summary> Find a path between the start and end elements, using 
    /// the A* algorithm. Returns null if no such path exists. </summary>
    public static List<settler_path_element> path(settler_path_element start, settler_path_element goal)
    {
        if (start == null || goal == null) return null;
        if (start.group != goal.group) return null;

        // Setup pathfinding state
        var open_set = new HashSet<settler_path_element>();
        var closed_set = new HashSet<settler_path_element>();
        var came_from = new Dictionary<settler_path_element, settler_path_element>();
        var fscore = new Dictionary<settler_path_element, float>();
        var gscore = new Dictionary<settler_path_element, float>();

        // Initialize pathfinding with just start open
        open_set.Add(start);
        gscore[start] = 0;
        fscore[start] = goal.heuristic(start);

        while (open_set.Count > 0)
        {
            // Find the lowest fscore in the open set
            var current = utils.find_to_min(open_set, (c) => fscore[c]);

            if (current == goal)
            {
                // Success - reconstruct path
                List<settler_path_element> path = new List<settler_path_element> { current };
                while (came_from.TryGetValue(current, out current))
                    path.Add(current);
                path.Reverse();
                return path;
            }

            // Close current
            open_set.Remove(current);
            closed_set.Add(current);

            foreach (var n in current.linked_elements())
            {
                if (closed_set.Contains(n))
                    continue;

                // Work out tentative path length to n, if we wen't via current
                var tgs = gscore[current] + n.heuristic(current);

                // Get the current neighbour gscore (infinity if not already scored)
                if (!gscore.TryGetValue(n, out float gsn))
                    gsn = Mathf.Infinity;

                if (tgs < gsn)
                {
                    // This is a better path to n, update it
                    came_from[n] = current;
                    gscore[n] = tgs;
                    fscore[n] = tgs + goal.heuristic(n);
                    open_set.Add(n);
                }
            }
        }

        // Pathfinding failed
        return null;
    }
}