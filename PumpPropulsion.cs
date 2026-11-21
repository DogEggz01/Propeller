using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Crest;
using HarmonyLib;
using UnityEngine;

namespace JetPump
{
    // Thrust response, water immersion, bow-up trim and wheel throttle input.

    // Transient engine response, separate from the saved throttle command.
    internal sealed class PumpDrive
    {
        private float output;
        internal void Stop() { output = 0f; }
        internal void Apply(Rigidbody body, Vector3 direction, Vector3 point, float command, float dt, PumpTrim trim, float ratedThrust)
        {
            if (command <= 0f) { Stop(); return; }
            output = Mathf.MoveTowards(output, command, 100f * dt / 1.5f);
            Vector3 force = direction * (ratedThrust * output / 100f);
            body.AddForceAtPosition(force, point, ForceMode.Force);
            trim.AddThrust(force / body.mass, point);
        }
    }

    internal sealed class PumpWater
    {
        // Retain positions from each GPU query: its result arrives in a later frame.
        private static readonly FieldInfo ResultSegments = AccessTools.Field(typeof(QueryBase), "_resultSegments");
        private const int ProbeCount = 9, History = 32;
        private readonly PumpItem owner;
        private readonly Vector3[] localPoints = new Vector3[ProbeCount], queries = new Vector3[ProbeCount];
        private readonly Vector3[] displacement = new Vector3[ProbeCount], normals = new Vector3[ProbeCount];
        private readonly Vector3[] sampledPoints = new Vector3[ProbeCount], historyPoints = new Vector3[ProbeCount * History];
        private readonly int[] historyFrames = new int[History];
        private readonly float[] historyTimes = new float[History], historyOffsets = new float[History], heights = new float[ProbeCount];
        private readonly int queryId;
        private ICollProvider provider;
        private float sampledAt, sampledOffset;
        private bool hasSample;
        internal bool Submerged { get; private set; }
        internal bool SampleValid { get; private set; }

        internal PumpWater(PumpItem item, Transform mouth)
        {
            owner = item; queryId = item.GetInstanceID();
            localPoints[0] = item.transform.InverseTransformPoint(mouth.position);
            var rim = item.GetComponentsInChildren<Transform>(true);
            Transform rimAnchor = Array.Find(rim, t => t.name == "WaterRim");
            float radius = Vector3.Distance(mouth.position, rimAnchor.position);
            for (int i = 1; i < ProbeCount; i++)
            {
                float angle = (i - 1) * Mathf.PI / 4f;
                localPoints[i] = item.transform.InverseTransformPoint(mouth.position + radius * (mouth.right * Mathf.Cos(angle) + mouth.up * Mathf.Sin(angle)));
            }
            for (int i = 0; i < History; i++) historyFrames[i] = -1;
            Reset();
        }
        private static bool Finite(Vector3 v) => PumpRecord.Finite(v.x) && PumpRecord.Finite(v.y) && PumpRecord.Finite(v.z);
        internal Vector3 WorldPoint(int index)
        {
            var item = owner.Item;
            return PumpItem.ConvertPoint(item.itemRigidbodyC.transform.TransformPoint(localPoints[index]), item.currentWalkCol, item.currentActualBoat) + owner.SpatialOffset;
        }
        internal void Sample()
        {
            var ocean = OceanRenderer.Instance;
            var current = ocean ? ocean.CollisionProvider : null;
            if (provider != current) { hasSample = Submerged = false; provider = current; }
            if (provider == null) { hasSample = SampleValid = Submerged = false; return; }
            int slot = Time.frameCount % History;
            historyFrames[slot] = Time.frameCount; historyTimes[slot] = Time.unscaledTime; historyOffsets[slot] = Plugin.VerticalOffset;
            for (int i = 0; i < ProbeCount; i++) historyPoints[slot * ProbeCount + i] = queries[i] = WorldPoint(i);
            int status = provider.Query(queryId, .2f, queries, displacement, normals, null);
            hasSample = provider.RetrieveSucceeded(status);
            int resultSlot = slot;
            if (provider is QueryBase)
            {
                var segments = ResultSegments == null ? null : ResultSegments.GetValue(provider) as Dictionary<int, Vector3Int>;
                Vector3Int segment;
                if (segments == null || !segments.TryGetValue(queryId, out segment) || segment.z < 0) hasSample = false;
                else
                {
                    resultSlot = segment.z % History;
                    if (historyFrames[resultSlot] != segment.z) hasSample = false;
                }
            }
            sampledAt = historyTimes[resultSlot]; sampledOffset = historyOffsets[resultSlot];
            for (int i = 0; i < ProbeCount; i++)
            {
                sampledPoints[i] = historyPoints[resultSlot * ProbeCount + i];
                heights[i] = displacement[i].y + ocean.SeaLevel;
                hasSample &= PumpRecord.Finite(heights[i]) && Finite(normals[i]) && normals[i].y > .1f;
            }
            Evaluate();
        }
        internal void Evaluate()
        {
            var ocean = OceanRenderer.Instance;
            bool valid = hasSample && sampledOffset == Plugin.VerticalOffset && ocean && ocean.CollisionProvider == provider && Time.unscaledTime - sampledAt <= .25f;
            float minimumDepth = float.PositiveInfinity;
            for (int i = 0; i < ProbeCount; i++)
            {
                Vector3 point = WorldPoint(i);
                Vector3 travel = point - sampledPoints[i];
                // A tangent-plane correction is only meaningful near sampled X/Z.
                valid &= travel.x * travel.x + travel.z * travel.z <= 1f;
                float surface = heights[i] - (normals[i].x * travel.x + normals[i].z * travel.z) / Mathf.Max(.1f, normals[i].y);
                minimumDepth = Mathf.Min(minimumDepth, surface - point.y);
            }
            SampleValid = valid;
            Submerged = valid && minimumDepth > (Submerged ? 0f : .02f);
        }
        internal float Power(PumpRecord record) => !record.RequireImmersion || Submerged ? record.AppliedPower : 0f;

