using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public enum SourceCinematicMode
    {
        Zoom = 4, ZoomNoRange = 5, ZoomNoFacing = 6,
        Spline = 7, SplineNoFacing = 8, SplineCameraTarget = 9,
        Rail = 10, RailNoFacing = 11, RailNoTracking = 12, CourseCameraTarget = 17, PointToPoint = 19
    }

    public sealed class SourceCinematicEntry
    {
        public string Table { get; }
        public int Ordinal { get; }
        public int SourceLine { get; }
        public int RawMode { get; }
        public string ModeSymbol { get; }
        public bool IsNoSelection { get; }
        public SourceCinematicMode Mode { get { return (SourceCinematicMode)RawMode; } }
        public int ModeTicks { get; }
        public int TravelTicks { get; }
        public float ThetaDegrees { get; }
        public float PhiDegrees { get; }
        public float Distance { get; }
        public ReadOnlyCollection<SourceVector3> ControlPoints { get; }

        public SourceCinematicEntry(string table, int ordinal, int sourceLine, int mode, string modeSymbol,
            int modeTicks, int travelTicks, float thetaDegrees, float phiDegrees, float distance,
            IReadOnlyList<SourceVector3> controlPoints, bool noSelection = false)
        {
            RosterGuard.Identifier(table); RosterGuard.Identifier(modeSymbol);
            RosterGuard.Require(ordinal >= 0 && ordinal < 256 && sourceLine > 0 && modeTicks >= 0 &&
                modeTicks <= 100000 && travelTicks >= 0 && travelTicks <= 100000 &&
                Finite(thetaDegrees) && Math.Abs(thetaDegrees) <= 360f &&
                Finite(phiDegrees) && Math.Abs(phiDegrees) < 90f && Finite(distance) && distance >= 0f && distance <= 2000f,
                "Invalid original camera entry/timing/angles.");
            if (!noSelection)
                RosterGuard.Require((mode >= 4 && mode <= 12 || mode == 17 || mode == 19) && modeTicks > 0,
                    "Only implemented source zoom/spline/rail/course17 or original kickoff point-stream modes are supported.");
            if (controlPoints == null) throw new ArgumentNullException(nameof(controlPoints));
            int expected = mode == 17 || mode == 19 ? controlPoints.Count : noSelection || mode <= 6 ? 0 : mode <= 9 ? 2 : 4;
            RosterGuard.Require(controlPoints.Count == expected, "Original camera mode/control-point shape disagrees.");
            if (mode == 19)
                RosterGuard.Require(expected >= 2 && expected <= 2000 &&
                    controlPoints[expected - 1].X == 0f && controlPoints[expected - 1].Y == 0f &&
                    controlPoints[expected - 1].Z == 0f && controlPoints[expected - 2].Z != 0f,
                    "Original kickoff point stream requires its bounded zero terminator and nonzero unit endpoint.");
            if (mode == 17)
                RosterGuard.Require(expected >= 2 && expected <= 2000 &&
                    controlPoints[expected - 1].X == 0f && controlPoints[expected - 1].Y == 0f &&
                    controlPoints[expected - 1].Z == 0f &&
                    controlPoints[0].X * controlPoints[0].X + controlPoints[0].Z * controlPoints[0].Z > 0f &&
                    controlPoints[0].Y != 0f,
                    "Original CAM_CO_C requires its actual bounded point stream and nonsingular first point.");
            var copy = new SourceVector3[controlPoints.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceVector3 p = controlPoints[i];
                RosterGuard.Require(Finite(p.X) && Finite(p.Y) && Finite(p.Z), "Nonfinite original camera control point.");
                copy[i] = p;
            }
            Table = table; Ordinal = ordinal; SourceLine = sourceLine; RawMode = mode; ModeSymbol = modeSymbol;
            IsNoSelection = noSelection; ModeTicks = modeTicks; TravelTicks = travelTicks;
            ThetaDegrees = thetaDegrees; PhiDegrees = phiDegrees; Distance = distance;
            ControlPoints = Array.AsReadOnly(copy);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    public sealed class SourceResultCourseCameraData
    {
        public string ContentSha256 { get; }
        public ReadOnlyCollection<SourceCinematicEntry> Entries { get; }
        private SourceResultCourseCameraData(string hash, List<SourceCinematicEntry> entries)
        { ContentSha256 = hash; Entries = entries.AsReadOnly(); }

        public static SourceResultCourseCameraData Read(byte[] bytes)
        {
            RosterGuard.Require(bytes != null && bytes.Length <= 512 * 1024 &&
                RosterGuard.Sha256(bytes) == SourceTeamResourceProfile.CompleteAv306CourseSha256,
                "The distinct authenticated original result-course payload is required.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(bytes), "original result-course camera");
            RosterGuard.Require(root.Text("schema") == "original-result-course-camera/v1" &&
                root.Text("profile") == SourceTeamResourceProfile.CompleteAv306StableId &&
                root.Text("source_revision") == SourceTeamRosterLibrary.PinnedSourceRevision &&
                root.Text("source_tree") == SourceTeamRosterLibrary.PinnedSourceTree &&
                root.Text("table") == "csi_cd_endplay",
                "Original result-course source/profile/table identity changed.");
            root.ExactInteger("tick_rate", 57);
            RosterGuard.Require(root.Text("units") == "original centered source-world XYZ; authored points unscaled",
                "Original result-course points must retain source coordinates.");
            var entries = new List<SourceCinematicEntry>();
            foreach (object raw in root.Array("entries"))
            {
                RosterObject entry = RosterJson.Object(raw, "original result-course entry");
                int ordinal = entry.Integer("ordinal", 0, 6), line = entry.Integer("source_line", 1, 100000);
                int mode = entry.Integer("mode", 17, 23);
                string symbol = entry.Text("mode_symbol");
                bool none = entry.Boolean("no_selection");
                int total = entry.Integer("mode_ticks", 0, 700), travel = entry.Integer("travel_ticks", 0, 0);
                float theta = (float)entry.Number("theta_degrees", 0, 0);
                float phi = (float)entry.Number("phi_degrees", 0, 0), distance = (float)entry.Number("distance", 0, 0);
                object[] points = entry.Array("control_points");
                var controls = new List<SourceVector3>();
                foreach (object point in points)
                {
                    object[] values = RosterJson.Array(point);
                    RosterGuard.Require(values.Length == 3, "Original course point must have XYZ components.");
                    controls.Add(new SourceVector3((float)RosterJson.Number(values[0], -10000, 10000),
                        (float)RosterJson.Number(values[1], -10000, 10000),
                        (float)RosterJson.Number(values[2], -10000, 10000)));
                }
                entry.Done();
                RosterGuard.Require(ordinal == entries.Count && (ordinal == 0
                    ? none && mode == 23 && symbol == "NMODE_NOCSI" && total == 0 && controls.Count == 0
                    : !none && mode == 17 && symbol == "CAM_CO_C" && total == 700 && controls.Count == 301),
                    "Original result-course requires its ordered NOP and six actual301-point CAM_CO_C entries.");
                entries.Add(new SourceCinematicEntry("csi_cd_endplay", ordinal, line, mode, symbol,
                    total, travel, theta, phi, distance, controls, none));
            }
            RosterGuard.Require(entries.Count == 7, "All seven original course table entries are required.");
            var expected = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "CamData.c", "4bfc54c6b9633b076f01163c2142c7fce799a0e0" },
                { "include/camdefs.h", "8586d537afe10067e110722f6ba42bb3f9f07952" },
                { "include/GAME.H", "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3" },
                { "include/ani3d.h", "826c6322467fcfe503b8402b540e4731671f9b6b" }
            };
            foreach (object raw in root.Array("inputs"))
            {
                RosterObject input = RosterJson.Object(raw, "original course input");
                string path = input.Path("path"), blob = input.Text("git_blob_sha1", 40);
                string hash = input.Text("sha256", 64);
                input.Integer("bytes", 1, 4 * 1024 * 1024); input.Done();
                RosterGuard.Require(expected.ContainsKey(path) && expected[path] == blob, "Original course Git blob differs.");
                RosterGuard.Hex(hash, 64, "course original input SHA256"); expected.Remove(path);
            }
            RosterGuard.Require(expected.Count == 0, "Original course definition inputs are incomplete.");
            root.Done();
            return new SourceResultCourseCameraData(RosterGuard.Sha256(bytes), entries);
        }
    }

    public sealed class SourceCinematicCueDefinition
    {
        public string Script { get; }
        public int Line { get; }
        public string Kind { get; }
        public ReadOnlyCollection<string> Arguments { get; }
        internal SourceCinematicCueDefinition(string script, int line, string kind, string[] arguments)
        { Script = script; Line = line; Kind = kind; Arguments = Array.AsReadOnly(arguments); }
    }

    public sealed class SourceCinematicCameraBinding
    {
        public string Script { get; }
        public int OpcodeLine { get; }
        public int OperandLine { get; }
        public string Table { get; }
        internal SourceCinematicCameraBinding(string script, int opcode, int operand, string table)
        { Script = script; OpcodeLine = opcode; OperandLine = operand; Table = table; }
    }

    public sealed class SourceCinematicLibrary
    {
        private SourceOriginalSoundBindings originalSoundBindings;
        public string ContentSha256 { get; }
        public ReadOnlyDictionary<string, ReadOnlyCollection<SourceCinematicEntry>> Tables { get; }
        public ReadOnlyCollection<float> ShakerExponent { get; }
        public ReadOnlyCollection<SourceCinematicCueDefinition> AnimationCues { get; }
        public ReadOnlyCollection<SourceCinematicCueDefinition> ContactCameraSites { get; }
        public ReadOnlyCollection<string> Qualifications { get; }
        public string ContactAnimationManifestSha256 { get; }
        public string ContactProgramsSha256 { get; }
        public string C2CallbackDataSha256 { get; }
        public string PreSnapSwitchMovementProgramsSha256 { get; }
        public ReadOnlyCollection<SourceCinematicCameraBinding> CameraOperandBindings { get; }
        public SourceResultCourseCameraData CourseCamera { get; private set; }

        public SourceCinematicLibrary WithResultCourse(SourceResultCourseCameraData course)
        {
            if (course == null || course.ContentSha256 != SourceTeamResourceProfile.CompleteAv306CourseSha256 ||
                ContentSha256 != SourceTeamResourceProfile.CompleteAv306ImpactSha256 ||
                ContactAnimationManifestSha256 != SourceTeamResourceProfile.CompleteAv306AnimationSha256 ||
                C2CallbackDataSha256 != SourceTeamResourceProfile.CompleteAv306CallbackSha256)
                throw new InvalidOperationException("The original result course belongs only to its distinct306 impact/callback capability.");
            var tables = new Dictionary<string, ReadOnlyCollection<SourceCinematicEntry>>(Tables, StringComparer.Ordinal);
            tables.Add("csi_cd_endplay", course.Entries);
            var result = new SourceCinematicLibrary(ContentSha256, tables, ShakerExponent.ToArray(),
                AnimationCues.ToArray(), ContactCameraSites.ToArray(), Qualifications.ToArray(),
                ContactAnimationManifestSha256, ContactProgramsSha256, CameraOperandBindings.ToArray(),
                C2CallbackDataSha256, PreSnapSwitchMovementProgramsSha256);
            result.CourseCamera = course;
            return result;
        }

        private SourceCinematicLibrary(string hash, Dictionary<string, ReadOnlyCollection<SourceCinematicEntry>> tables,
            float[] shaker, SourceCinematicCueDefinition[] cues, SourceCinematicCueDefinition[] contacts, string[] qualifications,
            string contactAnimation, string contactPrograms, SourceCinematicCameraBinding[] cameraBindings, string c2Data,
            string preSnapSwitchMovementPrograms)
        {
            ContentSha256 = hash;
            Tables = new ReadOnlyDictionary<string, ReadOnlyCollection<SourceCinematicEntry>>(tables);
            ShakerExponent = Array.AsReadOnly(shaker); AnimationCues = Array.AsReadOnly(cues);
            ContactCameraSites = Array.AsReadOnly(contacts);
            Qualifications = Array.AsReadOnly(qualifications);
            ContactAnimationManifestSha256 = contactAnimation; ContactProgramsSha256 = contactPrograms;
            CameraOperandBindings = Array.AsReadOnly(cameraBindings);
            C2CallbackDataSha256 = c2Data;
            PreSnapSwitchMovementProgramsSha256 = preSnapSwitchMovementPrograms;
        }

        public SourceCinematicEntry Selected(string originalTable, int actualSelectedOrdinal)
        {
            ReadOnlyCollection<SourceCinematicEntry> table;
            if (!Tables.TryGetValue(originalTable, out table) || actualSelectedOrdinal < 0 || actualSelectedOrdinal >= table.Count)
                throw new InvalidOperationException("Missing original camera table/actual randrng ordinal: " +
                    originalTable + "[" + actualSelectedOrdinal + "]. No random substitute shot is selected.");
            return table[actualSelectedOrdinal];
        }

        public SourceCinematicCueDefinition RequireExecutedOpcode(string script, int originalLine, string kind)
        {
            foreach (SourceCinematicCueDefinition cue in AnimationCues)
                if (cue.Script == script && cue.Line == originalLine && cue.Kind == kind) return cue;
            if (kind == "callback" && (ContentSha256 == SourceTeamResourceProfile.CompleteOriginalImpactSha256 ||
                ContentSha256 == SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256 ||
                ContentSha256 == SourceTeamResourceProfile.CompleteAv306ImpactSha256))
            {
                if (originalSoundBindings == null)
                    originalSoundBindings = SourceOriginalSoundBindings.ForCompleteOriginalPresentation(this);
                foreach (SourceAudioVmBinding binding in originalSoundBindings.Bindings)
                    if (binding.FunctionSymbol == "hut_snd" && binding.ScriptSymbol == script && binding.SourceLine == originalLine)
                        return new SourceCinematicCueDefinition(script, originalLine, kind, new[] {
                            binding.FunctionSymbol, binding.Argument0.ToString(CultureInfo.InvariantCulture),
                            binding.Argument1.ToString(CultureInfo.InvariantCulture), binding.Argument2.ToString(CultureInfo.InvariantCulture) });
            }
            throw new InvalidOperationException("No original presentation opcode at plyrseq.c:" + originalLine +
                " in " + script + " (" + kind + "); a generic tackle event is not a presentation trigger.");
        }

        public SourceCinematicCueDefinition RequireContactSelection(string function, int originalLine)
        {
            foreach (SourceCinematicCueDefinition site in ContactCameraSites)
                if (site.Script == function && site.Line == originalLine) return site;
            throw new InvalidOperationException("No original contact camera selector at detect.c:" + originalLine +
                " in " + function + "; a generic tackle notification is not an eligible camera selection.");
        }

        public static SourceCinematicLibrary Read(byte[] json, string expectedSha256)
        {
            RosterGuard.Hex(expectedSha256, 64, "expected cinematic content SHA256");
            RosterGuard.Require(json != null && json.Length >= 2 && json.Length <= 1024 * 1024 &&
                RosterGuard.Sha256(json) == expectedSha256, "Original cinematic content pin mismatch.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(json), "original impact presentation");
            string schema = root.Text("schema");
            bool av = schema == "original-impact-complete-av306/v1";
            bool final = schema == "original-impact-complete-original303-final/v1" || av;
            bool complete = schema == "original-impact-complete-original303/v1" || final;
            bool preSnap = schema == "original-impact-contact282-c2-m1-presnap-switch/v1" || complete;
            bool m1 = preSnap || schema == "original-impact-contact281-c2-m1/v1";
            bool c2 = m1 || schema == "original-impact-contact281-c2/v1";
            bool contact = c2 || schema == "original-impact-contact272/v1";
            RosterGuard.Require((contact || schema == "original-impact-presentation/v1") &&
                root.Text("source_revision") == SourceTeamRosterLibrary.PinnedSourceRevision &&
                root.Text("source_tree") == SourceTeamRosterLibrary.PinnedSourceTree &&
                root.Text("profile") == "normal-release-57hz-no-debug-no-NOSETCAM",
                "Unknown original impact source profile.");
            root.ExactInteger("tick_rate", 57);
            int modes = root.Integer("camera_mode_count", 22, 22);
            var tables = new Dictionary<string, ReadOnlyCollection<SourceCinematicEntry>>(StringComparer.Ordinal);
            foreach (object rawTable in root.Array("tables"))
            {
                RosterObject table = RosterJson.Object(rawTable, "original camera table");
                string symbol = table.Text("symbol"); RosterGuard.Identifier(symbol);
                var entries = new List<SourceCinematicEntry>();
                foreach (object rawEntry in table.Array("entries"))
                {
                    RosterObject entry = RosterJson.Object(rawEntry, "original camera entry");
                    int ordinal = entry.Integer("ordinal", 0, 255), line = entry.Integer("source_line", 1, 100000);
                    int mode = entry.Integer("mode", 4, modes + 1);
                    string modeSymbol = entry.Text("mode_symbol");
                    int total = entry.Integer("mode_ticks", 0, 100000), travel = entry.Integer("travel_ticks", 0, 100000);
                    float theta = (float)entry.Number("theta_degrees", -360, 360);
                    float phi = (float)entry.Number("phi_degrees", -89.999, 89.999), distance = (float)entry.Number("distance", 0, 2000);
                    object[] points = entry.Array("control_points");
                    RosterGuard.Require(points.Length == 0 || points.Length == 6 || points.Length == 12,
                        "Invalid original camera control array width.");
                    var controls = new SourceVector3[points.Length / 3];
                    for (int i = 0; i < controls.Length; i++)
                        controls[i] = new SourceVector3((float)RosterJson.Number(points[i * 3], -10000, 10000),
                            (float)RosterJson.Number(points[i * 3 + 1], -10000, 10000),
                            (float)RosterJson.Number(points[i * 3 + 2], -10000, 10000));
                    entry.Done();
                    RosterGuard.Require(ordinal == entries.Count, "Original CSI weighted ordinals cannot be deduplicated/reordered.");
                    entries.Add(new SourceCinematicEntry(symbol, ordinal, line, mode, modeSymbol, total, travel,
                        theta, phi, distance, controls, mode >= modes));
                }
                table.Done();
                RosterGuard.Require(entries.Count > 0 && entries.Count <= 256 && !tables.ContainsKey(symbol),
                    "Missing/duplicate original camera table.");
                tables.Add(symbol, entries.AsReadOnly());
            }
            RosterGuard.Require(tables.Count >= 1 && tables.Count <= 64, "Invalid original camera table inventory.");
            double[] originalShaker = root.Numbers("shaker_exp", 64, 0.0001, 1);
            var shaker = new float[originalShaker.Length];
            for (int i = 0; i < shaker.Length; i++) shaker[i] = (float)originalShaker[i];
            var cues = new List<SourceCinematicCueDefinition>();
            var cueIds = new HashSet<int>();
            foreach (object rawCue in root.Array("animation_cues"))
            {
                RosterObject cue = RosterJson.Object(rawCue, "original animation presentation opcode");
                string script = cue.Text("script"); RosterGuard.Identifier(script);
                int line = cue.Integer("line", 1, 100000);
                string kind = cue.Text("kind");
                object[] args = cue.Array("arguments");
                var arguments = new string[args.Length];
                for (int i = 0; i < arguments.Length; i++)
                    arguments[i] = args[i] is string ? RosterJson.Text(args[i], 128, false) :
                        RosterJson.Integer(args[i], final ? -5 : 0, 100000).ToString(System.Globalization.CultureInfo.InvariantCulture);
                cue.Text("execution"); cue.Done();
                RosterGuard.Require(cueIds.Add(line) && (kind == "camera" && args.Length == 1 ||
                    kind == "shaker" && args.Length == 2 || kind == "callback" && args.Length == 4),
                    "Original presentation opcode identity/operands changed.");
                cues.Add(new SourceCinematicCueDefinition(script, line, kind, arguments));
            }
            foreach (object rawInput in root.Array("inputs"))
            {
                RosterObject input = RosterJson.Object(rawInput, "cinematic original input");
                input.Path("path"); input.Integer("bytes", 1, PlayerImportLimits.SourceBytes);
                RosterGuard.Hex(input.Text("git_blob_sha1", 40), 40, "cinematic Git blob");
                RosterGuard.Hex(input.Text("sha256", 64), 64, "cinematic source SHA256"); input.Done();
            }
            var contacts = new List<SourceCinematicCueDefinition>();
            foreach (object rawSite in root.Array("contact_camera_sites"))
            {
                RosterObject site = RosterJson.Object(rawSite, "original contact camera selector");
                string function = site.Text("function"); RosterGuard.Identifier(function);
                int line = site.Integer("line", 1, 100000);
                string table = site.Text("table"); RosterGuard.Identifier(table);
                site.Done();
                RosterGuard.Require(tables.ContainsKey(table), "Missing original contact camera table: " + table);
                contacts.Add(new SourceCinematicCueDefinition(function, line, "contact", new[] { table }));
            }
            string contactAnimation = null, contactPrograms = null, c2Data = null, movementPrograms = null;
            var cameraBindings = new List<SourceCinematicCameraBinding>();
            if (contact)
            {
                RosterObject binding = root.Object("contact_binding");
                contactAnimation = binding.Text("animation_manifest_sha256", 64);
                contactPrograms = binding.Text("programs_sha256", 64);
                RosterGuard.Hex(contactAnimation, 64, "C1 animation manifest");
                RosterGuard.Hex(contactPrograms, 64, "C1 source programs");
                if (c2)
                {
                    RosterGuard.Require(binding.Text("legacy_animation_manifest_sha256", 64) == SourceTeamResourceProfile.ContactAnimationSha256 &&
                        binding.Text("legacy_programs_sha256", 64) == SourceTeamResourceProfile.ContactProgramsSha256,
                        "C2 must retain the qualified C1 source/library lineage.");
                    c2Data = binding.Text("c2_payload_sha256", 64);
                    RosterGuard.Require(c2Data == (av ? SourceTeamResourceProfile.CompleteAv306CallbackSha256 :
                        complete ? SourceTeamResourceProfile.CompleteOriginalCallbackSha256 :
                        preSnap ? SourceInterfacePackagePlan.PreSnapSwitchCallbackSha256 :
                        m1 ? SourceInterfacePackagePlan.C2M1CallbackSha256
                        : SourceInterfacePackagePlan.C2CallbackSha256),
                        "C2 impact must bind its actual frozen callback data.");
                }
                if (preSnap)
                {
                    if (av)
                        RosterGuard.Require(binding.Text("base_complete_original303_catalog_sha256", 64) ==
                            SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256,
                            "CompleteAv306 must retain its exact final303 impact ancestor.");
                    if (complete)
                        RosterGuard.Require(binding.Text("base_presnap_switch_catalog_sha256", 64) ==
                            SourceTeamResourceProfile.PreSnapSwitchImpactSha256,
                            "CompleteOriginal303 retains the exact482-site original282 impact lineage.");
                    movementPrograms = binding.Text("presnap_switch_movement_programs_sha256", 64);
                    RosterGuard.Require(expectedSha256 == (av ? SourceTeamResourceProfile.CompleteAv306ImpactSha256 :
                            final ? SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256 :
                            complete ? SourceTeamResourceProfile.CompleteOriginalImpactSha256 :
                            SourceTeamResourceProfile.PreSnapSwitchImpactSha256) &&
                        contactAnimation == (av ? SourceTeamResourceProfile.CompleteAv306AnimationSha256 :
                            complete ? SourceTeamResourceProfile.CompleteOriginalAnimationSha256 :
                            SourceTeamResourceProfile.PreSnapSwitchAnimationSha256) &&
                        contactPrograms == SourceTeamResourceProfile.C2ProgramsSha256 &&
                        movementPrograms == SourceTeamResourceProfile.PreSnapSwitchMovementProgramsSha256 &&
                        binding.Text("base_m1_catalog_sha256", 64) == SourceTeamResourceProfile.C2M1ImpactSha256 &&
                        binding.Text("base_m1_animation_manifest_sha256", 64) == SourceTeamResourceProfile.C2AnimationSha256 &&
                        binding.Text("base_m1_callback_payload_sha256", 64) == SourceInterfacePackagePlan.C2M1CallbackSha256 &&
                        binding.Text("presnap_switch_manifest_sha256", 64) == SourceTeamResourceProfile.PreSnapSwitchHopInventorySha256,
                        "Pre-snap impact requires its separately frozen base/hop/library/callback and BOTH C2/Movement program generations.");
                }
                RosterGuard.Require(binding.Text("base_catalog_sha256", 64) ==
                    "794b1fa2865733677d08d5c465dad6a002966750a7d0523138f61f3a25be1fb3",
                    "C1 impact requires the actual complete original catalog generation.");
                foreach (object raw in binding.Array("camera_sites"))
                {
                    RosterObject site = RosterJson.Object(raw, "C1 exact camera instruction site");
                    string script = site.Text("script"), table = site.Text("table");
                    int opcode = site.Integer("opcode_line", 1, 100000), operand = site.Integer("operand_line", 1, 100000);
                    site.Done();
                    RosterGuard.Require(operand > opcode && operand <= opcode + 4 && tables.ContainsKey(table) &&
                        HasCue(cues, script, opcode, "camera", table) && HasCue(cues, script, operand, "camera", table),
                        "C1 camera must explicitly retain both exact original .byte opcode and .word operand identities.");
                    cameraBindings.Add(new SourceCinematicCameraBinding(script, opcode, operand, table));
                }
                if (final)
                {
                    RosterGuard.Require(binding.Text("base_complete_original_catalog_sha256", 64) ==
                        SourceTeamResourceProfile.CompleteOriginalImpactSha256,
                        "Final TD admission must retain the exact pre-TD complete catalog lineage.");
                    object[] tdSites = binding.Array("complete_original_td_camera_sites");
                    RosterGuard.Require(tdSites.Length == 11, "All eleven exact original TD camera WORD sites are required.");
                    foreach (object raw in tdSites)
                    {
                        RosterObject site = RosterJson.Object(raw, "original TD exact camera instruction");
                        string script = site.Text("script"), table = site.Text("table");
                        int opcode = site.Integer("opcode_line", 1, 100000), operand = site.Integer("operand_line", 1, 100000);
                        site.Done();
                        RosterGuard.Require(operand == opcode + 1 && tables.ContainsKey(table) &&
                            HasCue(cues, script, opcode, "camera", table) && HasCue(cues, script, operand, "camera", table),
                            "TD camera requires both authenticated original opcode and WORD identities.");
                        cameraBindings.Add(new SourceCinematicCameraBinding(script, opcode, operand, table));
                    }
                    object[] soundLines = binding.Array("complete_original_sound_lines");
                    RosterGuard.Require(soundLines.Length == 15, "The exact additional original TD/receiver sound sites are required.");
                    foreach (object raw in soundLines)
                    {
                        int line = RosterJson.Integer(raw, 1, 100000);
                        RosterGuard.Require(cues.Exists(cue => cue.Line == line && cue.Kind == "callback" &&
                            (cue.Arguments[0] == "taunt_speech" || cue.Arguments[0] == "plyr_open_sp")),
                            "Final source sound identity must be a real closed original callback.");
                    }
                }
                object[] additional = binding.Array("new_callback_lines");
                var unique = new HashSet<int>();
                foreach (object raw in additional)
                {
                    int line = RosterJson.Integer(raw, 1, 100000);
                    RosterGuard.Require(unique.Add(line) && cues.Exists(cue => cue.Line == line && cue.Kind == "callback" &&
                        (cue.Arguments[0] == "taunt_speech" || cue.Arguments[0] == "tackle_spch")),
                        "C1 additional speech sites must remain exact original metadata, not implemented substitutes.");
                }
                binding.Done();
                RosterGuard.Require(cameraBindings.Count == (final ? 36 : 25) && additional.Length == (m1 ? 41 : c2 ? 36 : 28) &&
                    cues.Count == (final ? 508 : m1 ? 482 : c2 ? 477 : 469),
                    "Impact coverage requires the exact historical469 or explicit C2477 source sites, never a missing-speech subset.");
            }
            string[] qualifications = root.Strings("qualifications", 1, 32); root.Done();
            return new SourceCinematicLibrary(expectedSha256, tables, shaker, cues.ToArray(), contacts.ToArray(), qualifications,
                contactAnimation, contactPrograms, cameraBindings.ToArray(), c2Data, movementPrograms);
        }

        private static bool HasCue(List<SourceCinematicCueDefinition> cues, string script, int line, string kind, string argument)
        { return cues.Exists(cue => cue.Script == script && cue.Line == line && cue.Kind == kind && cue.Arguments[0] == argument); }
    }
}
