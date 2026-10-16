#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <string>
#include <vector>

#include "DCSDecoder.h"
#include "DCSDecoderNative.h"

// Invoked only on streams selected from hash-verified banks by prepare.py.
// The upstream raw-stream API is not a sandbox for arbitrary compressed input.
int main(int argc, char **argv)
{
    try
    {
        if (argc != 4)
            throw std::runtime_error("usage: decode <stream> <mix 0..127> <pcm-output>");
        char *end = nullptr;
        long mix = std::strtol(argv[2], &end, 10);
        if (*end != '\0' || end == argv[2] || mix < 0 || mix > 127)
            throw std::runtime_error("unsupported mixing level");
        std::ifstream input(argv[1], std::ios::binary);
        if (!input)
            throw std::runtime_error("cannot open stream");
        std::vector<uint8_t> bytes{std::istreambuf_iterator<char>(input), {}};
        if (bytes.size() < 18 || bytes.size() > 1024 * 1024)
            throw std::runtime_error("stream size outside bounded extraction profile");
        unsigned int frames = (bytes[0] << 8) | bytes[1];
        if (frames == 0 || frames >= 65535)
            throw std::runtime_error("invalid frame count");
        const size_t originalSize = bytes.size();
        // The decoder's bit reader performs word lookahead.
        bytes.resize(originalSize + 16, 0);
        DCSDecoder::MinHost host;
        DCSDecoderNative decoder(&host);
        decoder.InitStandalone(DCSDecoder::OSVersion::OS95);
        decoder.SetDefaultVolume(255);
        decoder.SoftBoot();
        DCSDecoder::ROMPointer stream(0, bytes.data());
        auto info = decoder.GetStreamInfo(stream);
        if (info.nFrames != static_cast<int>(frames) || info.nBytes < 18 ||
            static_cast<size_t>(info.nBytes) > originalSize)
            throw std::runtime_error("decoded stream exceeds supplied bank extent");
        decoder.LoadAudioStream(0, stream, static_cast<int>(mix));
        std::ofstream output(argv[3], std::ios::binary | std::ios::trunc);
        if (!output)
            throw std::runtime_error("cannot open PCM output");
        uint32_t samples = (frames + 2) * 240;
        for (uint32_t i = 0; i < samples; ++i)
        {
            uint16_t sample = static_cast<uint16_t>(decoder.GetNextSample());
            char le[2] = { static_cast<char>(sample & 255), static_cast<char>(sample >> 8) };
            output.write(le, 2);
        }
        output.close();
        if (!output)
            throw std::runtime_error("PCM write failed");
        std::cout << "{\"frames\":" << info.nFrames << ",\"consumed_bytes\":"
                  << info.nBytes << ",\"format_type\":" << info.formatType
                  << ",\"sample_count\":" << samples << "}\n";
        return 0;
    }
    catch (const std::exception &ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }
}
