using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

namespace Blitz.SourcePlayer;

public sealed class PlayerHost
{
    public const string RuntimeFlavor = "portable C# browser preview (not a Unity player build)";
    private readonly IRenderSink sink;
    private readonly SourceCabinetQuantizer quantizer;
    private readonly SourceCabinetInputBuffer input;
    private readonly SourceCabinetPlaySelection selection;
    private readonly SourceHudComposer hud;
    private readonly SourceHudStatusAnimation hudClock = new();
    private readonly Queue<SourceCommand>[] commands = { new(), new(), new(), new() };
    private readonly Queue<object> events = new();
    private readonly Queue<object> contacts = new();
    private readonly Queue<object> dustCues = new();
    private readonly Queue<object> hopSounds = new();
    private readonly SourceSoundVmTransport hopSoundTransport = new();
    private readonly Dictionary<string, long> eventCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> contactCounts = new(StringComparer.Ordinal);
    private readonly int[] lastButtons = new int[4];
    private readonly double[] lastAxes = new double[8];
    private readonly double[] joints = new double[14 * 15 * 16];
    private readonly int[] modelIds = new int[14], appearanceIds = new int[14];
    public PlayerData Data { get; }
    public RenderAssets Assets { get; }
    public GameSimulation Game { get; }
    public FixedStepBridge Bridge { get; }
    public PreviewCamera Camera { get; private set; }
    public OriginalHudProcess HudProcess { get; private set; }
    public PreviewOptions Options { get; }
    public FirePresentation Fire { get; private set; }
    public long Epoch { get; private set; }
    public long Frames { get; private set; }
    public long SourceWakes { get; private set; }
    public long InputSamples { get; private set; }
    public long InputChanges { get; private set; }
    public long SoundSelections { get; private set; }
    public long UnqualifiedSoundCalls { get; private set; }
    public long UnqualifiedVisualCallbacks { get; private set; }
    public long DustCallbacks { get; private set; }
    public long BoundDustCallbacks { get; private set; }
    public long PreSnapSwitchSoundSelections { get; private set; }
    public bool Active { get; private set; } = true;
    public string LastStatus { get; private set; } = "{}";
    private long lastSourceTick;

