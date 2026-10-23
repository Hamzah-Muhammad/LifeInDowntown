using System.Collections.Generic;
using LifeInDowntown.Items;
using LifeInDowntown.Player;
using UnityEngine;

namespace LifeInDowntown.NPC
{
    // A person Tarik has punched (docs/DESIGN.md 3P.3). Added by Pedestrian.Punch on the first hit.
    //   a jab or cross - a short stagger back
    //   a heavy hook   - knocked down: a ragdoll for 3 s, then back on their feet (unless that finished them)
    // Who fights back: corner crews at once, anyone else on the second punch (police, clerks and story people
    // don't). A fighter squares up, steps in to arm's length and swings every 1.1-1.6 s with IK-driven arms
    // (a TarikBody of their own); a swing that lands does 6-10 to Tarik, 70% less if he's blocking. Hurt
    // below 35 they give up and run. Step(dt) is the whole update, so tests can drive it.
    public class NpcMelee : MonoBehaviour
    {
        public const float Reach = 1.25f, StandOff = 0.95f, KnockdownSeconds = 3f, GiveUpBelow = 35f;
        public static readonly List<NpcMelee> Fighters = new List<NpcMelee>();

        public bool Fighting { get; private set; }
        public bool Down { get; private set; }
        public int Punched { get; private set; }
        public int SwingsLanded { get; private set; }

        Pedestrian ped;
        Animator anim;
        TarikBody arms;
        Transform fistR, fistL, hintR, hintL;
        float staggerT, downT, swingT = -1f, nextSwing = 0.8f, armsW;
        Vector3 staggerDir;
        static readonly int ModeId = Animator.StringToHash("Mode");
        const float SwingSeconds = 0.5f, SwingLands = 0.3f;

        void Init()
        {
            if (ped != null) return;
            ped = GetComponent<Pedestrian>();
            anim = GetComponentInChildren<Animator>();
        }

        public static bool AnyFighterNear(Vector3 at, float range)
        {
            foreach (var f in Fighters)
                if (f != null && f.Fighting && !f.Down && (f.transform.position - at).sqrMagnitude < range * range) return true;
            return false;
        }

        public void TakePunch(float amount, Vector3 point, Vector3 dir, bool heavy)
        {
            Init();
            dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : -transform.forward;
            Punched++;
            ped.health -= amount;
            if (ped.health <= 0f) { Stop(); ped.Kill(point, dir * 12f); return; }
            if (heavy) KnockDown(point, dir);
            else { staggerT = 0.35f; staggerDir = dir; swingT = -1f; nextSwing = Mathf.Max(nextSwing, 0.6f); }

            if (ped.role == Pedestrian.Role.Police) { ped.Approach(Tarik() != null ? Tarik().position : point, 3f); return; }
            if (ped.role == Pedestrian.Role.Clerk || ped.role == Pedestrian.Role.Story) { if (!Down) ped.Flee(point - dir * 2f); return; }
            if (Fighting && ped.health < GiveUpBelow) { GiveUp(point - dir * 2f); return; }
            if (!Fighting && (ped.role == Pedestrian.Role.Corner || Punched >= 2) && ped.health >= GiveUpBelow) StartFight();
            else if (!Fighting) { ped.path = null; ped.mode = Pedestrian.Mode.LookAround; ped.watchPlayer = true; ped.watchRange = 10f; SetMode(ped.mode); }   // "Oi!": stops and stares
        }

        void KnockDown(Vector3 point, Vector3 dir)
        {
            Down = true; downT = KnockdownSeconds; swingT = -1f; staggerT = 0f;
            armsW = 0f; if (arms != null) arms.rightWeight = arms.leftWeight = 0f;
            ped.enabled = false;
            Ragdoll.Go(gameObject, point, dir * 26f + Vector3.up * 4f);
        }

        void StartFight()
        {
            Fighting = true;
            if (!Fighters.Contains(this)) Fighters.Add(this);
            ped.path = null; ped.watchPlayer = false; ped.mode = Pedestrian.Mode.Idle;
            if (arms == null)
            {
                arms = gameObject.AddComponent<TarikBody>();
                arms.anim = anim;
                fistR = Point("FistR"); fistL = Point("FistL"); hintR = Point("HintR"); hintL = Point("HintL");
                arms.rightTarget = fistR; arms.leftTarget = fistL; arms.rightHint = hintR; arms.leftHint = hintL;
                arms.rightCurl = arms.leftCurl = 1f;
            }
        }

        void GiveUp(Vector3 from)
        {
            Stop();
            if (!Down) ped.Flee(from);
        }

        void Stop()
        {
            Fighting = false; swingT = -1f;
            Fighters.Remove(this);
            if (arms != null) arms.rightWeight = arms.leftWeight = 0f;
        }