        internal void Reset() { hasSample = Submerged = SampleValid = false; }
    }

    // PumpItem submits each force in FixedUpdate. A later callback combines them
    // before simulation, so one boat gets one correction regardless of pump count.
    [DefaultExecutionOrder(1000)]
    internal sealed class PumpTrim : MonoBehaviour
    {
        private static readonly List<PumpTrim> live = new List<PumpTrim>();
        private Rigidbody body;
        private float forwardAcceleration, noseDownTorque, filteredPitch, filteredRate, blend;
        private bool initialized;
        private Vector3 forward, right;

        private void Awake() { body = GetComponent<Rigidbody>(); live.Add(this); }
        private void OnDestroy() { live.Remove(this); }
        internal static void ResetAll()
        {
            foreach (var trim in live) if (trim) { trim.enabled = false; Destroy(trim); }
            live.Clear();
        }
        internal void AddThrust(Vector3 acceleration, Vector3 point)
        {
            if (!body) body = GetComponent<Rigidbody>();
            // The starboard axis stays horizontal when the bow approaches 90
            // degrees. Projecting the bow itself loses the heading at vertical.
            right = Vector3.ProjectOnPlane(body.transform.right, Vector3.up).normalized;
            forward = Vector3.Cross(right, Vector3.up);
            float longitudinal = Vector3.Dot(acceleration, body.transform.forward);
            forwardAcceleration += longitudinal;
            // Counter only pitch from longitudinal thrust; lateral/vertical force
            // and all yaw/roll moments remain at the actual pump mounting point.
            noseDownTorque += Vector3.Dot(Vector3.Cross(point - body.worldCenterOfMass, body.transform.forward * longitudinal * body.mass), right);
        }
        private void FixedUpdate()
        {
            float drive = forwardAcceleration, pitchTorque = noseDownTorque;
            forwardAcceleration = noseDownTorque = 0f;
            if (!PumpWorld.SimulationAllowed || !body || body.isKinematic || drive <= .0001f ||
                (Plugin.TrimAngle <= 90f && Vector3.Dot(body.transform.up, Vector3.up) < -.01f) ||
                Vector3.ProjectOnPlane(body.transform.right, Vector3.up).sqrMagnitude < .04f)
            {
                initialized = false; blend = 0f;
                return;
            }
            float dt = Time.fixedDeltaTime;
            float pitch = Mathf.Atan2(Vector3.Dot(body.transform.forward, Vector3.up), Vector3.Dot(body.transform.forward, forward));
            // Targets above vertical explicitly permit inversion. Keep the angle
            // continuous when crossing +180/-180 instead of reversing the filter.
            if (Plugin.TrimAngle > 90f && pitch < -Mathf.PI * .5f) pitch += 2f * Mathf.PI;
            if (!initialized) { filteredPitch = pitch; filteredRate = 0f; initialized = true; }
            float alpha = 1f - Mathf.Exp(-dt / 2f);
            float previous = filteredPitch;
            filteredPitch += Mathf.DeltaAngle(filteredPitch * Mathf.Rad2Deg, pitch * Mathf.Rad2Deg) * Mathf.Deg2Rad * alpha;
            filteredRate = Mathf.Lerp(filteredRate, (filteredPitch - previous) / dt, alpha);
            blend = Mathf.MoveTowards(blend, Mathf.Clamp01(drive / 5f), dt / 2f);
            // Forward/up motion can continue through vertical; lateral drift does
            // not count toward the trim target. Reverse pumps still fail drive > 0.
            float speed = Vector3.ProjectOnPlane(body.velocity, right).magnitude;
            float target = Plugin.TrimAngle * Mathf.Deg2Rad * Mathf.SmoothStep(0f, 1f, speed / 3f);
            float correction = Mathf.Clamp((target - filteredPitch) * .8f - filteredRate * .9f, -.10f, .10f) * blend;
            // Torque in physical units cancels AddForceAtPosition's mass-scaled
            // moment. Convert the extra angular acceleration through the inertia
            // tensor; never change buoyancy, angular drag or the boat transform.
            Vector3 trimTorque = TorqueForAcceleration(body, -right * correction);
            body.AddTorque(-right * Mathf.Max(0f, pitchTorque) + trimTorque, ForceMode.Force);
        }
        internal static Vector3 TorqueForAcceleration(Rigidbody body, Vector3 acceleration)
        {
            Quaternion frame = body.rotation * body.inertiaTensorRotation;
            return frame * Vector3.Scale(body.inertiaTensor, Quaternion.Inverse(frame) * acceleration);
        }
    }