    public PlayerHost(PlayerData data, IRenderSink sink, Action<string> progress = null, PreviewOptions options = null)
    {
        Data = data;
        this.sink = sink;
        Options = options ?? PreviewOptions.FromUrl("", data.Catalog.Profile.StableId);
        DataCatalog.Require(Options.Profile == data.Catalog.Profile.StableId,
            "Host options must retain the authenticated built profile before asset or kernel construction.");
        if (Options.FireEnabled && data.Fire == null)
            throw new InvalidDataException("The selected fire opt-in has no authenticated original assets.");
        Assets = new RenderAssets(data, sink, progress);
        Fire = new FirePresentation(data.Fire, Options.FireEnabled, sink);
        progress?.Invoke("Constructing " + KernelBinding.FactoryFor(data.Catalog.Profile) + ", fourteen actors and source camera");
        Game = KernelBinding.Create(data, Options);
        Camera = new PreviewCamera(data, Assets, Game);
        hopSoundTransport.BindOutput(new HopSoundObservation(this));
        quantizer = new SourceCabinetQuantizer(data.Hud.Input);
        input = new SourceCabinetInputBuffer(data.Configuration.Match);
        selection = new SourceCabinetPlaySelection(data.Hud);
        Game.SourceStationInputTransform = selection.ApplyAtSourceTick;
        selection.EventRaised += value => {
            if (!value.IsRandomSelection) return;
            if (value.SoundCall.HasValue) { SoundSelections++; UnqualifiedSoundCalls++; }
            Enqueue(events, new {
                kind = "cabinet-random-selection", sourceTick = value.SourceTick, station = value.StationId,
                team = value.Side.ToString(), stage = value.Kind.ToString(), page = value.PageIndex,
                slot = value.SlotIndex, flipped = value.OffenseFlipped, bank = value.SoundBank,
                playlist = value.SoundCall, volume = value.SoundVolume, pan = value.SoundPan,
                priority = value.SoundPriority, pcmOrDspPlayback = false
            }, 128);
        };
        hud = new SourceHudComposer(data.Hud);
        HudProcess = new OriginalHudProcess(data.Hud, Game);
        hudClock.Advance(Game.SourceTick, HudProcess.CommittedHud.ClockWarning);
        Bridge = new FixedStepBridge(Game);
        Bridge.SourceEventRaised += value => {
            string type = value.Type.ToString();
            eventCounts[type] = eventCounts.GetValueOrDefault(type) + 1;
            Enqueue(events, new {
                sourceTick = value.SourceTick, hostTick = value.Tick, type,
                actor = value.ActorPlayerId, target = value.TargetPlayerId, station = value.StationId,
                team = value.Team?.ToString(), value = value.Value,
                result = value.Result.IsSpecified ? value.Result.Kind.ToString() : null,
                cause = value.Result.IsSpecified ? value.Result.Cause?.ToString() : null
            }, 128);
        };
        Bridge.AfterSimulationStep += ObserveSound;
        Bridge.SourcePresentationCueRaised += cue => {
            if (Game.SourceDefensivePreSnapSwitchEnabled && cue is SourceCallbackCue sound &&
                sound.Context.SourcePath == "plyrseq.c" && sound.Context.ExecutingSymbol == "m_hopclp_anim")
            {
                SourceSoundVmDispatch dispatch = hopSoundTransport.TakeForCue(sound) ??
                    throw new InvalidDataException("The enabled hop has no actual synchronous resolved sound/cue pairing.");
                Camera.Playback.ConsumeResolvedSourceSound(dispatch, hopSoundTransport.DeliverResolved);
            }
            Camera.Cue(cue);
            if (cue.Kind == SourcePresentationCueKind.DustCallback)
            {
                DustCallbacks++;
                var callback = (SourceCallbackCue)cue;
                Enqueue(dustCues, new { sourceTick = cue.Context.SourceTick, order = cue.Context.ProcessOrder,
                    actor = cue.Context.ActorPlayerId, path = cue.Context.SourcePath, symbol = cue.Context.ExecutingSymbol,
                    line = cue.Context.SourceLine, callback.FunctionSymbol, callback.Argument0, callback.Argument1,
                    callback.Argument2, cue.Context.ScriptInstanceId, cue.Context.OpcodeSymbol }, 64);
                if (!Game.SourceFireEnabled) UnqualifiedVisualCallbacks++;
                else
                {
                    DataCatalog.Require(Game.SourceFireRecords.Any(r => r.Kind == "CreateProcess" &&
                        r.Detail == "dustcld" && r.Site.SourceTick == cue.Context.SourceTick &&
                        r.Site.ActorId == cue.Context.ActorPlayerId),
                        "DUST_CALLBACK_UNBOUND: an enabled real DustCallback did not queue its source dustcld children.");
                    BoundDustCallbacks++;
                }
            }
        };
        Bridge.AfterSourcePresentation += hopSoundTransport.CompletePresentationBatch;
        Bridge.AfterHostStep += ObserveStep;
        Bridge.Restarted += ResetPresentation;
        Capture(new double[8], new int[4]);
    }

    private void ObserveSound()
    {
        foreach (SourceSelectionTimerSound call in Game.TakeSourceSelectionTimerSounds())
        {
            SoundSelections++;
            UnqualifiedSoundCalls++;
            Enqueue(events, new { kind = "timedown_play_select", sourceTick = call.SourceTick,
                serial = call.ProcessSerial, number = call.Number, bank = call.Bank,
                playlist = call.PlaylistId, volume = call.Volume, pan = call.Pan,
                priority = call.PriorityWord, pcmOrDspPlayback = call.PcmOrDspPlayback }, 64);
        }
        foreach (SourcePossessionSpeech speech in Game.SourcePossessionSpeechSubmissions)
        {
            SoundSelections++;
            UnqualifiedSoundCalls++;
            Enqueue(events, new { kind = "intercepted_ball_sp", sourceTick = speech.SourceTick,
                processOrder = speech.ProcessOrder, serial = speech.ProcessSerial, bank = speech.Bank,
                ordinal = speech.SelectedOrdinal, playlist = speech.PlaylistId, volume = speech.Volume,
                pan = speech.Pan, priority = speech.PriorityWord, pcmOrDspPlayback = false }, 64);
        }
        SourceSoundVmBatch batch = Game.TakeSourceSoundVmDispatches();
        var hops = new List<SourceSoundVmDispatch>();
        foreach (SourceSoundVmDispatch dispatch in batch.Dispatches)
        {
            SoundSelections++;
            if (dispatch.Status == SourceSoundVmStatus.MissingInput)
                throw new InvalidDataException("Required original sound-selection input is missing: " + dispatch.Detail);
            if (dispatch.Result != null) UnqualifiedSoundCalls += dispatch.Result.Calls.Count;
            if (Game.SourceDefensivePreSnapSwitchEnabled && dispatch.HasOriginalAnimationCue &&
                dispatch.OriginalCueContext.ExecutingSymbol == "m_hopclp_anim")
            {
                DataCatalog.Require(dispatch.C2Context?.Kind == SourceC2SoundFunctionKind.AttackSoundFixed &&
                    dispatch.Status == SourceSoundVmStatus.Resolved,
                    "The enabled hop must resolve its exact typed callback, never Unsupported or muted success.");
                hops.Add(dispatch);
            }
        }
        hopSoundTransport.Drain(new SourceSoundVmBatch(hops));
    }

