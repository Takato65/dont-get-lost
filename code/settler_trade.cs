﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class settler_trade : MonoBehaviour
{
    public item item;
    public float stock_replenish_per_minute = 0;
    public int stock_replenish_target = 10;

    public void run_stock_updates()
    {
        if (slot.stock < stock_replenish_target)
        {
            float prob = Time.deltaTime * stock_replenish_per_minute / 60f;
            if (Random.Range(0, 1f) < prob)
                slot.stock += 1;
        }
    }

    public shop_slot slot
    {
        get
        {
            if (_trade_slot == null)
            {
                _trade_slot = Resources.Load<shop_slot>("ui/shop_slot").inst();
                _trade_slot.item = item;
            }
            return _trade_slot;
        }
    }
    shop_slot _trade_slot;

#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects()]
    [UnityEditor.CustomEditor(typeof(settler_trade))]
    class editor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var st = (settler_trade)target;
            st.item = utils.select_from_folder_dropdown("Item (dropdown)", "items", st.item);
        }
    }
#endif
}