    internal static class PumpInput
    {
        private static readonly FieldInfo StickyWheelField = AccessTools.Field(typeof(GoPointer), "stickyClickedButton");
        private static readonly List<PumpRecord> records = new List<PumpRecord>();
        internal static bool Held(GoPointerButton helm) => helm.IsStickyClicked() || helm.IsCliked();
        // W/S on a held wheel or tiller drives every panel linked on that boat (a control group moves together).
        // Returns whether the held helm has wheel control.
        internal static bool HandleWheelInput(GoPointerButton helm, bool increase, bool decrease, float delta)
        {
            if (!PumpWorld.InputAllowed || !helm || !Held(helm)) return false;
            records.Clear();
            foreach (var box in PumpWorld.Linked(helm)) records.Add(box.Record);
            if (records.Count == 0) return false;
            PumpWorld.State.AdjustThrottle(records, increase, decrease, delta);
            return true;
        }
        internal static void PatchCheatspeed(Harmony harmony)
        {
            var type = PumpWorld.OptionalType("NANDCommand", "NANDCommand.CheatsPatch");
            if (type == null) return;
            // A NANDCommand whose patch changed costs only this guard, never Propeller.
            try
            {
                var method = AccessTools.Method(type, "Postfix", new[] { typeof(GoPointer) });
                if (method == null) throw new MissingMethodException("NANDCommand.CheatsPatch.Postfix(GoPointer) not found");
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(PumpInput), nameof(AllowCheatspeed)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("NANDCommand's cheat speed is not recognized by this Propeller version: on a wheel with wheel control it pushes the boat as well as the propellers. " + e.Message);
            }
        }
        private static bool AllowCheatspeed(GoPointer __0)
        {
            if (!Plugin.Ready || !__0) return true;
            // Only suppress NAND's propulsion when this pointer is holding a selected pump's wheel.
            var wheel = StickyWheelField.GetValue(__0) as GPButtonSteeringWheel;
            return !wheel || PumpWorld.Linked(wheel).Count == 0;
        }
    }