    private sealed class HopSoundObservation : ISourceSoundVmOutputConsumer
    {
        private readonly PlayerHost host;
        internal HopSoundObservation(PlayerHost host) { this.host = host; }
        public void ConsumeResolved(SourceSoundVmDispatch dispatch)
        {
            DataCatalog.Require(dispatch.C2Context?.Kind == SourceC2SoundFunctionKind.AttackSoundFixed &&
                dispatch.Status == SourceSoundVmStatus.Resolved && dispatch.Result?.Calls.Count == 1,
                "The recording consumer accepts only the actual resolved fixed attack call.");
            var call = dispatch.Result.Calls[0];
            host.PreSnapSwitchSoundSelections++;
            Enqueue(host.hopSounds, new {
                kind = dispatch.C2Context.Kind.ToString(), sourceTick = dispatch.C2Context.SourceTick,
                actor = dispatch.C2Context.ActorId, station = dispatch.C2StateAtCall.Value.ActorStation,
                process = dispatch.C2Context.Process, processOrder = dispatch.C2Context.ProcessOrder,
                firstCallIndex = dispatch.C2Context.FirstCallIndex,
                script = dispatch.OriginalCueContext.ExecutingSymbol, instance = dispatch.OriginalCueContext.ScriptInstanceId,
                path = dispatch.OriginalCueContext.SourcePath, line = dispatch.OriginalCueContext.SourceLine,
                function = dispatch.OriginalCue.FunctionSymbol, arguments = new[] {
                    dispatch.OriginalCue.Argument0, dispatch.OriginalCue.Argument1, dispatch.OriginalCue.Argument2 },
                call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord, call.SubcallOrder,
                randomCalls = dispatch.Result.RandomCalls, callSite = new {
                    path = call.CalleeSourceSite.SourcePath, function = call.CalleeSourceSite.FunctionSymbol,
                    line = call.CalleeSourceSite.SourceLine, blob = call.CalleeSourceSite.SourceGitBlob },
                sameCue = ReferenceEquals(call.OriginalCue, dispatch.OriginalCue),
                sameContext = ReferenceEquals(call.C2Context, dispatch.C2Context),
                output = "actual transport/cinematic recording consumer; PCM/DSP playback unqualified",
                pcmOrDspPlayback = false
            }, 32);
        }
    }

    private void ObserveStep(IGameState state)
    {
        HudProcess.Observe(Game);
        if (Game.SourceTick > lastSourceTick)
        {
            DataCatalog.Require(Game.SourceTick == lastSourceTick + 1,
                "The host skipped a source wake; source HUD/camera ordering cannot be reconstructed per render.");
            SourceWakes++;
            lastSourceTick = Game.SourceTick;
            hudClock.Advance(lastSourceTick, HudProcess.CommittedHud.ClockWarning,
                HudProcess.CommittedHud.StatusProcessSuspended);
        }
        Camera.ObserveHostStep(state);
        Fire.Observe(Game);
        foreach (var value in Game.ContactEvents)
        {
            string kind = value.Kind.ToString();
            contactCounts[kind] = contactCounts.GetValueOrDefault(kind) + 1;
            Enqueue(contacts, new {
                sourceTick = value.SourceTick, processOrder = value.ProcessOrder,
                kind, actor = value.ActorPlayerId, other = value.OtherPlayerId,
                script = value.ScriptSymbol, attackMode = value.AttackMode
            }, 64);
        }
    }

