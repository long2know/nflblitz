#include <cstdint>
#include <cstdlib>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <vector>
#include "DCSDecoder.h"
#include "DCSDecoderNative.h"

class PlaylistDecoder : public DCSDecoderNative
{
public:
    explicit PlaylistDecoder(Host *host) : DCSDecoderNative(host) {}

    void Start(std::vector<uint8_t> &bank, size_t offset, size_t length)
    {
        if (offset + length > bank.size() || length < 5 || bank[offset] != 1 || bank[offset + 1] != 1)
            throw std::runtime_error("only an authenticated single-logical-channel BNK program is supported");
        size_t end = offset + length;
        size_t p = offset + 2;
        while (p + 3 <= end)
        {
            uint16_t wait = (bank[p] << 8) | bank[p + 1];
            uint8_t op = bank[p + 2];
            p += 3;
            size_t operands = 0;
            switch (op)
            {
                case 0x00: case 0x0d: case 0x0f: break;
                case 0x01: operands = 5; break;
                case 0x02: case 0x0e: operands = 1; break;
                case 0x07: case 0x08: case 0x09: case 0x10: operands = 2; break;
                case 0x0a: case 0x0b: case 0x0c: case 0x11: case 0x12: operands = 4; break;
                default: throw std::runtime_error("unsupported original playlist opcode; never skipped");
            }
            if (p + operands > end)
                throw std::runtime_error("truncated original playlist operands");
            if (op == 1)
            {
                size_t field = p + 1;
                uint32_t relative = (bank[field] << 16) | (bank[field + 1] << 8) | bank[field + 2];
                size_t absolute = field + relative;
                if (bank[p] != 0 || absolute + 18 > bank.size() || absolute >= 0x100000)
                    throw std::runtime_error("unqualified cross-channel or invalid original stream address");
                bank[field] = static_cast<uint8_t>(absolute >> 16);
                bank[field + 1] = static_cast<uint8_t>(absolute >> 8);
                bank[field + 2] = static_cast<uint8_t>(absolute);
            }
            else if ((op >= 7 && op <= 12 || op >= 16 && op <= 18 || op == 2) && bank[p] != 0)
                throw std::runtime_error("cross-channel playlist control is not an isolated compiled program");
            p += operands;
            if (op == 0 || wait == 65535)
                break;
        }
        size_t capacity = 1;
        while (capacity < bank.size() + 16) capacity *= 2;
        bank.resize(capacity, 0);
        InitStandalone(OSVersion::OS95);
        SetDefaultVolume(255);
        SoftBoot();
        ROM[0].Set(0, bank.data(), bank.size());
        LoadTrack(0, ROMPointer(0, bank.data() + offset + 2));
    }
};

int main(int argc, char **argv)
{
    try
    {
        if (argc != 6)
            throw std::runtime_error("usage: render_playlist <bank> <program offset> <program bytes> <frames> <PCM output>");
        size_t offset = std::stoul(argv[2]), length = std::stoul(argv[3]), frames = std::stoul(argv[4]);
        if (frames < 1 || frames > 32768)
            throw std::runtime_error("render frame budget exceeded");
        std::ifstream input(argv[1], std::ios::binary);
        if (!input) throw std::runtime_error("cannot read authenticated bank");
        std::vector<uint8_t> bank{ std::istreambuf_iterator<char>(input), {} };
        if (bank.size() > 4 * 1024 * 1024)
            throw std::runtime_error("bank extent exceeded");
        DCSDecoder::MinHost host;
        PlaylistDecoder decoder(&host);
        decoder.Start(bank, offset, length);
        std::ofstream output(argv[5], std::ios::binary | std::ios::trunc);
        if (!output) throw std::runtime_error("cannot create PCM output");
        for (size_t i = 0; i < frames * 240; i++)
        {
            uint16_t sample = static_cast<uint16_t>(decoder.GetNextSample());
            char bytes[2] = { static_cast<char>(sample), static_cast<char>(sample >> 8) };
            output.write(bytes, 2);
        }
        output.close();
        if (!output) throw std::runtime_error("PCM output failed");
        std::cout << "{\"frames\":" << frames << ",\"sample_count\":" << frames * 240
                  << ",\"policy\":\"compiled-source-single-logical-channel-playlist-not-DSP\"}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