    // Dinghies (optional, never required): its cutter steers with TillerRudder, not the game's wheel. While wheel control
    // is on for the cutter, W/S on the tiller drives the throttle only; Dinghies' own W/S re-centring is undone. While it
    // is off, the tiller behaves exactly as Dinghies made it. Any mismatch drops tiller support only, never Propeller.
    internal static class PumpTiller
    {
        private static FieldInfo input, hinge, locked, sticky;
        private static ConfigEntry<bool> inverted;
        internal static void Patch(Harmony harmony)
        {
            var type = PumpWorld.OptionalType("Dinghies", "Dinghies.TillerRudder");
            if (type == null) return;
            try
            {
                var update = AccessTools.Method(type, "ExtraLateUpdate");
                var rudder = AccessTools.Field(type, "rudder");
                input = AccessTools.Field(type, "input");
                hinge = AccessTools.Field(type, "hingeJoint");
                locked = AccessTools.Field(type, "locked");
                sticky = AccessTools.Field(typeof(GoPointerButton), "stickyClickedBy");
                if (!typeof(GoPointerButton).IsAssignableFrom(type) || update == null || rudder?.FieldType != typeof(Rudder) || input?.FieldType != typeof(float) ||
                    hinge?.FieldType != typeof(HingeJoint) || locked?.FieldType != typeof(bool) || sticky == null)
                    throw new MissingMemberException("Dinghies.TillerRudder members changed");
                var main = PumpWorld.OptionalType("Dinghies", "Dinghies.DinghiesMain");
                inverted = main == null ? null : AccessTools.Field(main, "invertedTillerConfig")?.GetValue(null) as ConfigEntry<bool>;
                harmony.Patch(update, prefix: new HarmonyMethod(typeof(PumpTiller), nameof(Prefix)), postfix: new HarmonyMethod(typeof(PumpTiller), nameof(Postfix)));
                PumpWorld.TillerType = type;
                PumpWorld.TillerRudder = rudder;
            }
            catch (Exception e)
            {
                PumpWorld.TillerType = null;
                Plugin.Log.LogWarning("Dinghies tiller not supported by this Propeller version; wheel control works without it. " + e.Message);
            }
        }
        // The tiller's angle is needed only while it is held.
        private static void Prefix(GoPointerButton __instance, out float __state) { __state = PumpInput.Held(__instance) ? (float)input.GetValue(__instance) : 0f; }
        private static void Postfix(GoPointerButton __instance, float __state)
        {
            if (!Plugin.Ready || !PumpInput.Held(__instance) ||
                !PumpInput.HandleWheelInput(__instance, GameInput.GetKey(InputName.MoveUp), GameInput.GetKey(InputName.MoveDown), Time.deltaTime)) return;
            // Dinghies set input = 0 this frame if W or S was down; keep the tiller where it was, plus this frame's A/D.
            var pointer = sticky.GetValue(__instance) as GoPointer;
            var joint = hinge.GetValue(__instance) as HingeJoint;
            if (!pointer || !pointer.movement || !joint || (bool)locked.GetValue(__instance)) return;
            Vector3 keys = pointer.movement.GetKeyboardDelta();
            if (keys.y == 0f) return;
            float limit = joint.limits.max;
            float value = Mathf.Clamp(__state + keys.x * .025f * (inverted != null && inverted.Value ? -1f : 1f), -limit, limit);
            input.SetValue(__instance, value);
            var spring = joint.spring;
            spring.targetPosition = value;
            joint.spring = spring;
        }
    }

    [HarmonyPatch(typeof(GPButtonSteeringWheel), nameof(GPButtonSteeringWheel.ExtraLateUpdate))]
    internal static class PumpWheelThrottle
    {
        private static void Postfix(GPButtonSteeringWheel __instance)
        {
            if (!PumpWorld.InputAllowed || !PumpInput.Held(__instance)) return;
            PumpInput.HandleWheelInput(__instance, GameInput.GetKey(InputName.MoveUp), GameInput.GetKey(InputName.MoveDown), Time.deltaTime);
        }
    }
    // Observe successful vanilla embark changes; do not infer disembarking from distance.
    [HarmonyPatch]
    internal static class PumpSafety
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerEmbarkerNew), "PlayerDisembark");
            yield return AccessTools.Method(typeof(PlayerEmbarkerNew), "PlayerEmbark");
            yield return AccessTools.Method(typeof(PlayerEmbarkDisembarkTrigger), "ExitBoat");
            yield return AccessTools.Method(typeof(PlayerEmbarkDisembarkTrigger), "EnterBoat");
            yield return AccessTools.Method(typeof(PlayerEmbarkTriggerNew), "ExitBoat");
            yield return AccessTools.Method(typeof(PlayerEmbarkTriggerNew), "EnterBoat");
        }
        private static void Prefix(out Transform __state) { __state = GameState.currentBoat; }
        private static void Postfix(Transform __state) { BoatChanged(__state, GameState.currentBoat); }
        internal static void BoatChanged(Transform before, Transform after)
        {
            if (!Plugin.Ready || !before || before == after || !GameState.playing || PumpWorld.Loading ||
                GameState.currentlyLoading || GameState.loadingBoatLocalItems || GameState.justStarted ||
                GameState.recovering || GameState.currentShipyard || GameState.sleeping || GameState.inBed || GameState.justWokeUp) return;
            int boatId = PumpWorld.BoatId(before);
            if (after && boatId == PumpWorld.BoatId(after)) return;
            PumpWorld.State.SafetyShutdown(boatId);
            foreach (var item in PumpWorld.Live)
                if (item && item.IsPump && item.Record != null && item.Record.BoatId == boatId && !item.Record.Enabled)
                    item.StopDrive();
        }
    }

}