    public void Capture(double[] axes, int[] buttons)
    {
        DataCatalog.Require(axes is { Length: 8 } && buttons is { Length: 4 } &&
            axes.All(double.IsFinite) && axes.All(value => value is >= -1 and <= 1) &&
            buttons.All(value => value is >= 0 and <= 7),
            "All four raw browser cabinet slots need finite normalized axes and A/B/C masks.");
        var stations = Data.Configuration.Match.Stations;
        var samples = new SourceCabinetSample[stations.Count];
        for (int i = 0; i < samples.Length; i++)
        {
            int id = stations[i].StationId;
            var stick = quantizer.Analog((float)axes[id * 2], (float)axes[id * 2 + 1]);
            SourceCommand command = !input.WaitingForNeutral && commands[id].Count > 0 ?
                commands[id].Dequeue() : default;
            samples[i] = new SourceCabinetSample(id, stick, (SourceButtons)buttons[id], command);
        }
        input.Capture(samples);
        InputSamples++;
        if (!lastAxes.SequenceEqual(axes) || !lastButtons.SequenceEqual(buttons)) InputChanges++;
        Array.Copy(axes, lastAxes, axes.Length);
        Array.Copy(buttons, lastButtons, buttons.Length);
    }

    public void Continue(int stationId)
    {
        DataCatalog.Require(Data.Configuration.Match.Stations.Any(s => s.StationId == stationId),
            "Continue must belong to an actual configured cabinet station.");
        DataCatalog.Require(commands[stationId].Count < 8, "Cabinet command backlog exceeded.");
        commands[stationId].Enqueue(new SourceCommand(SourceAction.Continue));
    }

    public void SetActive(bool active)
    {
        if (Active == active) return;
        Active = active;
        input.Clear();
        selection.ClearPendingInput();
        foreach (var pending in commands) pending.Clear();
        Bridge.SetActive(active);
        Array.Clear(lastButtons);
        Array.Clear(lastAxes);
    }

    public void Restart() => Bridge.Restart();

    private void ResetPresentation()
    {
        Epoch++;
        input.Clear();
        selection.Clear();
        hudClock.Clear();
        HudProcess = new OriginalHudProcess(Data.Hud, Game);
        hudClock.Advance(0, HudProcess.CommittedHud.ClockWarning);
        Camera = new PreviewCamera(Data, Assets, Game);
        Fire = new FirePresentation(Data.Fire, Options.FireEnabled, sink, Epoch, uploadTextures: false);
        lastSourceTick = 0;
        events.Clear();
        contacts.Clear();
        dustCues.Clear();
        eventCounts.Clear();
        contactCounts.Clear();
        SourceWakes = SoundSelections = UnqualifiedSoundCalls = UnqualifiedVisualCallbacks = 0;
        DustCallbacks = BoundDustCallbacks = 0;
        PreSnapSwitchSoundSelections = 0;
        hopSounds.Clear();
        hopSoundTransport.Reset();
        foreach (var pending in commands) pending.Clear();
    }

    public int Advance(double elapsedSeconds, int bufferWidth, int bufferHeight, double cssWidth, double cssHeight, double dpr)
    {
        int steps = Bridge.AdvanceSource(elapsedSeconds, input.NextHostFrame);
        var viewport = new SourceHudViewport(bufferWidth, bufferHeight, cssWidth, cssHeight, dpr);
        Render(viewport);
        return steps;
    }

