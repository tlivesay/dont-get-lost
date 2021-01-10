﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface INonEquipable { }
public interface INonEquipableCallback : INonEquipable { void on_equip_callback(player player); }
public interface INonLogistical { }

public class item : networked, IInspectable, IPlayerInteractable
{
    public const float LOGISTICS_SIZE = 0.3f;

    static item()
    {
        tips.add("To see if you can eat an item, hover over it and press " +
                 controls.current_bind(controls.BIND.INSPECT) + " to check it's food value. " +
                 "Equip it in your quckbar and left click to eat.");
    }

    //###########//
    // VARIABLES //
    //###########//

    public Sprite sprite; // The sprite represeting this item in inventories etc
    public string plural;
    public int value;
    public int fuel_value = 0;
    public float logistics_scale = 1f; // How much to scale the item by when it is in the logistics network
    public bool is_equpped => GetComponentInParent<player>() != null;

    public food food_values => GetComponent<food>();

    void make_logistics_version()
    {
        if (!is_client_side)
            throw new System.Exception("Can only make client side items into the logstics version!");

        is_logistics_version = true;
        transform.localScale *= logistics_scale;

        // Remove components that are incompatible with the logistics version
        foreach (var c in GetComponentsInChildren<Component>())
            if (c is INonLogistical)
                Destroy(c);
    }

    public bool is_logistics_version { get; private set; }

    public string display_name
    {
        get => name.Replace('_', ' ');
    }

    public string singular_or_plural(int count)
    {
        if (count == 1) return display_name;
        return plural;
    }

    //#####################//
    // IPlayerInteractable //
    //#####################//

    public virtual player_interaction[] player_interactions()
    {
        return new player_interaction[] { new pick_up_interaction(this), new select_matching_interaction(this) };
    }

    class pick_up_interaction : player_interaction
    {
        item item;
        public pick_up_interaction(item item) { this.item = item; }

        public override bool conditions_met()
        {
            return controls.mouse_click(controls.MOUSE_BUTTON.LEFT);
        }

        public override bool start_interaction(player player)
        {
            item.pick_up();
            return true;
        }

        public override string context_tip()
        {
            return "Left click to pick up " + item.display_name;
        }
    }

    class select_matching_interaction : player_interaction
    {
        item item;
        public select_matching_interaction(item item) { this.item = item; }

        public override bool conditions_met()
        {
            return controls.key_press(controls.BIND.SELECT_ITEM_FROM_WORLD);
        }

        public override bool start_interaction(player player)
        {
            player.current.equip_matching(item);
            return true;
        }

        public override string context_tip()
        {
            return "Press Q to select matching objects from inventory";
        }
    }

    //############//
    // PLAYER USE //
    //############//

    public struct use_result
    {
        public bool underway;
        public bool allows_look;
        public bool allows_move;
        public bool allows_throw;

        public static use_result complete => new use_result()
        {
            underway = false,
            allows_look = true,
            allows_move = true,
            allows_throw = true
        };

        public static use_result underway_allows_none => new use_result()
        {
            underway = true,
            allows_look = false,
            allows_move = false,
            allows_throw = false
        };

        public static use_result underway_allows_all => new use_result()
        {
            underway = true,
            allows_look = true,
            allows_move = true,
            allows_throw = true
        };

        public static use_result underway_allows_look_only => new use_result()
        {
            underway = true,
            allows_look = true,
            allows_move = false,
            allows_throw = false
        };
    }

    // Use the equipped version of this item
    public virtual use_result on_use_start(player.USE_TYPE use_type, player player)
    {
        // If this is the authority player, carry out basic uses
        if (player.has_authority)
        {
            if (use_type == player.USE_TYPE.USING_LEFT_CLICK)
            {
                if (food_values != null)
                {
                    // Eat
                    player.inventory.remove(this, 1);
                    player.modify_hunger(food_values.metabolic_value());
                    player.play_sound("sounds/munch1", 0.99f, 1.01f, 0.5f);
                    foreach (var p in GetComponents<product>())
                        p.create_in(player.inventory);
                }
            }
            else if (use_type == player.USE_TYPE.USING_RIGHT_CLICK)
            {
                // Place this item on the gutter
                var gutter = gutter_to_place_on(player, out RaycastHit hit);
                if (gutter != null)
                {
                    var created = item.create(name, hit.point, Quaternion.identity, logistics_version: true);
                    gutter.add_item(created);
                    player.inventory.remove(this, 1);
                }
            }
        }

        return use_result.complete;
    }

    item_gutter gutter_to_place_on(player p, out RaycastHit hit)
    {
        var ray = p.camera_ray(player.INTERACTION_RANGE, out float dis);
        return utils.raycast_for_closest<item_gutter>(ray, out hit, dis);
    }

    public virtual string equipped_context_tip()
    {
        string ret = "";
        if (food_values != null) ret += "Left click to eat";
        var gut = gutter_to_place_on(player.current, out RaycastHit hit);
        if (gut != null) ret += "\nRight click to place on gutter";
        if (ret.Length == 0) return null;
        return ret;
    }

    public virtual use_result on_use_continue(player.USE_TYPE use_type, player player) { return use_result.complete; }
    public virtual void on_use_end(player.USE_TYPE use_type, player player) { }
    public virtual bool allow_left_click_held_down() { return false; }
    public virtual bool allow_right_click_held_down() { return false; }

    /// <summary> Called when this item is equipped.</summary>
    public virtual void on_equip(player player)
    {
        // Remove all colliders
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        // Make it invisible.
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // Destroy non-equippable things
        foreach (Component eq in GetComponentsInChildren<INonEquipable>())
        {
            if (eq is INonEquipableCallback)
                ((INonEquipableCallback)eq).on_equip_callback(player);
            Destroy(eq);
        }
    }

