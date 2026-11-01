using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.AssetImport
{
    public sealed class SourceAudioMixerPoint
    {
        public int Frame { get; }
        public int Level { get; }
        internal SourceAudioMixerPoint(int frame, int level) { Frame = frame; Level = level; }
    }

    public sealed class SourceAudioMixerProgram
    {
        public const string NativeReferenceSha256 = "b231397143c03d0a707d67116aaccacde655b6245816bae8c4fd72a8f4937ed1";
        public SourceAudioPcm PrimaryZeroPcm { get; }
        public int StopFrame { get; }
        public ReadOnlyCollection<SourceAudioMixerPoint> GlobalMusic { get; }
        public ReadOnlyCollection<SourceAudioMixerPoint> PrimaryZeroLevels { get; }

        internal SourceAudioMixerProgram(SourceAudioPcm zero, int stop,
            List<SourceAudioMixerPoint> global, List<SourceAudioMixerPoint> own)
        {
            PrimaryZeroPcm = zero; StopFrame = stop;
            GlobalMusic = global.AsReadOnly(); PrimaryZeroLevels = own.AsReadOnly();
        }

        public int PrimaryZeroLevelAt(long sample)
        {
            int level = 0;
            foreach (SourceAudioMixerPoint point in PrimaryZeroLevels)
            {
                if ((long)point.Frame * 240 > sample) break;
                level = point.Level;
            }
            return level;
        }

        public int GlobalLevelAt(long sample)
        {
            int level = 0;
            foreach (SourceAudioMixerPoint point in GlobalMusic)
            {
                if ((long)point.Frame * 240 > sample) break;
                level = point.Level;
            }
            return level;
        }
    }
}