    private void Render(SourceHudViewport viewport)
    {
        IGameState state = Game.State, previous = Bridge.PreviousState;
        DataCatalog.Require(state.Players.Count == 14 && state.Players.Select(a => a.Id).Distinct().Count() == 14 &&
            state.Players.Count(a => a.Team == TeamId.Home) == 7 && state.Players.Count(a => a.Team == TeamId.Away) == 7,
            "A live preview requires all fourteen actual production actors on both teams.");
        var actorStatus = new object[14];
        var indicators = new List<(int Station, SourceVector3 Head)>();
        foreach (PlayerView actor in state.Players)
        {
            PlayerView old = previous.Players.First(p => p.Id == actor.Id);
            var binding = Assets.Bind(actor, state);
            var pose = SourceActorPoseSampler.Evaluate(Data.Animations, Data.ReferencePivots,
                old, actor, Bridge.RenderInterpolation01);
            SourceVector3 root = SourceActorPoseSampler.CanInterpolate(old, actor) ?
                SourceAnimationSampling.Lerp(FrameMath.Root(old), Game.SourceActorWorldPosition(actor.Id), Bridge.RenderInterpolation01) :
                Game.SourceActorWorldPosition(actor.Id);
            for (int joint = 0; joint < pose.Joints.Count; joint++)
                FrameMath.Joint(joints, (actor.Id * 15 + joint) * 16, pose.Joints[joint], root, pose.ModelScale);
            modelIds[actor.Id] = binding.Appearance.Model.Index;
            appearanceIds[actor.Id] = binding.Id;
            if (actor.Source.ControllerStationId >= 0)
            {
                var head = pose.Joints[Data.Rosters.ActorModels.HeadJointIndex].Position;
                indicators.Add((actor.Source.ControllerStationId,
                    new SourceVector3(root.X + head.X, root.Y + head.Y, root.Z - head.Z)));
            }
            actorStatus[actor.Id] = new {
                id = actor.Id, team = actor.Team.ToString(), sourcePlayerId = binding.Appearance.SourcePlayerId,
                rosterTeam = actor.Source.Roster.TeamIndex, rosterRecord = actor.Source.Roster.RosterIndex,
                position = new[] { actor.Position.X, actor.Height, actor.Position.Z },
                model = binding.Appearance.Model.Index, appearance = binding.Id,
                uniform = binding.Appearance.UniformIdentity,
                uniformHash = Data.Teams.Textures.First(t => t.Id == binding.Appearance.Uniform.Slots[2].Id).Sha256,
                textureSlots = binding.Appearance.Uniform.Slots.Select(slot => slot.Id).ToArray(),
                number = binding.Appearance.Player.Profile.PackedJerseyNumber,
                script = actor.Animation.Primary.ScriptSymbol, clip = actor.Animation.Primary.Sample.SequenceName,
                frame = actor.Animation.Primary.Sample.SourceFrame, animationSourceTick = actor.Animation.SourceTick,
                secondary = actor.Animation.Secondary.ScriptSymbol, action = actor.Animation.Action.ToString(),
                poseDigest = PoseDigest(pose), handicap = actor.Source.Handicap, rawHandicap = actor.Source.RawHandicap,
                controller = actor.Source.ControllerStationId, tint = "none",
                rawFlags = actor.Source.Presentation.RawFlags,
                flashWhite = new SourceFirePlayerFlashState(actor.Source.Presentation.RawFlags).White,
                headVariant = binding.Appearance.CustomHead != null ? "original-station-custom" :
                    binding.Appearance.PoppedHead != null ? "original-phead-source-authored" : "original-helmeted",
                headSourceSha256 = binding.Appearance.PoppedHead != null ? SourceTeamPoppedHeadData.SourceSha256 : null,
                headGeometrySha256 = binding.Appearance.PoppedHead != null ? SourceTeamPoppedHeadData.HeaderSha256 : null
            };
        }
        var hudDraw = new List<SourceHudPrimitive>(hud.Compose(HudProcess.CommittedState, viewport, selection, hudClock.RedClock, !Active));
        foreach (var indicator in indicators)
        {
            if (state.Phase is GamePhase.PreSnap or GamePhase.LiveBall &&
                Project(Camera.ViewProjection, indicator.Head, viewport, out double x, out double y))
                hudDraw.AddRange(hud.Indicator(indicator.Station, x, y, viewport, Data.Configuration.Match.Stations.Count));
        }
        var hudVertices = new double[hudDraw.Sum(p => p.Vertices.Count) * 4];
        int cursor = 0;
        foreach (var draw in hudDraw)
        foreach (var vertex in draw.Vertices)
        {
            hudVertices[cursor++] = vertex.X;
            hudVertices[cursor++] = vertex.Y;
            hudVertices[cursor++] = vertex.S;
            hudVertices[cursor++] = vertex.T;
        }
        SourceVector3 ball = Game.SourceBallWorldPosition;
        SourceFreeBallDraw ballDraw = SourceFreeBallBinding.Capture(Game);
        double[] ballPacket = FrameMath.FreeBall(ballDraw);
        var ballTrails = SourceFreeBallBinding.CaptureTrails(Game);
        var match = ((ISourceMatchState)state).Match;
        var source = (ISourceGameState)state;
        Frames++;
        Fire.Send(state, Camera.CameraSpaceProjection, hudDraw, HudProcess.CommittedHud.KickMeter);
        var kicking = (ISourceKickMeterState)state;
        LastStatus = JsonSerializer.Serialize(new {
            schema = "blitz-portable-source-player-frame/v1", runtimeFlavor = RuntimeFlavor,
            kernel = KernelBinding.Describe(Game), profile = Data.Animations.InventorySha256,
            resourceProfile = Data.Catalog.Profile.StableId,
            animationLibrary = new { sha256 = Data.Animations.InventorySha256,
                clips = Data.Animations.SequenceNames.Count, frames = Data.Animations.FrameCount },
            callbacks = new { profile = Data.Callbacks.Profile, sha256 = Data.Callbacks.PayloadSha256,
                bindings = Data.Callbacks.Bindings.Count },
            defensivePreSnapSwitchEnabled = Game.SourceDefensivePreSnapSwitchEnabled,
            manifestSha256 = Data.Catalog.ManifestSha256,
            hostTick = state.Tick, sourceTick = Game.SourceTick, sourceTicksPerSecond = source.SourceTicksPerSecond,
            epoch = Epoch,
            menu = MenuState(state),
            sourceWakes = SourceWakes, frames = Frames, phase = state.Phase.ToString(),
            matchPhase = match.Phase.ToString(), playKind = match.PlayKind.ToString(),
            playNumber = state.PlayNumber, possession = state.Possession.ToString(),
            selectedPlays = new { home = match.HomePlay, away = match.AwayPlay, match.OffenseFlipped },
            lastOutcome = state.LastOutcome.ToString(),
            teams = new { home = match.HomeGameplay, away = match.AwayGameplay },
            clock = HudProcess.CommittedHud.Clock, packedClock = ((ISourceMatchState)state).PackedGameTime,
            homeScore = state.HomeScore, awayScore = state.AwayScore, down = state.Down,
            lineOfScrimmage = state.LineOfScrimmage, firstDownLine = state.FirstDownLine, offenseDirection = state.OffenseDirection,
            actors = actorStatus, actorCount = state.Players.Count, jointsPerActor = 15,
            ball = new { mode = state.Ball.Mode.ToString(), carrier = state.Ball.CarrierId, target = state.Ball.TargetPlayerId,
                position = new[] { ball.X, ball.Y, ball.Z }, kind = state.Ball.Kind.ToString(),
                sourceTick = ballDraw.State.SourceTick, visible = ballDraw.State.Visible,
                heading = ballDraw.State.Heading, elevation = ballDraw.State.Elevation, roll = ballDraw.State.Roll,
                scale = ballDraw.State.Scale, pupBigBall = ballDraw.State.PupBigBall, rawFlags = ballDraw.State.RawFlags,
                alpha = ballDraw.Alpha, alternate = ballDraw.Alternate, white = ballDraw.White,
                worldMatrix = ballDraw.WorldMatrix, packet = ballPacket,
                sourceOrigin = SourceFreeBallData.Origin },
            ballTrails = ballTrails.Select(draw => new {
                objectSerial = draw.Trail.Value.ObjectSerial, processSerial = draw.Trail.Value.ProcessSerial,
                stage = draw.Trail.Value.Stage, hidden = draw.Trail.Value.Hidden,
                objectId = draw.Trail.Value.ObjectId, drawOrder = draw.Trail.Value.DrawOrder,
                localFlags = draw.Trail.Value.LocalFlags,
                sourceTick = draw.State.SourceTick, visible = draw.State.Visible,
                position = new[] { draw.State.Position.X, draw.State.Position.Y, draw.State.Position.Z },
                heading = draw.State.Heading, elevation = draw.State.Elevation, roll = draw.State.Roll,
                scale = draw.State.Scale, pupBigBall = draw.State.PupBigBall, rawFlags = draw.State.RawFlags,
                alpha = draw.Alpha, effectiveAlpha = draw.EffectiveAlpha, white = draw.White, alternate = draw.Alternate,
                entry = new {
                    sourceTick = draw.Trail.Value.EntrySample.SourceTick,
                    position = new[] { draw.Trail.Value.EntrySample.WorldPosition.X,
                        draw.Trail.Value.EntrySample.WorldPosition.Y, draw.Trail.Value.EntrySample.WorldPosition.Z },
                    heading = draw.Trail.Value.EntrySample.Heading, elevation = draw.Trail.Value.EntrySample.Elevation,
                    roll = draw.Trail.Value.EntrySample.Roll, scale = draw.Trail.Value.EntrySample.Scale,
                    rawFlags = draw.Trail.Value.EntrySample.RawFlags, provenance = draw.Trail.Value.EntrySample.Provenance
                },
                worldMatrix = draw.WorldMatrix, packet = FrameMath.FreeBall(draw),
                sourceOrigin = SourceFreeBallData.Origin
            }).ToArray(),
            camera = new {
                sourceTick = Camera.LastSourceTick, suspended = Camera.Suspended,
                sourceHeading = Game.SourceCameraHeading, headingSampleTick = Game.SourceCameraHeadingSampleTick,
                position = new[] { Camera.Current.Position.X, Camera.Current.Position.Y, Camera.Current.Position.Z },
                transform = Camera.Current.Transform, cueCount = Camera.CueCount,
                transient = Camera.Playback.IsTransientActive, sourceHres = Data.Bootstrap.HorizontalResolution,
                sourceVres = Data.Bootstrap.VerticalResolution, cameraTrace = Camera.Trace,
                transientTable = Camera.Playback.Frame.Table, transientOrdinal = Camera.Playback.Frame.Ordinal,
                transientElapsed = Camera.Playback.Frame.ElapsedTicks, resuming = Camera.Playback.IsResumingNormal,
                needsShakeDraws = Camera.Playback.NeedsShakeDraws,
                producer = "original-active-CAMBOT-normal-kickoff-impact",
                normalWakes = Camera.Playback.OriginalNormal.NormalWakes,
                rawGameMode = Camera.Playback.LastCameraBoundary == null ? -1 : Camera.Playback.LastCameraBoundary.RawGameMode,
                focus = new[] { Camera.Playback.SourceState.Focus.X, Camera.Playback.SourceState.Focus.Y,
                    Camera.Playback.SourceState.Focus.Z }, distance = Camera.Playback.SourceState.Distance,
                viewProjection = Camera.ViewProjection, cameraSpaceProjection = Camera.CameraSpaceProjection
            },
            possessionServices = new {
                ordering = "portable declared Core/FX slots; not a native global scheduler witness",
                cameraSleeping = ((ISourcePossessionState)state).CameraProcessSleeping,
                retainedOutputTick = ((ISourcePossessionState)state).CameraOutputSourceTick,
                previousOffense = Camera.Playback.PossessionOffensePrevious,
                timeout = Camera.Playback.PossessionTimeout,
                message = ((ISourcePossessionState)state).PossessionMessage,
                messages = ((ISourcePossessionState)state).PossessionMessages,
                processes = ((ISourcePossessionState)state).Processes,
                speech = Game.SourcePossessionSpeechSubmissions,
                hudRepresentation = "exact logical pair; complete original WIF union while both flash states agree; native Composit packing unqualified"
            },
            sourceRandom = new {
                evidence = Game.SourceRandomService.Provenance.Evidence.ToString(),
                tail = Game.SourceRandomService is SourceInjectedRandom ledger ?
                    ledger.Trace.Skip(Math.Max(0, ledger.Trace.Count - 64)).Select(draw => new {
                        order = draw.Order, sourceTick = draw.Site.SourceTick, actor = draw.Site.ActorId,
                        process = draw.Site.Process, processOrder = draw.Site.ProcessOrder,
                        callIndex = draw.Site.CallIndex, site = draw.Site.SourceIdentity,
                        bound = draw.Site.BoundOrModulus, value = draw.Value
                    }).ToArray() : Array.Empty<object>()
            },
            hud = new { primitives = hudDraw.Count, vertices = hudVertices.Length / 4,
                origin = "original authored atlas/font/draw composer; portable raster origin",
                committedSourceTick = HudProcess.CommittedSourceTick, resultPending = HudProcess.ResultPending,
                pendingResultWakes = HudProcess.PendingResultWakes,
                process = HudProcess.ResultPending ? "Actual C2 result queued; last committed authored HUD held for one source wake" : "Actual committed original HUD state",
                elements = hudDraw.Select(p => p.Element).Distinct().ToArray() },
            assets = new { meshes = Assets.MeshCount, textures = Assets.TextureCount,
                worldDraws = Assets.WorldDrawCount, appearances = Assets.AppearanceCount,
                poppedHead = Data.PoppedHead == null ? null : new {
                    origin = SourceTeamPoppedHeadData.Origin, sourceSha256 = SourceTeamPoppedHeadData.SourceSha256,
                    geometrySha256 = SourceTeamPoppedHeadData.HeaderSha256, manifestSha256 = Data.PoppedHead.ContentSha256,
                    headJoint = SourceTeamPoppedHeadData.HeadJoint, rawTextureCode = SourceTeamPoppedHeadData.RawTextureCode,
                    logicalTextureSlot = SourceTeamPoppedHeadData.LogicalTextureSlot, nativeHeaderEquivalent = false
                } },
            input = new { samples = InputSamples, changes = InputChanges, pending = input.PendingFrames,
                waitingForNeutral = input.WaitingForNeutral, buttons = lastButtons, axes = lastAxes },
            active = Active, events = events.ToArray(), eventCounts, contacts = contacts.ToArray(), contactCounts,
            audio = new { output = "Audio output unqualified", selections = SoundSelections,
                unplayedRawCalls = UnqualifiedSoundCalls, preSnapSwitchSelections = PreSnapSwitchSoundSelections,
                resolvedHopSelections = hopSounds.ToArray() },
            effects = Fire.Describe(),
            visualCallbacks = new { dust = DustCallbacks, boundDust = BoundDustCallbacks,
                unqualified = UnqualifiedVisualCallbacks,
                cues = dustCues.ToArray(),
                qualification = "Original source processes and authored DIRT rendering; not native pixels or target PRNG." },
            kick = new { mode = kicking.KickMeterMode.ToString(), meter = kicking.KickMeter,
                lastLaunch = kicking.LastKickLaunch, metrics = kicking.KickMeterMetrics,
                origin = "portable-authored HUD metrics; NOT native Composit/generated dimensions" },
            limitations = new[] {
                "Not a Unity player build, native Unity qualification or bit-exact arcade acceptance.",
                "Authored TGA/portable HUD raster origin; missing Texus/WMS quantization is not reconstructed.",
                "Original active CAMBOT normal/tracking/integrator, kickoff and special-return source stages; " +
                    "host comparisons are separate from native fast trig/global scheduler/initial QB-box/possession-message services.",
                "Original medball2 SDL/HEDSHU and source-entry football ghosts; native generated header/FMA/WBUFFER/pixels/global scheduler remain unqualified.",
                "Production default preview RNG is explicitly unverified against the historical platform PRNG.",
                "No PCM/DSP output or synthetic sound. Dust callbacks are not substituted with generic particles.",
                KernelBinding.IsPortable(Game) ? "Portable repairs are explicit; StrictSource is not silently changed." :
                    "The required portable factory option is not bound: not player-ready."
            }
        });
        sink.Present(Camera.ViewProjection, joints, modelIds, appearanceIds,
            FrameMath.FreeBalls(ballDraw, ballTrails),
            hudVertices, hudDraw.Select(p => unchecked((int)p.Color)).ToArray(),
            hudDraw.Select(p => "hud/" + p.Texture).ToArray(), hudDraw.Select(p => p.Vertices.Count).ToArray(), LastStatus);
    }

