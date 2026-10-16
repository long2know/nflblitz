using System;
using Blitz.AssetImport;
using UnityEngine;

namespace Blitz.Presentation
{
    // An explicitly isolated preview boundary, NOT a snd_scall_bank or
    // SourceCallback consumer. No RNG, DSP priority, pan, or call-volume mapping.
    public sealed class SourceAudioIsolatedPlayer : IDisposable
    {
        private readonly AudioSource source;
        private AudioClip ownedClip;
        private bool disposed;

        public SourceAudioIsolatedPlayer(AudioSource dedicatedSource)
        {
            if (dedicatedSource == null)
                throw new ArgumentNullException(nameof(dedicatedSource));
            if (dedicatedSource.isPlaying || dedicatedSource.clip != null)
                throw new ArgumentException("Lease a dedicated, empty AudioSource.", nameof(dedicatedSource));
            source = dedicatedSource;
        }

        public void PlayIsolated(SourceAudioBank bank, int originalPlaylistId)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SourceAudioIsolatedPlayer));
            if (bank == null)
                throw new ArgumentNullException(nameof(bank));
            if (source == null)
                throw new InvalidOperationException("The dedicated AudioSource was destroyed.");
            if (source.isPlaying)
                throw new NotSupportedException("Overlapping playback requires original DSP allocation/priority semantics.");
            if (source.clip != ownedClip)
                throw new InvalidOperationException("The dedicated AudioSource lease was changed externally.");
            SourceAudioPlaylist playlist = bank.GetPlaylist(originalPlaylistId);
            if (!playlist.IsolatedPlaybackQualified)
                throw new NotSupportedException("Original playlist end timing requires a qualified DCS playlist interpreter.");
            ReleaseClip();
            SourceAudioPcm pcm = playlist.Pcm;
            ownedClip = AudioClip.Create(bank.BankName + ":" + originalPlaylistId,
                pcm.SampleCount, pcm.Channels, pcm.SampleRate, false);
            if (ownedClip == null)
                throw new InvalidOperationException("Unity failed to allocate original PCM.");
            if (!ownedClip.SetData(pcm.CopyNormalizedSamples(), 0))
            {
                ReleaseClip();
                throw new InvalidOperationException("Unity rejected original PCM data.");
            }
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.panStereo = 0;
            source.volume = 1;
            source.pitch = 1;
            source.clip = ownedClip;
            source.Play();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (source != null && source.clip == ownedClip)
            {
                source.Stop();
                source.clip = null;
            }
            ReleaseClip();
        }

        private void ReleaseClip()
        {
            if (ownedClip == null)
                return;
            if (source != null && source.clip == ownedClip)
                source.clip = null;
            UnityEngine.Object.Destroy(ownedClip);
            ownedClip = null;
        }
    }
}
