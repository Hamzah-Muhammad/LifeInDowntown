using LifeInDowntown.City;
using LifeInDowntown.World;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Things to pick up in Tarik's condo (docs/DESIGN.md 3M): a water and a bag of chips on the coffee
    // table, a pistol and a box of rounds on the bedroom nightstand. Each has a fixed pickup id, so once
    // taken it stays gone across saves. Called by CityGenerator.Generate after the condo is furnished.
    public static class ItemSetup
    {
        public static void Setup()
        {
            var old = GameObject.Find("Pickups");
            if (old != null) Object.DestroyImmediate(old);
            var condo = GameObject.Find("Condo");
            if (condo == null) { Debug.LogWarning("ITEMS_SKIP no condo"); return; }
            var root = new GameObject("Pickups").transform;

            var bottle = CityGenerator.Materials.Plain("Item_WaterBottle", new Color(0.55f, 0.75f, 0.95f), 0.85f, 0f, Color.black, 0f);
            var chips = CityGenerator.Materials.Plain("Item_ChipsBag", new Color(0.75f, 0.08f, 0.1f), 0.6f, 0.3f, Color.black, 0f);
            var ammoBox = CityGenerator.Materials.Plain("Item_AmmoBox", new Color(0.62f, 0.5f, 0.24f), 0.3f, 0f, Color.black, 0f);
            var slide = CityGenerator.Materials.Plain("Item_PistolSlide", new Color(0.07f, 0.07f, 0.075f), 0.55f, 0.2f, Color.black, 0f);
            var frame = CityGenerator.Materials.Plain("Item_PistolFrame", new Color(0.045f, 0.045f, 0.05f), 0.3f, 0.2f, Color.black, 0f);

            int placed = 0;
            if (Top(condo.transform, "modern_coffee_table_01", out var table, out var tableTop))
            {
                var right = table.right; var fwd = table.forward;
                Pickup(root, "water", 1, "condo_water", Prim(PrimitiveType.Cylinder, bottle, new Vector3(0.065f, 0.1f, 0.065f)),
                               tableTop + right * 0.22f + Vector3.up * 0.1f, table.rotation);
                Pickup(root, "ketchup_chips", 1, "condo_chips", Prim(PrimitiveType.Cube, chips, new Vector3(0.2f, 0.05f, 0.28f)),
                               tableTop - right * 0.2f + fwd * 0.05f + Vector3.up * 0.025f, table.rotation * Quaternion.Euler(0f, 25f, 0f));
                placed += 2;
            }
            if (Top(condo.transform, "painted_wooden_nightstand", out var stand, out var standTop))
            {
                // the officer's pistol (PistolImport) if imported, else the box stand-in with saved materials
                var real = AssetDatabase.LoadAssetAtPath<GameObject>(PistolImport.PrefabPath);
                var gun = real != null ? (GameObject)PrefabUtility.InstantiatePrefab(real) : MinniGameFramework.Combat.WeaponHolder.StandIn();
                if (real == null)
                    foreach (var r in gun.GetComponentsInChildren<Renderer>()) r.sharedMaterial = r.name.StartsWith("Slide") || r.name.StartsWith("Sight") ? slide : frame;
                Pickup(root, "pistol_9mm", 1, "condo_pistol", gun, standTop + Vector3.up * 0.015f, stand.rotation * Quaternion.Euler(0f, 0f, 90f));   // lying on its side
                Pickup(root, "ammo_9mm", 30, "condo_ammo", Prim(PrimitiveType.Cube, ammoBox, new Vector3(0.12f, 0.04f, 0.08f)),
                       standTop + stand.right * 0.12f + Vector3.up * 0.02f, stand.rotation);
                placed += 2;
            }
            Debug.Log($"ITEMS_OK condoPickups={placed}");
        }

        // the furniture piece's transform and the centre of its top surface
        static bool Top(Transform condo, string name, out Transform t, out Vector3 top)
        {
            t = null; top = Vector3.zero;
            foreach (Transform child in condo)
            {
                if (child.name != name) continue;
                var b = CondoFurnitureImport.Bounds(child.gameObject);
                t = child; top = new Vector3(b.center.x, b.max.y, b.center.z);
                return true;
            }
            Debug.LogWarning($"ITEMS_WARN no {name} in the condo");
            return false;
        }

        static GameObject Prim(PrimitiveType type, Material m, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static void Pickup(Transform parent, string item, int count, string id, GameObject visual, Vector3 pos, Quaternion rot)
        {
            var holder = new GameObject("Pickup_" + id);
            holder.transform.SetParent(parent, false);
            holder.transform.SetPositionAndRotation(pos, rot);
            visual.transform.SetParent(holder.transform, false);
            visual.transform.localPosition = Vector3.zero;
            var p = holder.AddComponent<WorldPickup>();
            p.itemId = item; p.count = count; p.pickupId = id; p.radius = 1.4f;
        }
    }
}
