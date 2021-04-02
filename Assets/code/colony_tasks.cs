using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class colony_tasks : MonoBehaviour
{
    public RectTransform header_skill_template;
    public RectTransform row_skill_template;
    public RectTransform row_template;

    public UnityEngine.UI.ScrollRect header_rect;
    public UnityEngine.UI.ScrollRect table_rect;

    void setup_skills()
    {
        // Already done?
        if (header_skill_template == null) return;

        foreach (var s in skill.all)
        {
            if (!s.is_visible) continue;

            // Create the header for this skill
            var h = header_skill_template.inst();
            h.SetParent(header_skill_template.parent);
            h.GetComponentInChildren<UnityEngine.UI.Text>().text = s.display_name.capitalize();

            // Create the skills in the row template
            var r = row_skill_template.inst();
            r.SetParent(row_skill_template.parent);
            r.name = s.name;
        }

        // The skills are built and will not need
        // changing, delete corresponding templates
        Destroy(header_skill_template.gameObject);
        Destroy(row_skill_template.gameObject);
        header_skill_template = null;
        row_skill_template = null;

        // Only disable the row template as it
        // is used in the refresh() function
        row_template.gameObject.SetActive(false);
    }

    public void refresh()
    {
        setup_skills();

        // Delete all old rows
        foreach (RectTransform row in row_template.parent)
            if (row != row_template)
                Destroy(row.gameObject);

        // Reactivate the row template for copying
        row_template.gameObject.SetActive(true);

        // Create a row for each settler
        foreach (var s in settler.all_settlers())
        {
            var r = row_template.inst();
            r.SetParent(row_template.parent);
            r.GetComponentInChildren<UnityEngine.UI.Text>().text = s.name.capitalize();

            foreach (var sk in skill.all)
            {
                if (!sk.is_visible) continue;

                var tra = r.Find("skills").Find(sk.name);
                if (tra == null) Debug.LogError("Could not find skill entry for " + sk);
                var but = tra.GetComponentInChildren<UnityEngine.UI.Button>();
                var txt = but.GetComponentInChildren<UnityEngine.UI.Text>();
                txt.text = skill.xp_to_level(s.skills[sk]).ToString();
                but.image.color = skill.priority_color(s.job_priorities[sk]);

                but.onClick.AddListener(() =>
                {
                    if (s == null) return;
                    s.job_priorities[sk] = skill.cycle_priority(s.job_priorities[sk]);
                    refresh();
                });
            }
        }

        // Deactivate the row template again
        row_template.gameObject.SetActive(false);
    }

    private void Update()
    {
        // Keep the header alligned to the table
        header_rect.horizontalNormalizedPosition = table_rect.horizontalNormalizedPosition;
    }
}