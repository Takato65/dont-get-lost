using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class item_harvest_spot : settler_interactable_options
{
    public List<item> options = new List<item>();
    public float harvest_time = 1;

    //##############################//
    // settler_interactable_options //
    //##############################//

    protected override option get_option(int i)
    {
        return new option
        {
            text = options[i].display_name,
            sprite = options[i].sprite
        };
    }

    protected override int options_count => options.Count;

    //######################//
    // SETTLER_INTERACTABLE //
    //######################//

    item_output output => GetComponentInChildren<item_output>();

    float elapsed_time = 0;
    int harvested_count = 0;

    public override INTERACTION_RESULT on_assign(settler s)
    {
        elapsed_time = 0;
        harvested_count = 0;
        return INTERACTION_RESULT.UNDERWAY;
    }

    public override INTERACTION_RESULT on_interact(settler s)
    {
        elapsed_time += Time.deltaTime;
        if (elapsed_time > (harvested_count + 1) * harvest_time)
        {
            harvested_count += 1;
            var itm = options[selected_option];
            output.add_item(item.create(itm.name, output.transform.position,
                output.transform.rotation, logistics_version: true));
        }

        if (elapsed_time > 5f) return INTERACTION_RESULT.COMPLETE;
        return INTERACTION_RESULT.UNDERWAY;
    }
}