    private static bool Project(double[] matrix, SourceVector3 point, SourceHudViewport viewport, out double x, out double y)
    {
        double w = matrix[3] * point.X + matrix[7] * point.Y + matrix[11] * point.Z + matrix[15];
        x = y = 0;
        if (w <= 0) return false;
        double nx = (matrix[0] * point.X + matrix[4] * point.Y + matrix[8] * point.Z + matrix[12]) / w;
        double ny = (matrix[1] * point.X + matrix[5] * point.Y + matrix[9] * point.Z + matrix[13]) / w;
        if (Math.Abs(nx) > 1 || Math.Abs(ny) > 1) return false;
        x = (nx + 1) * viewport.ContentWidth / 2;
        y = (1 - ny) * viewport.ContentHeight / 2;
        return true;
    }

    private object MenuState(IGameState state)
    {
        if (state.Phase != GamePhase.PlaySelect) return null;
        object Side(TeamId side) => new {
            page = selection.Page(side), cursor = selection.Cursor(side), selected = selection.Selected(side),
            slots = Enumerable.Range(0, 9).Select(i => {
                var slot = selection.Slot(state, side, selection.Page(side), i);
                return new { index = i, name = slot.Name, symbol = slot.PlaySymbol };
            }).ToArray()
        };
        SourceMatchView match = ((ISourceMatchState)state).Match;
        return new { home = Side(TeamId.Home), away = Side(TeamId.Away),
            countdown = match.SelectionCountdown, timer = match.SelectionTimer };
    }

    private static string PoseDigest(SourceEvaluatedPose pose)
    {
        ulong hash = 14695981039346656037;
        foreach (var joint in pose.Joints)
            foreach (float value in new[] { joint.Position.X, joint.Position.Y, joint.Position.Z,
                joint.Rotation.X, joint.Rotation.Y, joint.Rotation.Z, joint.Rotation.W })
                hash = unchecked((hash ^ BitConverter.SingleToUInt32Bits(value)) * 1099511628211);
        return hash.ToString("x16");
    }

    private static void Enqueue(Queue<object> queue, object value, int maximum)
    {
        queue.Enqueue(value);
        while (queue.Count > maximum) queue.Dequeue();
    }
}
