using System.Collections.Generic;
using System.IO;
using LifeInDowntown.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeInDowntown.EditorTools
{
    // Renders named camera poses of the city scene to Screenshots/<pose>.png (docs/DESIGN.md 6).
    // Run with -batchmode but WITHOUT -nographics so HDRP renders on the GPU.
    public static class ShotTool
    {
        const string ScenePath = "Assets/Scenes/Downtown_YongeDundas.unity";
        const string DataPath = "Assets/City/Data/downtown_yonge_dundas.json";
        const int Width = 1600, Height = 900, WarmupFrames = 24;

        [MenuItem("Life In Downtown/Render Screenshots")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var data = JsonUtility.FromJson<CityData>(File.ReadAllText(DataPath));
            Directory.CreateDirectory("Screenshots");

            var poses = new List<(string name, Vector3 pos, Vector3 target)>
            {
                ("overhead", new Vector3(0, 420, -330), new Vector3(0, 0, -20)),
                ("aerial_northwest", new Vector3(-260, 160, -260), new Vector3(0, 30, 0)),
            };
            AddStreetPose(poses, data, "Yonge Street", "street_yonge_looking_north", southToNorth: true);
            AddStreetPose(poses, data, "Dundas Street East", "street_dundas_looking_west", southToNorth: false);
            poses.Add(("intersection_yonge_dundas", new Vector3(-22f, 4.5f, -26f), new Vector3(4f, 0f, 2f)));      // crosswalks, signals
            var shopSites = CityGenerator.ShopSites(data);
            if (shopSites.Count > 0)
            {
                var shop = shopSites[0];
                poses.Add(("rabba_outside", shop.W(-1.5f, -7f, 1.7f), shop.W(0f, 0f, 2.6f)));
                poses.Add(("rabba_inside", shop.W(-3.2f, 0.8f, 1.65f), shop.W(1.2f, 9.5f, 0.9f)));
            }
            if (CityGenerator.TryCondoFrame(data, out var condo))
            {
                poses.Add(("tower_balconies", condo.outwardGround - condo.inward * 45f + condo.along * 20f + Vector3.up * 1.7f,
                           condo.outwardGround + Vector3.up * 70f));          // looking up at Pantages Tower from Shuter St
                // from the living room out through the glass, and from the window back into the room
                poses.Add(("condo_window_view", condo.W(-1.2f, 4.6f, 1.6f), condo.W(-0.6f, -40f, 0.4f)));
                poses.Add(("condo_room", condo.W(0.2f, 0.5f, 1.65f), condo.W(-1.6f, 7.5f, 1.0f)));
                poses.Add(("condo_cn_tower_view", condo.W(-1.0f, 1.2f, 1.6f), new Vector3(-499f, 280f, -1514f)));   // toward the real CN Tower
                poses.Add(("condo_topdown", condo.W(0f, 3.75f, 11f) + condo.inward * 0.01f, condo.W(0f, 3.75f, 0f)));    // layout check, ceiling hidden
            }
            var bodycam = GameObject.Find("BodycamCamera");
            if (bodycam != null)   // what the player sees at spawn: chest height, wide lens
            {
                poses.Add(("bodycam_spawn", bodycam.transform.position, bodycam.transform.position + bodycam.transform.forward * 50f));
                poses.Add(("bodycam_pistol_drawn", bodycam.transform.position, bodycam.transform.position + bodycam.transform.forward * 50f));   // DESIGN 3M
            }

            AddCrowdPoses(poses, CityGenerator.TryCondoFrame(data, out var cf) ? cf : (CityGenerator.CondoFrame?)null);
            foreach (var (shot, id) in new[] { ("items_nightstand", "condo_pistol"), ("items_coffee_table", "condo_chips") })   // DESIGN 3M
            {
                var pick = GameObject.Find("Pickups/Pickup_" + id);
                if (pick != null) poses.Add((shot, pick.transform.position + new Vector3(0.9f, 0.75f, 0.6f), pick.transform.position));
            }
            // -shots a,b,c renders only those poses
            var only = LifeInDowntown.Game.GameFlow.Arg("-shots");
            if (only != null) poses = poses.FindAll(p => System.Array.IndexOf(only.Split(','), p.name) >= 0);

            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 60f;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;

            Debug.Log($"SHOT_GFX {SystemInfo.graphicsDeviceType} rayTracing={SystemInfo.supportsRayTracing}");
            var rain = GameObject.Find("Rain");

            foreach (var (name, pos, target) in poses)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                cam.fieldOfView = name.StartsWith("bodycam") ? PlayerSetup.VerticalFov : 60f;
                foreach (var hide in new[] { "CondoCeiling" })
                {
                    var hidden = GameObject.Find(hide);
                    if (hidden != null) hidden.GetComponent<Renderer>().enabled = name != "condo_topdown";
                }
                // the overhead view also clears the tower roof above the condo
                var tower = GameObject.Find("Bldg_" + CityGenerator.CondoTower);
                if (tower != null) tower.GetComponent<Renderer>().enabled = name != "condo_topdown";
                if (rain != null)
                {
                    // the rain box stands on the ground around the camera (useless high up, so aerials skip it)
                    rain.SetActive(pos.y < 30f);
                    rain.transform.position = new Vector3(pos.x, 0f, pos.z);
                }
                var preview = PreviewCrowd(pos);
                if (name == "bodycam_pistol_drawn")   // the held pistol where WeaponHolder puts it, in camera space
                {
                    var holder = Object.FindFirstObjectByType<LifeInDowntown.Items.WeaponHolder>();
                    if (holder != null && holder.gunModel != null)
                    {
                        var held = Object.Instantiate(holder.gunModel, cam.transform, false);
                        held.transform.localPosition = holder.heldPosition;
                        held.transform.localRotation = Quaternion.Euler(holder.heldEuler);
                        preview.Add(held);
                    }
                }
                for (int i = 0; i < WarmupFrames; i++) Render(cam, rt);   // lets auto-exposure and sky settle
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string file = Path.Combine("Screenshots", name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                var stack = UnityEngine.Rendering.HighDefinition.HDCamera.GetOrCreate(cam).volumeStack;   // the camera's own stack
                var fogNow = stack.GetComponent<UnityEngine.Rendering.HighDefinition.Fog>();
                var expNow = stack.GetComponent<UnityEngine.Rendering.HighDefinition.Exposure>();
                Debug.Log($"SHOT_OK {file} pos={pos} fog={fogNow.enabled.value}/{fogNow.meanFreePath.value:0} exposureEV={expNow.fixedExposure.value:0.0} crowd={preview.Count}");
                foreach (var w in preview) Object.DestroyImmediate(w);
            }
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(go);
            rt.Release();
        }

        // close-ups of the placed street people (docs/DESIGN.md 3L) and the street outside the condo lobby
        static void AddCrowdPoses(List<(string, Vector3, Vector3)> poses, CityGenerator.CondoFrame? frame)
        {
            var crowd = Object.FindFirstObjectByType<LifeInDowntown.NPC.CrowdDirector>();
            if (crowd == null) return;
            if (frame is CityGenerator.CondoFrame condo)
                poses.Add(("crowd_condo_street", condo.outwardGround - condo.inward * 3f + condo.along * 4f + Vector3.up * 1.6f,
                           condo.outwardGround - condo.inward * 3f + condo.along * 60f + Vector3.up * 1.2f));
            LifeInDowntown.NPC.Pedestrian corner = null, homeless = null, sitter = null;
            foreach (var p in crowd.statics)
            {
                if (sitter == null && (p.mode == LifeInDowntown.NPC.Pedestrian.Mode.Sit || p.mode == LifeInDowntown.NPC.Pedestrian.Mode.SitLook)) sitter = p;
                if (corner == null && p.watchPlayer) corner = p;
                if (homeless == null && p.transform.parent != null && p.transform.parent.name == "Homeless") homeless = p;
            }
            void Face(string name, Transform t, float dist, float side)
            {
                var eye = t.position + t.forward * dist + t.right * side + Vector3.up * 1.55f;
                poses.Add((name, eye, t.position + Vector3.up * 0.8f));
            }
            if (corner != null) Face("crowd_corner_crew", corner.transform, 6f, 2f);
            if (homeless != null) Face("crowd_homeless", homeless.transform, 3.5f, 1.2f);
            if (sitter != null) Face("crowd_sitter", sitter.transform, 3.2f, -1.2f);
            var cop = System.Array.Find(crowd.statics, p => p.name.StartsWith("Police_"));
            if (cop != null) Face("crowd_police", cop.transform, 4f, 1.5f);
            Transform sleeper = null;
            foreach (var t in crowd.GetComponentsInChildren<Transform>()) if (t.name == "SleepingBag") { sleeper = t; break; }
            if (sleeper != null) Face("crowd_sleeper", sleeper.transform.parent, 3.2f, 1.5f);
        }

        // what the runtime crowd looks like around a camera: fill the walker pool on the baked sidewalk lines
        // (same ring and street weights as CrowdDirector) and pose every person on a frame of their clip
        static List<GameObject> PreviewCrowd(Vector3 at)
        {
            var made = new List<GameObject>();
            var crowd = Object.FindFirstObjectByType<LifeInDowntown.NPC.CrowdDirector>();
            if (crowd == null) return made;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            var rnd = new System.Random(5);
            if (at.y < 30f)
            {
                var near = new List<int>(); var cum = new List<float>(); float total = 0f;
                int line = 0;
                for (int i = 0; i < crowd.points.Length; i++)
                {
                    while (line + 1 < crowd.lineStart.Length && crowd.lineStart[line + 1] <= i) line++;
                    var d = crowd.points[i] - at; d.y = 0f;
                    if (d.magnitude < 3f || d.magnitude > crowd.spawnMax) continue;
                    near.Add(i); total += crowd.lineWeight[line]; cum.Add(total);
                }
                for (int n = 0; n < crowd.walkers && near.Count > 0; n++)
                {
                    float x = (float)rnd.NextDouble() * total;
                    int k = cum.BinarySearch(x); if (k < 0) k = ~k; k = Mathf.Min(k, near.Count - 1);
                    int i = near[k];
                    var prefab = crowd.walkerPrefabs[rnd.Next(crowd.walkerPrefabs.Length)];
                    var w = (GameObject)Object.Instantiate(prefab);
                    var next = crowd.points[Mathf.Min(i + 1, crowd.points.Length - 1)] - crowd.points[i]; next.y = 0f;
                    if (rnd.NextDouble() < 0.5) next = -next;
                    w.transform.SetPositionAndRotation(crowd.points[i] + new Vector3(next.z, 0, -next.x).normalized * ((float)rnd.NextDouble() - 0.5f) * 1.2f,
                                                       next.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(next) : Quaternion.identity);
                    made.Add(w);
                }
            }
            foreach (var p in Object.FindObjectsByType<LifeInDowntown.NPC.Pedestrian>(FindObjectsSortMode.None))
            {
                var anim = p.GetComponentInChildren<Animator>();
                var ctrl = anim != null ? anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
                if (ctrl == null) continue;
                foreach (var st in ctrl.layers[0].stateMachine.states)
                    if (st.state.name == p.mode.ToString() && st.state.motion is AnimationClip clip)
                    {
                        var root = p.transform.position; var rot = p.transform.rotation;
                        AnimationMode.SampleAnimationClip(anim.gameObject, clip, (float)rnd.NextDouble() * clip.length);
                        p.transform.SetPositionAndRotation(root, rot);   // clips carry travel; keep the body where it was placed
                        anim.transform.localPosition = Vector3.zero; anim.transform.localRotation = Quaternion.identity;
                    }
            }
            return made;
        }

        static void Render(Camera cam, RenderTexture rt)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }

        // Eye-level pose on the right-hand sidewalk of the named street, standing on a real point of the
        // street ~70 m from the Yonge & Dundas origin (south of it for north views, east of it for west views)
        // and looking back along the street through the intersection. Streets bend, so no straight-line guess.
        static void AddStreetPose(List<(string, Vector3, Vector3)> poses, CityData data, string street, string name, bool southToNorth)
        {
            Vector2 eyeOnRoad = Vector2.zero;
            float bestErr = float.MaxValue, roadWidth = 10f;
            foreach (var r in data.roads)
            {
                if (r.name != street) continue;
                foreach (var v in CityDataUtil.Points(r.pts))
                {
                    bool behind = southToNorth ? v.y < 0f : v.x > 0f;
                    float err = Mathf.Abs(v.magnitude - 70f);
                    if (behind && err < bestErr) { bestErr = err; eyeOnRoad = v; roadWidth = r.width; }
                }
            }
            if (bestErr == float.MaxValue) { Debug.LogWarning($"SHOT_SKIP no road named {street}"); return; }
            Vector2 dir = (-eyeOnRoad).normalized;   // towards the intersection
            // 2.5 m past the curb: streetlight poles stand 0.6 m past it
            Vector2 eye = eyeOnRoad + new Vector2(dir.y, -dir.x) * (roadWidth * 0.5f + 2.5f);
            Vector2 target = eye + dir * 150f;
            poses.Add((name, new Vector3(eye.x, 1.7f, eye.y), new Vector3(target.x, 8f, target.y)));
        }
    }
}