        Transform Point(string n) { var t = new GameObject(n).transform; t.SetParent(transform, false); return t; }

        static Transform Tarik() => Pedestrian.Player != null ? Pedestrian.Player : GameObject.Find("Player")?.transform;

        void Update() => Step(Time.deltaTime);

        public void Step(float dt)
        {
            Init();
            if (ped == null || ped.Dead) { Stop(); return; }
            if (Down)
            {
                downT -= dt;
                if (downT > 0f) return;
                Down = false;
                Ragdoll.Recover(gameObject);
                ped.enabled = true;
                ped.mode = Fighting ? Pedestrian.Mode.Idle : Pedestrian.Mode.Nervous; ped.path = null;
                SetMode(ped.mode);
                if (!Fighting) return;
            }
            if (staggerT > 0f)
            {
                transform.position += staggerDir * (staggerT / 0.35f * 2.2f * dt);
                staggerT -= dt;
            }
            if (!Fighting) return;

            var tarik = Tarik();
            if (tarik == null) return;
            Vector3 to = tarik.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 25f) { Stop(); ped.mode = Pedestrian.Mode.LookAround; SetMode(ped.mode); return; }   // he walked off
            if (to.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * dt);
            bool closing = dist > StandOff + 0.1f && staggerT <= 0f && swingT < 0f;
            if (closing) transform.position += to.normalized * Mathf.Min(1.6f * dt, dist - StandOff);
            SetMode(closing ? Pedestrian.Mode.Walk : Pedestrian.Mode.Idle);

            // the swing: wind up, throw at his head, land (or whiff) at 0.3 s, back to guard
            nextSwing -= dt;
            if (swingT < 0f && nextSwing <= 0f && dist < Reach + 0.4f && staggerT <= 0f) { swingT = 0f; nextSwing = Random.Range(1.1f, 1.6f); }
            if (swingT >= 0f)
            {
                float before = swingT;
                swingT += dt;
                if (before < SwingLands && swingT >= SwingLands) Land(tarik, dist, to);
                if (swingT >= SwingSeconds) swingT = -1f;
            }
            Arms(dt, tarik);
        }

        void Land(Transform tarik, float dist, Vector3 to)
        {
            if (dist > Reach || Vector3.Angle(transform.forward, to) > 50f) return;
            var stats = PlayerStats.Instance;
            if (stats == null) return;
            float dmg = PlayerStats.PunchDamage();
            if (Fists.Instance != null && Fists.Instance.Blocking) dmg *= 0.3f;
            stats.TakeDamage(dmg, transform.position);
            SwingsLanded++;
            Fists.Instance?.Struck(transform.position, dmg);
        }

        // guard up in front of the face; the right hand drives at Tarik's head during a swing
        void Arms(float dt, Transform tarik)
        {
            if (arms == null) return;
            armsW = Mathf.MoveTowards(armsW, Down ? 0f : 1f, dt / 0.15f);
            arms.rightWeight = arms.leftWeight = armsW;
            Vector3 guardR = new Vector3(0.14f, 1.42f, 0.3f), guardL = new Vector3(-0.13f, 1.45f, 0.28f);
            Quaternion guardRot = Quaternion.Euler(-30f, 0f, 0f);
            fistL.localPosition = guardL; fistL.localRotation = guardRot;
            hintR.localPosition = new Vector3(0.45f, 1.0f, -0.1f); hintL.localPosition = new Vector3(-0.45f, 1.0f, -0.1f);
            float k = swingT < 0f ? 0f : swingT < 0.12f ? -0.4f * (swingT / 0.12f) : swingT < SwingLands ? Mathf.SmoothStep(-0.4f, 1f, (swingT - 0.12f) / (SwingLands - 0.12f)) : 1f - Mathf.SmoothStep(0f, 1f, (swingT - SwingLands) / (SwingSeconds - SwingLands));
            Vector3 head = transform.InverseTransformPoint(tarik.position + Vector3.up * 1.6f);
            Vector3 strike = Vector3.ClampMagnitude(head - new Vector3(0.18f, 1.4f, 0f), 0.62f) + new Vector3(0.18f, 1.4f, 0f);
            fistR.localPosition = k >= 0f ? Vector3.Lerp(guardR, strike, k) : guardR + new Vector3(0.06f, 0f, -0.12f) * -k / 0.4f;
            fistR.localRotation = Quaternion.Slerp(guardRot, Quaternion.Euler(-5f, 0f, 90f), Mathf.Clamp01(k));
        }

        void SetMode(Pedestrian.Mode m) { if (anim != null && anim.isActiveAndEnabled) anim.SetInteger(ModeId, (int)m); }

        void OnDisable() { if (!Down) Fighters.Remove(this); }
    }
}