    /// <summary> Called when this item is unequipped. <paramref name="local_player"/> = false
    /// iff this item is being unequipped by a remote player. </summary>
    public virtual void on_unequip(player player) { }

    public virtual Dictionary<string, int> add_to_inventory_on_pickup()
    {
        var ret = new Dictionary<string, int>();
        ret[name] = 1;
        return ret;
    }

    public void pick_up(bool register_undo = false)
    {
        if (this == null) return;

        if (!can_pick_up(out string message))
        {
            popup_message.create("Cannot pick up " + display_name + ": " + message);
            return;
        }

        var undo = pickup_undo();

        // Delete the object on the network / add it to
        // inventory only if succesfully deleted on the
        // server. This stops two clients from simultaneously
        // deleting an object to duplicate it.
        var to_pickup = add_to_inventory_on_pickup();
        delete(() =>
        {
            // Add the products from pickup into inventory
            foreach (var kv in to_pickup)
                player.current.inventory.add(kv.Key, kv.Value);

            if (register_undo)
                undo_manager.register_undo_level(undo);
        });
    }

    public undo_manager.undo_action pickup_undo()
    {
        if (this == null) return null; // Destroyed

        // Copies for lambda
        var pickup_items = add_to_inventory_on_pickup();
        string name_copy = string.Copy(name);
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        networked parent = transform.parent?.GetComponent<networked>();

        return () =>
        {
            // Check we still have all of the products
            foreach (var kv in pickup_items)
                if (!player.current.inventory.contains(kv.Key, kv.Value))
                    return null;

            // Remove all of the products
            foreach (var kv in pickup_items)
                if (!player.current.inventory.remove(kv.Key, kv.Value))
                    throw new System.Exception("Tried to remove non-existant item!");

            // Recreate the building
            var created = create(name_copy, pos, rot, networked: true, parent);

            // Return the redo function
            return () =>
            {
                // Redo the pickup, and return the redo-undo (yo, what)
                created.pick_up();
                return created.pickup_undo();
            };
        };
    }

    protected virtual bool can_pick_up(out string message)
    {
        message = null;
        return true;
    }

    //############//
    // NETWORKING //
    //############//

    public networked_variables.net_quaternion networked_rotation;

    public override void on_init_network_variables()
    {
        // Create newtorked variables
        networked_rotation = new networked_variables.net_quaternion();
        transform.rotation = Quaternion.identity;
        networked_rotation.on_change = () => transform.rotation = networked_rotation.value;
    }

    public override void on_create()
    {
        // Initialize networked variables
        networked_rotation.value = transform.rotation;
    }

    //##############//
    // IInspectable //
    //##############//

    public virtual string inspect_info()
    {
        return item_quantity_info(this, 1);
    }

    public virtual Sprite main_sprite() { return sprite; }
    public virtual Sprite secondary_sprite() { return null; }

    //################//
    // STATIC METHODS //
    //################//

    /// <summary> Create an item. </summary>
    public static item create(string name,
        Vector3 position, Quaternion rotation,
        bool networked = false,
        networked network_parent = null,
        bool register_undo = false,
        bool logistics_version = false)
    {
        item item = null;

        if (networked)
        {
            // Create a networked version of the chosen item
            item = (item)client.create(position, "items/" + name,
                rotation: rotation, parent: network_parent);

            if (register_undo)
                undo_manager.register_undo_level(() =>
                {

                    if (item == null) return null;
                    var redo = item.pickup_undo();
                    item.pick_up();
                    return redo;
                });

        }
        else
        {
            // Create a client-side only version of the item
            item = Resources.Load<item>("items/" + name);
            if (item == null)
                throw new System.Exception("Could not find the item: " + name);
            item = item.inst();
            item.is_client_side = true;
            item.transform.position = position;
            item.transform.rotation = rotation;
            item.transform.SetParent(network_parent == null ? null : network_parent.transform);

            if (logistics_version)
                item.make_logistics_version();
        }

        return item;
    }

    public static string item_quantity_info(item item, int quantity)
    {
        if (item == null || quantity == 0)
            return "No item.";

        // Title
        string info = (quantity < 2 ? item.display_name :
            (utils.int_to_comma_string(quantity) + " " + item.plural)) + "\n";

        // Value
        if (quantity > 1)
            info += "  Value : " + (item.value * quantity).qs() + " (" + item.value.qs() + " each)\n";
        else
            info += "  Value : " + item.value.qs() + "\n";

        // Tool type + quality
        if (item is tool)
        {
            var t = (tool)item;
            info += "  Tool type : " + tool.type_to_name(t.type) + "\n";
            info += "  Quality : " + tool.quality_to_name(t.quality) + "\n";
        }

        // Melee weapon info
        if (item is melee_weapon)
        {
            var m = (melee_weapon)item;
            info += "  Melee damage : " + m.damage + "\n";
        }

        // Can this item be built with
        if (item is building_material)
            info += "  Can be used for building\n";

        // Fuel value
        if (item.fuel_value > 0)
        {
            if (quantity > 1)
                info += "  Fuel value : " + (item.fuel_value * quantity).qs() + " (" + item.fuel_value.qs() + " each)\n";
            else
                info += "  Fuel value : " + item.fuel_value.qs() + "\n";
        }

        // Food value
        if (item.food_values != null)
        {
            int mv = item.food_values.metabolic_value();
            if (quantity > 1)
                info += "  Food value (metabolic): " + (mv * quantity).qs() + " (" + mv.qs() + " each)\n";
            else
                info += "  Food value (metabolic): " + mv.qs() + "\n";
        }

        return utils.allign_colons(info);
    }
}