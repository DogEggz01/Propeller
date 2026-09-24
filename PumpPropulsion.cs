using System;
using System.Collections.Generic;
using System.Reflection;
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
            Transform rimAnchor = System.Array.Find(rim, t => t.name == "WaterRim");
            float radius = Vector3.Distance(mouth.position, rimAnchor.position);
            for (int i = 1; i < ProbeCount; i++)
            {
                float angle = (i - 1) * Mathf.PI / 4f;
                localPoints[i] = item.transform.InverseTransformPoint(mouth.position + radius * (mouth.right * Mathf.Cos(angle) + mouth.up * Mathf.Sin(angle)));
            }
            for (int i = 0; i < History; i++) historyFrames[i] = -1;
            Reset();
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
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
                hasSample &= Finite(heights[i]) && Finite(normals[i]) && normals[i].y > .1f;
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
        internal static void HandleWheelInput(GPButtonSteeringWheel wheel, bool increase, bool decrease, float delta)
        {
            if (!PumpWorld.InputAllowed || (!wheel.IsStickyClicked() && !wheel.IsCliked())) return;
            var selected = PumpWorld.Selected(wheel);
            if (selected && PumpWorld.PairOperational(selected, out var pump)) pump.Record.AdjustThrottle(increase, decrease, delta);
        }
        internal static void PatchCheatspeed(Harmony harmony)
        {
            var type = AccessTools.TypeByName("NANDCommand.CheatsPatch");
            if (type == null) return;
            var method = AccessTools.Method(type, "Postfix", new[] { typeof(GoPointer) });
            if (method == null) throw new InvalidOperationException("Unrecognized NANDCommand cheatspeed patch; refusing duplicate propulsion.");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PumpInput), nameof(AllowCheatspeed)));
        }
        private static bool AllowCheatspeed(GoPointer __0)
        {
            if (!Plugin.Ready || !__0) return true;
            // Only suppress NAND's propulsion when this pointer is holding a selected pump's wheel.
            var wheel = StickyWheelField.GetValue(__0) as GPButtonSteeringWheel;
            return !wheel || PumpWorld.Selected(wheel) == null;
        }
    }

    [HarmonyPatch(typeof(GPButtonSteeringWheel), nameof(GPButtonSteeringWheel.ExtraLateUpdate))]
    internal static class PumpWheelThrottle
    {
        private static void Postfix(GPButtonSteeringWheel __instance)
        {
            if (!PumpWorld.InputAllowed) return;
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
