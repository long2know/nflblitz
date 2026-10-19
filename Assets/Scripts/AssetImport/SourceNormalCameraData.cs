using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public sealed class SourceKickoffCameraDefinition
    {
        public SourceCinematicEntry Sequence { get; }
        public SourceVector3 Eye { get; }
        public int AutoHikeTicks { get; }
        public SourceKickoffCameraDefinition(SourceCinematicEntry sequence, SourceVector3 eye, int autoHikeTicks)
        {
            if (sequence == null || sequence.Table != "csi_kickoff" ||
                sequence.RawMode != 9 && sequence.RawMode != 19 || autoHikeTicks < 0)
                throw new ArgumentException("An actual original kickoff sequence/start is required.");
            SourceCameraBoundary.RequireFinite(eye);
            Sequence = sequence; Eye = eye; AutoHikeTicks = autoHikeTicks;
        }

        public SourceCinematicEntry WithDistance(float distance)
        {
            return new SourceCinematicEntry(Sequence.Table, Sequence.Ordinal, Sequence.SourceLine,
                Sequence.RawMode, Sequence.ModeSymbol, Sequence.ModeTicks, Sequence.TravelTicks,
                Sequence.ThetaDegrees, Sequence.PhiDegrees, distance, Sequence.ControlPoints);
        }
    }

    public sealed class SourceNormalCameraData
    {
        public const string ContentSha256 = "43e9ccbf3c5162ac99b86d1c2f4925d834e5b7d78fb7b1631f4d33c431c4f3c4";
        public ReadOnlyCollection<SourceKickoffCameraDefinition> Kickoff { get; }
        public ReadOnlyCollection<SourceCinematicEntry> FieldGoal { get; }
        public SourceNormalCameraData(IReadOnlyList<SourceKickoffCameraDefinition> kickoff,
            IReadOnlyList<SourceCinematicEntry> fieldGoal)
        {
            if (kickoff == null || kickoff.Count != 11 || fieldGoal == null || fieldGoal.Count != 2)
                throw new ArgumentException("Supply the original eleven kickoff and two field-goal camera entries.");
            var kicks = new SourceKickoffCameraDefinition[11];
            for (int i = 0; i < kicks.Length; i++)
            {
                if (kickoff[i] == null || kickoff[i].Sequence.Ordinal != i) throw new ArgumentException("Kickoff ordinal order changed.");
                kicks[i] = kickoff[i];
            }
            var goals = new SourceCinematicEntry[2];
            for (int i = 0; i < goals.Length; i++)
            {
                if (fieldGoal[i] == null || fieldGoal[i].Ordinal != i || fieldGoal[i].Table != "csi_fs_fgtrk" ||
                    fieldGoal[i].RawMode != 9) throw new ArgumentException("Field-goal tracking identity changed.");
                goals[i] = fieldGoal[i];
            }
            Kickoff = Array.AsReadOnly(kicks); FieldGoal = Array.AsReadOnly(goals);
        }

        public static SourceNormalCameraData Read(byte[] bytes)
        {
            RosterGuard.Require(bytes != null && bytes.Length <= 1024 * 1024 &&
                RosterGuard.Sha256(bytes) == ContentSha256, "Original normal/kickoff camera data pin mismatch; no approximate fallback.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(bytes), "original CAMBOT data");
            RosterGuard.Require(root.Text("schema") == "original-cambot-gameplay/v1" &&
                root.Text("source_revision") == SourceTeamRosterLibrary.PinnedSourceRevision &&
                root.Text("source_tree") == SourceTeamRosterLibrary.PinnedSourceTree &&
                root.Text("profile") == "normal-release-57hz-no-debug-no-NOSETCAM", "Original CAMBOT profile changed.");
            root.ExactInteger("tick_rate", 57);
            RosterObject tables = root.Object("tables");
            var kickoff = new List<SourceKickoffCameraDefinition>();
            foreach (object raw in tables.Array("csi_kickoff"))
            {
                RosterObject entry = RosterJson.Object(raw, "original kickoff camera");
                SourceCinematicEntry sequence = ReadEntry(entry, "csi_kickoff");
                double[] eye = entry.Numbers("eye", 3, -2000, 2000);
                int autoHike = entry.Integer("auto_hike_ticks", 0, 10000);
                entry.Done();
                kickoff.Add(new SourceKickoffCameraDefinition(sequence,
                    new SourceVector3((float)eye[0], (float)eye[1], (float)eye[2]), autoHike));
            }
            var goals = new List<SourceCinematicEntry>();
            foreach (object raw in tables.Array("csi_fs_fgtrk"))
            {
                RosterObject entry = RosterJson.Object(raw, "original field-goal camera");
                goals.Add(ReadEntry(entry, "csi_fs_fgtrk")); entry.Done();
            }
            tables.Done();
            foreach (object raw in root.Array("inputs"))
            {
                RosterObject input = RosterJson.Object(raw, "original CAMBOT source pin");
                input.Path("path"); input.Integer("bytes", 1, PlayerImportLimits.SourceBytes);
                RosterGuard.Hex(input.Text("git_blob_sha1", 40), 40, "CAMBOT Git blob");
                RosterGuard.Hex(input.Text("sha256", 64), 64, "CAMBOT SHA256"); input.Done();
            }
            root.Done();
            return new SourceNormalCameraData(kickoff, goals);
        }

        private static SourceCinematicEntry ReadEntry(RosterObject entry, string table)
        {
            int ordinal = entry.Integer("ordinal", 0, 10), line = entry.Integer("source_line", 1, 100000);
            int mode = entry.Integer("mode", 9, 19);
            string symbol = entry.Text("mode_symbol");
            int total = entry.Integer("mode_ticks", 1, 100000), travel = entry.Integer("travel_ticks", 0, 100000);
            float theta = (float)entry.Number("theta_degrees", -360, 360);
            float phi = (float)entry.Number("phi_degrees", -89, 89);
            float distance = (float)entry.Number("distance", 0, 2000);
            object[] rawPoints = entry.Array("control_points");
            RosterGuard.Require(rawPoints.Length % 3 == 0 && rawPoints.Length >= 6 && rawPoints.Length <= 6000,
                "Original CAMBOT point stream shape changed.");
            var points = new SourceVector3[rawPoints.Length / 3];
            for (int i = 0; i < points.Length; i++)
                points[i] = new SourceVector3((float)RosterJson.Number(rawPoints[i * 3], -10000, 10000),
                    (float)RosterJson.Number(rawPoints[i * 3 + 1], -10000, 10000),
                    (float)RosterJson.Number(rawPoints[i * 3 + 2], -10000, 10000));
            return new SourceCinematicEntry(table, ordinal, line, mode, symbol, total, travel, theta, phi, distance, points);
        }
    }
}
