﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class sack : networked, IInspectable, IPlayerInteractable
{
    inventory inventory;

    //############//
    // NETWORKING //
    //############//

    networked_variables.net_string display_name;

    public override void on_init_network_variables()
    {
        display_name = new networked_variables.net_string("sack");
    }

    public override void on_add_networked_child(networked child)
    {
        base.on_add_networked_child(child);

        if (child is inventory)
            inventory = (inventory)child;
    }

    //##############//
    // IINspectable //
    //##############//

    public string inspect_info()
    {
        return display_name.value;
    }

    public Sprite main_sprite() { return null; }
    public Sprite secondary_sprite() { return null; }

    //#################//
    // ILeftPlayerMenu //
    //#################//

    player_interaction[] interactions;

    public player_interaction[] player_interactions()
    {
        if (interactions == null) interactions = new player_interaction[] { new menu(this) };
        return interactions;
    }

    class menu : left_player_menu
    {
        sack sack;
        public menu(sack sack) { this.sack = sack; }
        protected override RectTransform create_menu() { return sack.inventory.ui; }
        public override inventory editable_inventory() { return sack.inventory; }
        public override string display_name() { return sack.display_name.value; }
        protected override void on_open() { menu.GetComponentInChildren<UnityEngine.UI.Text>().text = display_name(); }
        protected override void on_close() { if (sack.inventory.is_empty()) sack.delete(); }
    }

    //##############//
    // STATIC STUFF //
    //##############//

    public static sack create(Vector3 location,
        IEnumerable<KeyValuePair<item, int>> contents = null,
        string display_name = "sack")
    {
        var sack = client.create(location, "misc/sack").GetComponent<sack>();
        sack.display_name.value = display_name;

        sack.add_register_listener(() =>
        {
            client.create(location, "inventories/sack", sack);

            if (contents != null)
                sack.inventory.add_register_listener(() =>
                {
                    foreach (var kv in contents)
                        sack.inventory.add(kv.Key, kv.Value);
                });
        });

        return sack;
    }
}
