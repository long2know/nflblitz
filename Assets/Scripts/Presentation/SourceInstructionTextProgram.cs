using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public sealed class SourceInstructionTextProgram
    {
        private static readonly int[,] PrintLines = {
            { 4943, 4951, 4954, 4957, 4960, 4963, 4966 },
            { 4997, 5005, 5007, 5010, 5012, 5015, 5017 },
            { 5044, 5052, 5054, 5057, 5059, 5062, 5064 },
            { 5091, 5099, 5101, 5104, 5106, 5109, 5111 }
        };
        private readonly SourceInstructionFormatSlot[] slots = new SourceInstructionFormatSlot[28];
        private readonly ISourceInstructionNativeTextFormatter formatter;

        public SourceInstructionTextProgram(IEnumerable<SourceInstructionFormatSlot> formats,
            ISourceInstructionNativeTextFormatter nativeFormatter)
        {
            if (formats == null) throw new ArgumentNullException(nameof(formats));
            formatter = nativeFormatter ?? throw new ArgumentNullException(nameof(nativeFormatter));
            foreach (SourceInstructionFormatSlot slot in formats)
            {
                if (slot == null) throw Missing("Null original format slot.");
                int index = (int)slot.Page * 7 + slot.Line;
                if (slots[index] != null || slot.NativePrintLine != PrintLines[(int)slot.Page, slot.Line])
                    throw Missing("Duplicate or incorrectly located original print slot.");
                slots[index] = slot;
            }
            for (int index = 0; index < slots.Length; index++)
                if (slots[index] == null) throw Missing("All 28 original print/format slots are required; missing " + index + ".");
        }

        public SourceInstructionFormattedText Evaluate(SourceInstructionTextRequest request,
            SourceInstructionFont font, uint color, SourceInstructionRaster raster, SourceInstructionPoint position)
        {
            SourceInstructionFormatSlot slot = slots[(int)request.Page * 7 + request.Line];
            if (slot.NativeSecondArgumentSymbols != request.NativeSecondArgumentSymbols)
                throw Missing("The native second argument is not a separate inferred renderer alignment.");
            SourceInstructionFormattedText formatted = formatter.Evaluate(
                new SourceInstructionFormatCall(slot, request, font, color, position, raster));
            if (formatted == null || formatted.Runs.Count == 0) throw Missing("The native format binding returned no evaluated string state/runs.");
            foreach (SourceInstructionTextRun run in formatted.Runs)
                if (run == null) throw Missing("The native format binding returned a null run.");
            return formatted;
        }

        private static SourceInstructionPresentationException Missing(string message)
        { return new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource, message); }
    }
}
