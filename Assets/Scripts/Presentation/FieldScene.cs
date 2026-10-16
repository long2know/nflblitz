using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Blitz.Contracts;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Blitz.Presentation
{
    internal static class Palette
    {
        internal static readonly Color Home = new Color32(39, 216, 226, 255);
        internal static readonly Color Away = new Color32(255, 149, 94, 255);
        internal static readonly Color Gold = new Color32(255, 220, 111, 255);
        internal static readonly Color Ink = new Color32(10, 25, 35, 255);
        internal static readonly Color Panel = new Color32(17, 39, 51, 255);
        internal static readonly Color Muted = new Color32(161, 186, 195, 255);
        internal static readonly Color White = new Color32(237, 248, 241, 255);
    }

    internal sealed class FieldScene : IDisposable
    {
        private sealed class Actor
        {
            internal Transform Root;
            internal Transform Body;
            internal Transform LeftLeg;
            internal Transform RightLeg;
            internal SourcePlayerRig SourceRig;
            internal SourceAnimationPlayer SourceAnimation;
            internal GameObject ControlRing;
            internal GameObject ReceiverRing;
            internal GameObject TargetRing;
            internal Transform Arrow;
        }

        private readonly Transform root;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly Actor[] actors = new Actor[SliceRules.PlayersPerTeam * 2];
        private readonly SourcePlayerRig sourcePlayerPrefab;
        private readonly ISourceAnimationLibrary sourceAnimations;
        private readonly Transform scrimmage;
        private readonly Transform target;
        private readonly Transform ball;
        private readonly Transform ballShadow;
        private readonly LineRenderer ballTrail;
        private readonly Vector3[] trailPoints = new Vector3[24];
        private readonly Font font;
        private long lastTrailTick = -1;
        private int trailCount;
        private float animationTime;

        internal FieldScene(Transform parent, SourcePlayerRig sourcePrefab, ISourceAnimationLibrary animations)
        {
            sourcePlayerPrefab = sourcePrefab;
            sourceAnimations = animations;
            if (sourcePrefab != null && animations == null)
                throw new InvalidOperationException("Source field actors require the original animation library; static pose fallback is disabled.");
            SourceEnvironmentProvider sourceWorld = SourceEnvironmentProvider.ResolveWorldMode();
            root = new GameObject(sourceWorld == null ? "Explicit graybox field" : "Original source environment").transform;
            root.SetParent(parent, false);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Material home = Lit("Home / tide", Palette.Home);
            Material away = Lit("Away / ember", Palette.Away);
            Material dark = Lit("Graphite", Palette.Ink);
            Material trim = Lit("Warm white", Palette.White);
            Material gold = Flat("First down / gold", Palette.Gold);
            Material cyan = Flat("Scrimmage / ice", Palette.Home);
            Material white = Flat("Receiver / white", Palette.White);
            Material shadow = Flat("Contact shadow", new Color32(14, 49, 34, 255));
            float halfWidth = SliceRules.FieldHalfWidthYards;

            if (sourceWorld != null)
            {
                SourceEnvironmentProvider.AttachRequired(sourceWorld, root);
            }
            else
            {
                Material turfA = Lit("Turf / fern", new Color32(28, 86, 61, 255));
                Material turfB = Lit("Turf / jade", new Color32(32, 96, 67, 255));
                Material chalk = Flat("Chalk", new Color32(184, 213, 187, 255));
                Material homeDark = Lit("Home end zone", new Color32(16, 76, 85, 255));
                Material awayDark = Lit("Away end zone", new Color32(113, 57, 39, 255));
                Box("Raised field foundation", root, new Vector3(0f, -0.55f, 0f),
                    new Vector3(126f, 0.7f, halfWidth * 2f + 6f), dark);
                Box("Quiet surround", root, new Vector3(0f, -0.96f, 0f), new Vector3(210f, 0.2f, 130f), dark);
                for (int stripe = 0; stripe < 20; stripe++)
                    Box("Five yard turf " + stripe, root, new Vector3(-47.5f + stripe * 5f, -0.14f, 0f),
                        new Vector3(5f, 0.24f, halfWidth * 2f), stripe % 2 == 0 ? turfA : turfB);
                Box("Home end zone", root, new Vector3(-55f, -0.14f, 0f), new Vector3(10f, 0.24f, halfWidth * 2f), homeDark);
                Box("Away end zone", root, new Vector3(55f, -0.14f, 0f), new Vector3(10f, 0.24f, halfWidth * 2f), awayDark);

                for (int yard = 0; yard <= 100; yard += 5)
                {
                    float width = yard == 0 || yard == 100 ? 0.24f : 0.09f;
                    Box("Yard line " + yard, root, new Vector3(yard - 50f, 0.008f, 0f),
                        new Vector3(width, 0.018f, halfWidth * 2f), chalk, false);
                    if (yard > 0 && yard < 100 && yard % 10 == 0)
                    {
                        string number = Math.Min(yard, 100 - yard).ToString();
                        GroundText(number, new Vector3(yard - 50f, 0.035f, -halfWidth + 4f), 2.3f, Palette.White);
                        GroundText(number, new Vector3(yard - 50f, 0.035f, halfWidth - 4f), 2.3f, Palette.White);
                    }
                }
                for (int yard = 1; yard < 100; yard++)
                {
                    if (yard % 5 == 0)
                        continue;
                    Box("Near hash " + yard, root, new Vector3(yard - 50f, 0.012f, -7f),
                        new Vector3(0.09f, 0.018f, 0.7f), chalk, false);
                    Box("Far hash " + yard, root, new Vector3(yard - 50f, 0.012f, 7f),
                        new Vector3(0.09f, 0.018f, 0.7f), chalk, false);
                }
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Box("Sideline", root, new Vector3(0f, 0.015f, sign * halfWidth),
                        new Vector3(120.3f, 0.025f, 0.28f), chalk, false);
                    Box("End boundary", root, new Vector3(sign * 60f, 0.015f, 0f),
                        new Vector3(0.28f, 0.025f, halfWidth * 2f), chalk, false);
                    Box("Low sideline rail", root, new Vector3(0f, -0.1f, sign * (halfWidth + 2.3f)),
                        new Vector3(124f, 0.7f, 0.45f), homeDark);
                    for (int end = -1; end <= 1; end += 2)
                        Box("Corner pylon", root, new Vector3(end * 50f, 0.45f, sign * halfWidth),
                            new Vector3(0.32f, 0.9f, 0.32f), away);
                    GoalPost(sign * 62f, Lit("Goal frame " + sign, new Color32(230, 188, 94, 255)));
                }
                GroundText("H O M E", new Vector3(-55f, 0.035f, 0f), 2.7f, Palette.Home, new Vector3(90f, 0f, 90f));
                GroundText("A W A Y", new Vector3(55f, 0.035f, 0f), 2.7f, Palette.Away, new Vector3(90f, 0f, 90f));
                Ring("Midfield emblem", root, 3.8f, 0.09f, chalk).transform.localPosition = new Vector3(0f, 0.032f, 0f);
                GroundText("30", new Vector3(0f, 0.04f, 0f), 3f, Palette.White);
            }

            scrimmage = Box("Current scrimmage / next spot when dead", root, Vector3.zero,
                new Vector3(0.18f, 0.025f, halfWidth * 2f), cyan, false).transform;
            target = Box("Thirty yard series target", root, Vector3.zero,
                new Vector3(0.23f, 0.025f, halfWidth * 2f), gold, false).transform;
            for (int id = 0; id < actors.Length; id++)
                actors[id] = CreateActor(id, id < SliceRules.PlayersPerTeam ? home : away, dark, trim, cyan, white, gold, shadow);

            ball = new GameObject("Ball / simulation flight height").transform;
            ball.SetParent(root, false);
            Primitive("Original football", PrimitiveType.Sphere, ball, Vector3.zero,
                new Vector3(0.46f, 0.42f, 0.75f), Lit("Football / leather", new Color32(143, 70, 32, 255)));
            Box("Football laces", ball, new Vector3(0f, 0.208f, 0f), new Vector3(0.07f, 0.03f, 0.32f), trim, false);
            for (int stitch = -1; stitch <= 1; stitch++)
                Box("Lace stitch", ball, new Vector3(0f, 0.226f, stitch * 0.1f), new Vector3(0.2f, 0.025f, 0.025f), trim, false);
            ballShadow = Primitive("Ball ground shadow", PrimitiveType.Cylinder, root, Vector3.zero,
                new Vector3(0.85f, 0.007f, 0.85f), shadow, false).transform;
            ballTrail = new GameObject("Recent real ball flight").AddComponent<LineRenderer>();
            ballTrail.transform.SetParent(root, false);
            ballTrail.sharedMaterial = gold;
            ballTrail.startWidth = 0.11f;
            ballTrail.endWidth = 0.04f;
            ballTrail.numCapVertices = 3;
            ballTrail.useWorldSpace = true;
            ballTrail.shadowCastingMode = ShadowCastingMode.Off;
            ballTrail.receiveShadows = false;
            ballTrail.positionCount = 0;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.68f, 0.75f, 0.79f);
            RenderSettings.fog = false;
            Light sun = new GameObject("Afternoon key light").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            sun.color = new Color(1f, 0.93f, 0.81f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.shadowBias = 0.035f;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 110f;
            QualitySettings.antiAliasing = 2;
        }

        internal void Render(IGameState previous, IGameState state, float renderInterpolation01, float deltaTime)
        {
            animationTime += Mathf.Min(deltaTime, 0.1f);
            scrimmage.localPosition = new Vector3(state.LineOfScrimmage - 50f, 0.04f, 0f);
            target.localPosition = new Vector3(state.FirstDownLine - 50f, 0.047f, 0f);
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerView view = state.Players[i];
                if (view.Id < 0 || view.Id >= actors.Length)
                    continue;
                Actor actor = actors[view.Id];
                PlayerView old = FindPlayer(previous, view.Id, view);
                bool interpolate = actor.SourceAnimation != null && Blitz.AssetImport.SourceActorPoseSampler.CanInterpolate(old, view);
                Vector3 currentPosition = Ground(view.Position) + Vector3.up * view.Height;
                actor.Root.localPosition = interpolate ? Vector3.Lerp(
                    Ground(old.Position) + Vector3.up * old.Height, currentPosition, renderInterpolation01) : currentPosition;
                Vector3 velocity = new Vector3(view.Velocity.X, 0f, view.Velocity.Z);
                Vector3 facing = velocity.sqrMagnitude > 0.05f ? velocity
                    : state.Possession == view.Team ? new Vector3(state.OffenseDirection, 0f, 0f)
                    : Ground(state.Ball.Position) - actor.Root.localPosition;
                facing.y = 0f;
                if (actor.SourceAnimation != null)
                {
                    actor.Body.localRotation = Quaternion.identity;
                    actor.SourceAnimation.Apply(old, view, renderInterpolation01);
                    facing = new Vector3(view.FacingDirection.X, 0f, view.FacingDirection.Z);
                }
                else if (facing.sqrMagnitude > 0.001f)
                    actor.Body.localRotation = Quaternion.LookRotation(facing);
                bool moving = state.Phase == GamePhase.LiveBall && velocity.sqrMagnitude > 0.5f;
                float stride = moving ? Mathf.Sin(animationTime * 17f + i * 0.9f) * 25f : 0f;
                if (actor.SourceRig == null)
                {
                    actor.LeftLeg.localRotation = Quaternion.Euler(stride, 0f, 0f);
                    actor.RightLeg.localRotation = Quaternion.Euler(-stride, 0f, 0f);
                }
                actor.ControlRing.SetActive(state.ControlledPlayerId == view.Id);
                actor.ReceiverRing.SetActive(state.Possession == TeamId.Home && view.Id >= 4 && view.Id <= 6
                    && (state.Phase == GamePhase.PreSnap || state.Phase == GamePhase.LiveBall));
                actor.TargetRing.SetActive(state.Ball.Mode == BallMode.InFlight && state.Ball.TargetPlayerId == view.Id);
                actor.Arrow.gameObject.SetActive(state.ControlledPlayerId == view.Id);
                actor.Arrow.localRotation = actor.SourceAnimation != null && facing.sqrMagnitude > 0.001f ?
                    Quaternion.LookRotation(facing) : actor.Body.localRotation;
                float pulse = 1f + 0.08f * Mathf.Sin(animationTime * 5f);
                actor.TargetRing.transform.localScale = new Vector3(pulse, 1f, pulse);
            }

            BallView ballView = state.Ball;
            Vector3 position = Ground(ballView.Position) + Vector3.up * Mathf.Max(0.3f, ballView.Height);
            if (ballView.Mode == BallMode.Held && ballView.CarrierId >= 0 && ballView.CarrierId < actors.Length)
            {
                Actor carrier = actors[ballView.CarrierId];
                Vector3 forward = carrier.SourceAnimation == null ? carrier.Body.forward :
                    new Vector3(carrier.SourceAnimation.AppliedView.FacingDirection.X, 0, carrier.SourceAnimation.AppliedView.FacingDirection.Z);
                position += forward * 0.65f;
                position.y = Mathf.Max(position.y, 1.15f);
            }
            ball.position = position;
            ball.localRotation = Quaternion.Euler(animationTime * 190f, animationTime * 85f, 15f);
            ballShadow.position = Ground(ballView.Position) + Vector3.up * 0.023f;
            float shadowSize = 0.65f + Mathf.Clamp(ballView.Height, 0f, 15f) * 0.055f;
            ballShadow.localScale = new Vector3(shadowSize, 0.007f, shadowSize);

            if (ballView.Mode != BallMode.InFlight)
            {
                trailCount = 0;
                ballTrail.positionCount = 0;
                lastTrailTick = -1;
            }
            else if (state.Tick != lastTrailTick)
            {
                lastTrailTick = state.Tick;
                if (trailCount == trailPoints.Length)
                    Array.Copy(trailPoints, 1, trailPoints, 0, trailPoints.Length - 1);
                else
                    trailCount++;
                trailPoints[trailCount - 1] = position;
                ballTrail.positionCount = trailCount;
                for (int i = 0; i < trailCount; i++)
                    ballTrail.SetPosition(i, trailPoints[i]);
            }
        }

        private static PlayerView FindPlayer(IGameState state, int id, PlayerView current)
        {
            if (state != null)
                for (int i = 0; i < state.Players.Count; i++)
                    if (state.Players[i].Id == id) return state.Players[i];
            return current;
        }

        internal void ResetRenderHistory()
        {
            trailCount = 0;
            lastTrailTick = -1;
            ballTrail.positionCount = 0;
        }

        internal string AnimationEvidence(IGameState state)
        {
            var evidence = new AnimationEvidenceDocument {
                hostTick = state.Tick, phase = state.Phase.ToString(), ballMode = state.Ball.Mode.ToString(),
                carrierId = state.Ball.CarrierId, targetId = state.Ball.TargetPlayerId,
                controlledPlayerId = state.ControlledPlayerId, actors = new List<AnimationActorEvidence>()
            };
            ISourceGameState source = state as ISourceGameState;
            evidence.sourceTick = source == null ? -1 : source.SourceTick;
            int[] indices = { 0, 1, 4, 8, 11, 14 };
            foreach (Actor actor in actors)
            {
                if (actor.SourceAnimation == null) continue;
                PlayerView view = actor.SourceAnimation.AppliedView;
                var item = new AnimationActorEvidence {
                    id = view.Id, role = view.Role.ToString(), action = view.Animation.Action.ToString(),
                    selectionEvidence = view.Animation.SelectionEvidence.ToString(),
                    sourceTick = view.Animation.SourceTick, script = view.Animation.Primary.ScriptSymbol,
                    sequence = view.Animation.Primary.Sample.SequenceName, sourceFrame = view.Animation.Primary.Sample.SourceFrame,
                    nextSourceFrame = view.Animation.Primary.Sample.NextSourceFrame,
                    frameInterpolation = view.Animation.Primary.Sample.Interpolation01,
                    renderInterpolation = actor.SourceAnimation.RenderInterpolation01,
                    secondaryScript = view.Animation.Secondary.ScriptSymbol,
                    secondarySequence = view.Animation.Secondary.Sample.SequenceName,
                    secondarySourceFrame = view.Animation.Secondary.Sample.SourceFrame,
                    position = actor.Root.position, joints = new List<AnimationJointEvidence>()
                };
                foreach (int index in indices)
                {
                    Transform joint = actor.SourceRig.GetJoint(index);
                    item.joints.Add(new AnimationJointEvidence { index = index, position = joint.position, localRotation = joint.localRotation });
                }
                evidence.actors.Add(item);
            }
            return EvidenceJson(evidence);
        }

        private static string EvidenceJson(AnimationEvidenceDocument document)
        {
            var json = new StringBuilder(16384);
            json.Append("{\"hostTick\":").Append(document.hostTick).Append(",\"sourceTick\":").Append(document.sourceTick);
            json.Append(",\"phase\":"); JsonString(json, document.phase);
            json.Append(",\"ballMode\":"); JsonString(json, document.ballMode);
            json.Append(",\"carrierId\":").Append(document.carrierId).Append(",\"targetId\":").Append(document.targetId)
                .Append(",\"controlledPlayerId\":").Append(document.controlledPlayerId).Append(",\"actors\":[");
            for (int i = 0; i < document.actors.Count; i++)
            {
                if (i != 0) json.Append(',');
                AnimationActorEvidence actor = document.actors[i];
                json.Append("{\"id\":").Append(actor.id).Append(",\"sourceTick\":").Append(actor.sourceTick);
                json.Append(",\"role\":"); JsonString(json, actor.role);
                json.Append(",\"action\":"); JsonString(json, actor.action);
                json.Append(",\"selectionEvidence\":"); JsonString(json, actor.selectionEvidence);
                json.Append(",\"script\":"); JsonString(json, actor.script);
                json.Append(",\"sequence\":"); JsonString(json, actor.sequence);
                json.Append(",\"sourceFrame\":").Append(actor.sourceFrame).Append(",\"nextSourceFrame\":").Append(actor.nextSourceFrame);
                json.Append(",\"frameInterpolation\":"); JsonNumber(json, actor.frameInterpolation);
                json.Append(",\"renderInterpolation\":"); JsonNumber(json, actor.renderInterpolation);
                json.Append(",\"secondaryScript\":"); JsonString(json, actor.secondaryScript);
                json.Append(",\"secondarySequence\":"); JsonString(json, actor.secondarySequence);
                json.Append(",\"secondarySourceFrame\":").Append(actor.secondarySourceFrame).Append(",\"position\":");
                JsonVector(json, actor.position);
                json.Append(",\"joints\":[");
                for (int j = 0; j < actor.joints.Count; j++)
                {
                    if (j != 0) json.Append(',');
                    AnimationJointEvidence joint = actor.joints[j];
                    json.Append("{\"index\":").Append(joint.index).Append(",\"position\":"); JsonVector(json, joint.position);
                    json.Append(",\"localRotation\":{\"x\":"); JsonNumber(json, joint.localRotation.x);
                    json.Append(",\"y\":"); JsonNumber(json, joint.localRotation.y);
                    json.Append(",\"z\":"); JsonNumber(json, joint.localRotation.z);
                    json.Append(",\"w\":"); JsonNumber(json, joint.localRotation.w);
                    json.Append("}}");
                }
                json.Append("]}");
            }
            return json.Append("]}").ToString();
        }

        private static void JsonVector(StringBuilder json, Vector3 value)
        {
            json.Append("{\"x\":"); JsonNumber(json, value.x);
            json.Append(",\"y\":"); JsonNumber(json, value.y);
            json.Append(",\"z\":"); JsonNumber(json, value.z); json.Append('}');
        }
        private static void JsonNumber(StringBuilder json, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Actual source animation telemetry contains a nonfinite native transform.");
            json.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }
        private static void JsonString(StringBuilder json, string value)
        {
            if (value == null) { json.Append("null"); return; }
            json.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') json.Append('\\').Append(c);
                else if (c < 32) json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else json.Append(c);
            }
            json.Append('"');
        }

        [Serializable] private sealed class AnimationEvidenceDocument
        {
            public long hostTick, sourceTick;
            public string phase, ballMode;
            public int carrierId, targetId, controlledPlayerId;
            public List<AnimationActorEvidence> actors;
        }
        [Serializable] private sealed class AnimationActorEvidence
        {
            public int id, sourceFrame, nextSourceFrame, secondarySourceFrame;
            public long sourceTick;
            public string role, action, selectionEvidence, script, sequence, secondaryScript, secondarySequence;
            public float frameInterpolation, renderInterpolation;
            public Vector3 position;
            public List<AnimationJointEvidence> joints;
        }
        [Serializable] private sealed class AnimationJointEvidence
        {
            public int index;
            public Vector3 position;
            public Quaternion localRotation;
        }

        private Actor CreateActor(int id, Material jersey, Material dark, Material trim,
            Material control, Material receiver, Material targetMaterial, Material shadow)
        {
            Transform actorRoot = new GameObject((id < 7 ? "Home " : "Away ") + id).transform;
            actorRoot.SetParent(root, false);
            Transform body = new GameObject("Facing / cosmetic rig").transform;
            body.SetParent(actorRoot, false);
            SourcePlayerRig sourceRig = null;
            Transform leftLeg = null, rightLeg = null;
            if (sourcePlayerPrefab != null)
                sourceRig = SourcePlayerProvider.AttachBindPose(sourcePlayerPrefab, body);
            else
            {
                Primitive("Jersey", PrimitiveType.Capsule, body, new Vector3(0f, 1.13f, 0f),
                new Vector3(0.78f, 0.67f, 0.72f), jersey);
            Box("Shoulder pads", body, new Vector3(0f, 1.48f, 0f), new Vector3(1.15f, 0.29f, 0.75f), jersey);
            Primitive("Helmet", PrimitiveType.Sphere, body, new Vector3(0f, 1.96f, 0f),
                new Vector3(0.71f, 0.69f, 0.72f), trim);
            Box("Helmet stripe", body, new Vector3(0f, 2.28f, 0f), new Vector3(0.12f, 0.055f, 0.58f), jersey);
            Box("Visor / forward", body, new Vector3(0f, 1.94f, 0.34f), new Vector3(0.5f, 0.16f, 0.12f), dark);
            leftLeg = Primitive("Left leg", PrimitiveType.Capsule, body, new Vector3(-0.25f, 0.44f, 0f),
                new Vector3(0.26f, 0.34f, 0.27f), trim).transform;
            rightLeg = Primitive("Right leg", PrimitiveType.Capsule, body, new Vector3(0.25f, 0.44f, 0f),
                new Vector3(0.26f, 0.34f, 0.27f), trim).transform;
            Box("Left boot", body, new Vector3(-0.25f, 0.12f, 0.09f), new Vector3(0.3f, 0.22f, 0.48f), dark);
            Box("Right boot", body, new Vector3(0.25f, 0.12f, 0.09f), new Vector3(0.3f, 0.22f, 0.48f), dark);
            Primitive("Left glove", PrimitiveType.Sphere, body, new Vector3(-0.58f, 1.08f, 0.1f),
                new Vector3(0.28f, 0.36f, 0.28f), dark);
            Primitive("Right glove", PrimitiveType.Sphere, body, new Vector3(0.58f, 1.08f, 0.1f),
                new Vector3(0.28f, 0.36f, 0.28f), dark);
            }
            GameObject controlRing = Ring("YOU / cyan ring", actorRoot, 1.2f, 0.13f, control);
            controlRing.transform.localPosition = Vector3.up * 0.055f;
            GameObject receiverRing = Ring("Eligible receiver", actorRoot, 0.98f, 0.045f, receiver);
            receiverRing.transform.localPosition = Vector3.up * 0.04f;
            GameObject targetRing = Ring("Pass target / gold", actorRoot, 1.55f, 0.12f, targetMaterial);
            targetRing.transform.localPosition = Vector3.up * 0.07f;
            Primitive("Contact shadow", PrimitiveType.Cylinder, actorRoot, new Vector3(0.16f, 0.015f, 0.16f),
                new Vector3(1.15f, 0.008f, 1.15f), shadow, false);
            Transform arrow = new GameObject("Controlled direction").transform;
            arrow.SetParent(actorRoot, false);
            Mesh arrowMesh = new Mesh { name = "Original directional chevron" };
            arrowMesh.vertices = new[]
            {
                new Vector3(-0.45f, 0.06f, 1.15f), new Vector3(0f, 0.06f, 1.85f),
                new Vector3(0.45f, 0.06f, 1.15f)
            };
            arrowMesh.triangles = new[] { 0, 1, 2 };
            arrowMesh.RecalculateNormals();
            meshes.Add(arrowMesh);
            arrow.gameObject.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
            arrow.gameObject.AddComponent<MeshRenderer>().sharedMaterial = control;
            return new Actor
            {
                Root = actorRoot, Body = body, LeftLeg = leftLeg, RightLeg = rightLeg, SourceRig = sourceRig,
                SourceAnimation = sourceRig == null ? null : new SourceAnimationPlayer(sourceRig, sourceAnimations),
                ControlRing = controlRing, ReceiverRing = receiverRing, TargetRing = targetRing, Arrow = arrow
            };
        }

        private void GoalPost(float x, Material material)
        {
            Box("Goal support", root, new Vector3(x, 2.3f, 0f), new Vector3(0.23f, 4.6f, 0.23f), material);
            Box("Goal crossbar", root, new Vector3(x, 4.5f, 0f), new Vector3(0.23f, 0.23f, 7.5f), material);
            for (int sign = -1; sign <= 1; sign += 2)
                Box("Goal upright", root, new Vector3(x, 6.45f, sign * 3.65f), new Vector3(0.23f, 4.1f, 0.23f), material);
        }

        private void GroundText(string text, Vector3 position, float size, Color color, Vector3 rotation = default(Vector3))
        {
            GameObject label = new GameObject("Field paint / " + text);
            label.transform.SetParent(root, false);
            label.transform.localPosition = position;
            label.transform.localEulerAngles = rotation == Vector3.zero ? new Vector3(90f, 0f, 0f) : rotation;
            TextMesh mesh = label.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = size * 0.1f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.text = text;
            mesh.color = color;
            MeshRenderer renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private GameObject Ring(string name, Transform parent, float radius, float thickness, Material material)
        {
            const int segments = 48;
            Vector3[] vertices = new Vector3[segments * 2];
            int[] triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * radius;
                vertices[i * 2 + 1] = direction * (radius - thickness);
                int next = ((i + 1) % segments) * 2;
                int offset = i * 6;
                triangles[offset] = i * 2;
                triangles[offset + 1] = i * 2 + 1;
                triangles[offset + 2] = next;
                triangles[offset + 3] = next;
                triangles[offset + 4] = i * 2 + 1;
                triangles[offset + 5] = next + 1;
            }
            Mesh mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            meshes.Add(mesh);
            GameObject ring = new GameObject(name);
            ring.transform.SetParent(parent, false);
            ring.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ring.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ring;
        }

        private Material Lit(string name, Color color)
        {
            Material material = NewMaterial("Standard", name, color);
            material.SetFloat("_Glossiness", 0.15f);
            material.SetFloat("_Metallic", 0f);
            return material;
        }

        private Material Flat(string name, Color color)
        {
            return NewMaterial("Unlit/Color", name, color);
        }

        private Material NewMaterial(string shaderName, string name, Color color)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("Required Built-in shader is missing: " + shaderName + ". Build using Blitz.Editor.WebGlBuild.Build.");
            Material material = new Material(shader) { name = name, color = color };
            materials.Add(material);
            return material;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool shadows = true)
        {
            return Primitive(name, PrimitiveType.Cube, parent, position, scale, material, shadows);
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool shadows = true)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = position;
            primitive.transform.localScale = scale;
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
            MeshRenderer renderer = primitive.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            return primitive;
        }

        internal static Vector3 Ground(FieldVector position)
        {
            return new Vector3(position.X - 50f, 0f, position.Z);
        }

        public void Dispose()
        {
            for (int i = 0; i < materials.Count; i++)
                Object.Destroy(materials[i]);
            for (int i = 0; i < meshes.Count; i++)
                Object.Destroy(meshes[i]);
        }
    }
}
