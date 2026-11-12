using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Storage
{
    public static class SourceCustomPlayCodec
    {
        public const string SourceBlob = "42605264092a30f861f5390535bbe217aac13b66";

        // cap_play_t's compact grid fields, not a native cap_block_t/CMOS image.
        // The source's "decompress" operation is convert_play, not bit packing.
        public static byte[] EncodeCompact(SourceCustomPlay play)
        {
            if (play == null) throw new ArgumentNullException(nameof(play));
            var bytes = new byte[SourceSavedRecordLimits.CompactPlayBytes];
            WriteName(bytes, 0, play.Name1); WriteName(bytes, 9, play.Name2);
            bytes[18] = (byte)play.Motion; bytes[19] = (byte)play.Flags;
            for (int i = 0; i < 27; i++)
            {
                bytes[20 + i * 3] = (byte)play.Points[i].X;
                bytes[21 + i * 3] = (byte)play.Points[i].Y;
                bytes[22 + i * 3] = (byte)play.Points[i].Flags;
            }
            return bytes;
        }

        public static SourceCustomPlayValidation DecodeCompact(byte[] bytes)
        {
            if (bytes == null || bytes.Length != SourceSavedRecordLimits.CompactPlayBytes)
                throw new ArgumentException("A compact source grid play contains exactly 101 explicit bytes.", nameof(bytes));
            bool repaired = false;
            string name1 = ReadName(bytes, 0, ref repaired), name2 = ReadName(bytes, 9, ref repaired);
            var points = new SourceCustomPoint[27];
            for (int i = 0; i < 27; i++)
                points[i] = new SourceCustomPoint(bytes[20 + i * 3], bytes[21 + i * 3],
                    (SourceCustomPointFlags)bytes[22 + i * 3]);
            var play = new SourceCustomPlay(name1, name2, bytes[18], (SourceCustomPlayFlags)bytes[19], points);
            SourceCustomPlayValidation result = ValidateAndNormalize(play);
            return new SourceCustomPlayValidation(result.Play, result.Error, repaired || result.Repaired);
        }

        public static SourceCustomPlayValidation ValidateAndNormalize(SourceCustomPlay play)
        {
            if (play == null) throw new ArgumentNullException(nameof(play));
            if (play.Flags == SourceCustomPlayFlags.Unused)
                return new SourceCustomPlayValidation(play, SourceCustomPlayValidationError.None, false);
            if (play.Motion == 3)
                return new SourceCustomPlayValidation(play, SourceCustomPlayValidationError.Motion, false);
            var points = new SourceCustomPoint[27];
            for (int i = 0; i < 27; i++) points[i] = play.Points[i];
            bool repaired = false;
            var specials = new int[3];
            for (int receiver = 0; receiver < 3; receiver++)
            {
                int start = receiver * 9;
                SourceCustomPoint root = points[start];
                if (!LegalStart(root.X, root.Y, play.Motion == receiver))
                    return Invalid(play, SourceCustomPlayValidationError.BasePosition);
                var rootFlags = root.Flags & SourceCustomPointFlags.Last;
                repaired |= rootFlags != root.Flags;
                points[start] = new SourceCustomPoint(root.X, root.Y, rootFlags);
                for (int other = 0; other < receiver; other++)
                    if (Overlap(root, points[other * 9]))
                        return Invalid(play, SourceCustomPlayValidationError.OverlappingBases);
                int at = 1;
                for (; at < 9 && (points[start + at - 1].Flags & SourceCustomPointFlags.Last) == 0 &&
                    points[start + at].X != 0; at++)
                {
                    SourceCustomPoint node = points[start + at];
                    if (node.X < 1 || node.X > 39 || node.Y < 1 || node.Y > 43)
                        return Invalid(play, SourceCustomPlayValidationError.ControlPosition);
                    for (int previous = 0; previous < at; previous++)
                        if (Overlap(node, points[start + previous]))
                            return Invalid(play, SourceCustomPlayValidationError.OverlappingRoute);
                }
                SourceCustomPoint last = points[start + at - 1];
                repaired |= (last.Flags & SourceCustomPointFlags.Last) == 0;
                points[start + at - 1] = new SourceCustomPoint(last.X, last.Y, last.Flags | SourceCustomPointFlags.Last);
                for (int clear = at; clear < 9; clear++)
                {
                    SourceCustomPoint old = points[start + clear];
                    repaired |= old.X != 0 || old.Y != 0 || old.Flags != 0;
                    points[start + clear] = default(SourceCustomPoint);
                }
                for (int node = 0; node < at; node++)
                {
                    int flags = (int)points[start + node].Flags;
                    for (int bit = 0; bit < 3; bit++) if ((flags & (1 << bit)) != 0) specials[bit]++;
                }
            }
            for (int i = 0; i < 3; i++)
                if (specials[i] > 3) return Invalid(play, SourceCustomPlayValidationError.SpecialLimit);
            return new SourceCustomPlayValidation(new SourceCustomPlay(play.Name1, play.Name2, play.Motion,
                play.Flags, points), SourceCustomPlayValidationError.None, repaired);
        }

        public static SourceCustomPlay CreateFromFormation(int formationIndex, string name1, string name2)
        {
            // cap.c:624-653, including the actual QB-far and motion-receiver operands.
            int[,] formations = {
                { 9, 8, 12, 5, 28, 8, 0, 1 },
                { 11, 7, 16, 1, 28, 8, 1, 0 },
                { 27, 7, 29, 8, 32, 8, 1, 0 },
                { 16, 4, 24, 4, 29, 5, 0, 2 },
                { 10, 5, 16, 4, 25, 8, 0, 0 }
            };
            if (formationIndex < 0 || formationIndex >= 5) throw new ArgumentOutOfRangeException(nameof(formationIndex));
            var points = new SourceCustomPoint[27];
            for (int i = 0; i < 3; i++)
                points[i * 9] = new SourceCustomPoint(formations[formationIndex, i * 2],
                    formations[formationIndex, i * 2 + 1], SourceCustomPointFlags.Last);
            return new SourceCustomPlay(name1, name2, formations[formationIndex, 7],
                SourceCustomPlayFlags.Exists | (formations[formationIndex, 6] != 0 ?
                SourceCustomPlayFlags.QuarterbackFar : SourceCustomPlayFlags.None), points);
        }

        public static SourceCustomPlay ReplaceRoute(SourceCustomPlay play, int receiver,
            IReadOnlyList<SourceCustomPoint> route)
        {
            if (play == null) throw new ArgumentNullException(nameof(play));
            if (receiver < 0 || receiver > 2) throw new ArgumentOutOfRangeException(nameof(receiver));
            if (route == null || route.Count < 1 || route.Count > 9)
                throw new ArgumentException("Supply the base and up to eight control points.", nameof(route));
            var points = new SourceCustomPoint[27];
            for (int i = 0; i < 27; i++) points[i] = play.Points[i];
            for (int i = 0; i < 9; i++)
                points[receiver * 9 + i] = i >= route.Count ? default(SourceCustomPoint) :
                    new SourceCustomPoint(route[i].X, route[i].Y, i == route.Count - 1 ?
                        route[i].Flags | SourceCustomPointFlags.Last : route[i].Flags & ~SourceCustomPointFlags.Last);
            SourceCustomPlayValidation result = ValidateAndNormalize(new SourceCustomPlay(play.Name1,
                play.Name2, play.Motion, play.Flags, points));
            if (!result.Valid) throw new ArgumentException("Invalid original custom route: " + result.Error + ".", nameof(route));
            return result.Play;
        }

        public static SourcePlayDefinition ConvertToPlay(SourceCustomPlay play, int stationId, int slotIndex,
            SourcePlaybook playbook)
        {
            if (playbook == null || playbook.Revision != SourceSavedRecordLimits.OriginalRevision ||
                playbook.Profile != SourceDefaultAudibleData.RequiredPlaybookProfile ||
                playbook.Directions == null)
                throw new ArgumentException("Use the authenticated original catalog and direction table.", nameof(playbook));
            if (stationId < 0 || stationId > 3) throw new ArgumentOutOfRangeException(nameof(stationId));
            if (slotIndex < 0 || slotIndex > 8) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            SourceCustomPlayValidation result = ValidateAndNormalize(play);
            if (!result.Valid || !play.Exists)
                throw new ArgumentException("An existing source-valid grid play is required.", nameof(play));
            play = result.Play;
            string id = "custom_p" + stationId + "_s" + slotIndex;
            var formation = new SourceFormationSlot[7];
            for (int i = 0; i < 3; i++)
                formation[i] = new SourceFormationSlot(new SourcePlanar(-12f, (i - 1) * 17.5f),
                    i == 1 ? "center_set_anim" : "stance3pt_anim", SourceActorMode.LinePreSnap, SourceFormationControl.None);
            bool far = (play.Flags & SourceCustomPlayFlags.QuarterbackFar) != 0;
            formation[3] = new SourceFormationSlot(new SourcePlanar(far ? -38f : -18f, 0f),
                far ? "shotgun_anim" : "qbset_anim", SourceActorMode.QuarterbackPreSnap, SourceFormationControl.Captain);
            SourceFormationControl[] positions = {
                SourceFormationControl.Left, SourceFormationControl.Center, SourceFormationControl.Right
            };
            var routes = new SourceRouteProgram[7];
            for (int i = 0; i < 3; i++) routes[i] = playbook.GetRoute("ds_none");
            routes[3] = playbook.GetRoute("pr_QB_block");
            for (int i = 0; i < 3; i++)
            {
                SourceCustomPoint root = play.Point(i, 0);
                float z = (root.X - 20) * 7.09f;
                if (root.X == 20) z += 2.5f;
                SourceFormationControl control = positions[i];
                if (play.Motion == i) control |= SourceFormationControl.SecondPlayer | SourceFormationControl.Motion;
                formation[i + 4] = new SourceFormationSlot(new SourcePlanar((root.Y - 10) * 7.09f, z),
                    "wr_set_anim", SourceActorMode.ReceiverPreSnap, control);
                routes[i + 4] = new SourceRouteProgram(id + "_r" + i, SourceBlob, 4464,
                    ConvertRoute(play, i, playbook.Directions));
            }
            SourcePlayFlags flags = SourcePlayFlags.Custom;
            if ((play.Flags & SourceCustomPlayFlags.PrimaryReceiver1) != 0) flags |= SourcePlayFlags.PrimaryReceiver1;
            else if ((play.Flags & SourceCustomPlayFlags.PrimaryReceiver2) != 0) flags |= SourcePlayFlags.PrimaryReceiver2;
            else if ((play.Flags & SourceCustomPlayFlags.PrimaryReceiver3) != 0) flags |= SourcePlayFlags.PrimaryReceiver3;
            string display = (play.Name1 + " " + play.Name2).Trim();
            return new SourcePlayDefinition(id, display.Length == 0 ? "CUSTOM" : display, SourceBlob, 4365,
                SourcePlaySide.Offense, flags, new SourceFormation(id + "_formation", SourceBlob, 4365, formation), routes);
        }

        public static int[] ConvertRoute(SourceCustomPlay play, int receiver, SourceDirectionMath directions)
        {
            if (directions == null) throw new ArgumentNullException(nameof(directions));
            SourceCustomPlayValidation result = ValidateAndNormalize(play);
            if (!result.Valid || !play.Exists)
                throw new ArgumentException("An existing original route must validate before conversion.", nameof(play));
            play = result.Play;
            if (receiver < 0 || receiver > 2) throw new ArgumentOutOfRangeException(nameof(receiver));
            var words = new List<int> { (int)SourceRouteOpcode.WaitSnap };
            bool flip = play.Point(receiver, 0).X < 20, jukeUsed = false, turboUsed = false;
            int turboLeft = 0, lastFace = 1024;
            for (int at = 0; at < 9 && (play.Point(receiver, at).Flags & SourceCustomPointFlags.Last) == 0; at++)
            {
                SourceCustomPoint current = play.Point(receiver, at), next = play.Point(receiver, at + 1);
                int angle = directions.Heading(new SourcePlanar(-current.X, current.Y), new SourcePlanar(-next.X, next.Y));
                if (angle > 512) angle -= 1024;
                if (flip) angle = -angle;
                int change = (angle - lastFace) & 1023;
                if (change > 512) change -= 1024;
                int fake = Math.Abs(change) <= 256 ? (lastFace - change) & 1023 : lastFace;
                if (fake > 512) fake -= 1024;
                float dx = next.X - current.X, dy = next.Y - current.Y;
                float magnitude = (float)Math.Sqrt(dx * dx + dy * dy);
                int distance = (int)(magnitude / 1.1f + 0.5f);
                if ((current.Flags & SourceCustomPointFlags.Turbo) != 0)
                {
                    if (turboLeft == 0) words.Add((int)SourceRouteOpcode.TurboOn);
                    turboLeft += turboUsed ? 6 : 10; turboUsed = true;
                }
                else if ((current.Flags & SourceCustomPointFlags.Juke) != 0)
                {
                    int probability = Math.Abs(change) >= 256 ? (jukeUsed ? 45 : 70) :
                        Math.Abs(change) >= 128 ? (jukeUsed ? 45 : 55) : (jukeUsed ? 20 : 35);
                    words.Add((int)SourceRouteOpcode.Juke);
                    Fake(words, fake, probability, jukeUsed ? 30 : 45); jukeUsed = true;
                }
                else if ((current.Flags & SourceCustomPointFlags.Spin) != 0)
                {
                    words.Add((int)SourceRouteOpcode.Spin); Fake(words, fake, 25, 30);
                }
                else if (words.Count > 1 && Math.Abs(change) >= 64)
                    Fake(words, fake, change >= 256 ? 45 : 30, 0);
                if (change != 0 || words.Count == 1)
                {
                    words.Add((int)SourceRouteOpcode.Face); words.Add(angle); lastFace = angle;
                }
                if (turboLeft != 0 && turboLeft <= distance)
                {
                    Run(words, turboLeft); words.Add((int)SourceRouteOpcode.TurboOff);
                    if (distance != turboLeft) Run(words, distance - turboLeft);
                    turboLeft = 0;
                }
                else { if (turboLeft != 0) turboLeft -= distance; Run(words, distance); }
            }
            if (turboLeft != 0) words.Add((int)SourceRouteOpcode.TurboOff);
            words.Add((int)SourceRouteOpcode.Improvise);
            return words.ToArray();
        }

        private static void Run(List<int> words, int yards)
        { words.Add((int)SourceRouteOpcode.RunYards); words.Add(yards); }
        private static void Fake(List<int> words, int direction, int probability, int force)
        { words.Add((int)SourceRouteOpcode.FakeOut); words.Add(direction); words.Add(probability); words.Add(force); }
        private static bool Overlap(SourceCustomPoint a, SourceCustomPoint b)
        { return Math.Abs(a.X - b.X) < 2 && Math.Abs(a.Y - b.Y) < 2; }
        private static bool LegalStart(int x, int y, bool motion)
        {
            return y > 0 && y <= 8 && (!motion || y <= 7) && x >= 8 && x <= 32 &&
                !(Math.Abs(x - 20) <= 1 && y > 3) && !(Math.Abs(x - 20) <= 3 && y > 6);
        }
        private static SourceCustomPlayValidation Invalid(SourceCustomPlay play, SourceCustomPlayValidationError error)
        { return new SourceCustomPlayValidation(play, error, false); }
        private static void WriteName(byte[] bytes, int start, string name)
        { for (int i = 0; i < name.Length; i++) bytes[start + i] = (byte)name[i]; }
        private static string ReadName(byte[] bytes, int start, ref bool repaired)
        {
            var chars = new List<char>();
            bool ended = false;
            for (int i = 0; i < 9; i++)
            {
                char c = (char)bytes[start + i];
                bool valid = i < 8 && ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == ' ' || c == '!' || c == '*' || c == '-');
                if (c == 0 || !valid) ended = true;
                if (ended) repaired |= c != 0;
                else chars.Add(c);
            }
            return new string(chars.ToArray());
        }
    }
}
