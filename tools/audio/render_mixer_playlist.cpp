#include <cstdint>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <vector>
#include "DCSDecoder.h"
#include "DCSDecoderNative.h"

class GlobalMixerDecoder : public DCSDecoderNative
{
public:
    explicit GlobalMixerDecoder(Host *host) : DCSDecoderNative(host) {}

    void Start(std::vector<uint8_t> &bank, size_t offset, size_t length, int primary)
    {
        if (primary < 0 || primary > 1 || offset + length > bank.size() || length < 5 ||
            bank[offset] != 1 || bank[offset + 1] != 1)
            throw std::runtime_error("exact single-local-stream programme and explicit primary0/1 required");
        size_t p = offset + 2, end = offset + length;
        int implicit = 0;
        while (p + 3 <= end)
        {
            uint16_t wait = (bank[p] << 8) | bank[p + 1];
            uint8_t op = bank[p + 2];
            p += 3;
            size_t operands;
            switch (op)
            {
                case 0x00: case 0x0d: case 0x0f: operands = 0; break;
                case 0x01: operands = 5; break;
                case 0x02: case 0x0e: case 0x13: operands = 1; break;
                case 0x07: case 0x08: case 0x09: case 0x10: operands = 2; break;
                case 0x0a: case 0x0b: case 0x0c: case 0x11: case 0x12: operands = 4; break;
                case 0x14: operands = 3; break;
                default: throw std::runtime_error("unknown programme opcode is never ignored");
            }
            if (p + operands > end)
                throw std::runtime_error("truncated original programme");
            lowered.push_back(static_cast<uint8_t>(wait >> 8));
            lowered.push_back(static_cast<uint8_t>(wait));
            if (op == 0x13 || op == 0x14)
            {
                // Original2.33 PM2FDA/3006 use literal global stream0, not local-stream mapping.
                // Their signed/clamped mixer arithmetic is the existing09/0B operator.
                lowered.push_back(op == 0x13 ? 0x09 : 0x0b);
                lowered.push_back(0);
                lowered.insert(lowered.end(), bank.begin() + p, bank.begin() + p + operands);
                implicit++;
            }
            else
            {
                lowered.push_back(op);
                if (op == 0x01)
                {
                    if (bank[p] != 0)
                        throw std::runtime_error("only original programme-local stream0 is qualified");
                    size_t field = p + 1;
                    uint32_t relative = (bank[field] << 16) | (bank[field + 1] << 8) | bank[field + 2];
                    size_t absolute = field + relative;
                    if (absolute + 18 > bank.size() || absolute >= 0x100000)
                        throw std::runtime_error("original relative stream address is invalid");
                    lowered.push_back(primary);
                    lowered.push_back(static_cast<uint8_t>(absolute >> 16));
                    lowered.push_back(static_cast<uint8_t>(absolute >> 8));
                    lowered.push_back(static_cast<uint8_t>(absolute));
                    lowered.push_back(bank[p + 4]);
                }
                else if (op == 0x02 || (op >= 0x07 && op <= 0x0c) || (op >= 0x10 && op <= 0x12))
                {
                    if (bank[p] != 0)
                        throw std::runtime_error("unqualified explicit cross-local-stream operator");
                    lowered.push_back(primary);
                    lowered.insert(lowered.end(), bank.begin() + p + 1, bank.begin() + p + operands);
                }
                else lowered.insert(lowered.end(), bank.begin() + p, bank.begin() + p + operands);
            }
            p += operands;
            if (op == 0 || wait == 65535) break;
        }
        if (p != end || implicit == 0)
            throw std::runtime_error("closed exact original implicit-global programme required");
        size_t capacity = 1;
        while (capacity < bank.size() + 16) capacity *= 2;
        bank.resize(capacity, 0);
        InitStandalone(OSVersion::OS95);
        SetDefaultVolume(255);
        SoftBoot();
        ROM[0].Set(0, bank.data(), bank.size());
        LoadTrack(primary, ROMPointer(0, lowered.data()));
    }

    int MusicContribution(int primary) const { return channel[0].mixer[primary].curLevel; }
    int OwnMix(int primary) const { return channel[primary].mixer[primary].curLevel; }
    bool ProgrammeActive(int primary) const { return !channel[primary].trackPtr.IsNull(); }
    bool StreamActive(int primary) const { return !channel[primary].audioStream.playbackBitPtr.IsNull(); }

private:
    std::vector<uint8_t> lowered;
};

int main(int argc, char **argv)
{
    try
    {
        if (argc != 8)
            throw std::runtime_error("usage: render_mixer_playlist bank offset length frames primary PCM-output timeline-output");
        size_t offset = std::stoul(argv[2]), length = std::stoul(argv[3]), frames = std::stoul(argv[4]);
        int primary = std::stoi(argv[5]);
        if (frames < 1 || frames > 32768)
            throw std::runtime_error("render frame budget exceeded");
        std::ifstream input(argv[1], std::ios::binary);
        if (!input) throw std::runtime_error("cannot read authenticated bank");
        std::vector<uint8_t> bank{ std::istreambuf_iterator<char>(input), {} };
        if (bank.size() > 4 * 1024 * 1024) throw std::runtime_error("bank extent exceeded");
        DCSDecoder::MinHost host;
        GlobalMixerDecoder decoder(&host);
        decoder.Start(bank, offset, length, primary);
        std::ofstream output(argv[6], std::ios::binary | std::ios::trunc);
        std::ofstream timeline(argv[7], std::ios::trunc);
        if (!output || !timeline) throw std::runtime_error("cannot create programme output");
        timeline << "{\"primary\":" << primary << ",\"frameSamples\":240,\"frames\":[";
        int oldMusic = 100000, oldOwn = 100000, stop = -1;
        bool first = true;
        for (size_t frame = 0; frame < frames; frame++)
        {
            for (size_t sample = 0; sample < 240; sample++)
            {
                uint16_t value = static_cast<uint16_t>(decoder.GetNextSample());
                char bytes[2] = { static_cast<char>(value), static_cast<char>(value >> 8) };
                output.write(bytes, 2);
                if (sample == 0)
                {
                    int music = decoder.MusicContribution(primary), own = decoder.OwnMix(primary);
                    bool active = decoder.ProgrammeActive(primary);
                    if (!active && stop < 0) stop = static_cast<int>(frame);
                    if (music != oldMusic || own != oldOwn || !active && stop == static_cast<int>(frame))
                    {
                        if (!first) timeline << ',';
                        first = false;
                        timeline << '[' << frame << ',' << music << ',' << own << ','
                                 << (active ? 1 : 0) << ',' << (decoder.StreamActive(primary) ? 1 : 0) << ']';
                        oldMusic = music; oldOwn = own;
                    }
                }
            }
        }
        if (stop < 0 || decoder.ProgrammeActive(primary) || decoder.StreamActive(primary))
            throw std::runtime_error("the finite original programme did not actually halt within its declared timeline");
        timeline << "],\"stopFrame\":" << stop
                 << ",\"qualification\":\"executed pinned CPU VM with source233 global0 operator lowering; not native DSP PCM\"}\n";
        output.close(); timeline.close();
        if (!output || !timeline) throw std::runtime_error("programme output failed");
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
