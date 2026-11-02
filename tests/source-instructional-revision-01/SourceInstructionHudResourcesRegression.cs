using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using UnityEngine;

namespace Blitz.RevisionTests
{
    public static class SourceInstructionHudResourcesRegression
    {
        public static IReadOnlyList<string> Run(SourceHudAsset actualHud,
            Func<SourceInstructionRaster> actualRaster, IEnumerable<SourceInstructionFormatSlot> originalFormats,
            ISourceInstructionNativeTextFormatter nativeFormatter,
            IReadOnlyDictionary<SourceInstructionFont, SourceInstructionFontResource> actualFonts,
            IReadOnlyDictionary<SourceInstructionColor, uint> actualColors,
            ISourceInstructionNativeSpriteLayout nativeSpriteLayout)
        {
            if (actualHud == null) throw new ArgumentNullException(nameof(actualHud));
            if (originalFormats == null) throw new ArgumentNullException(nameof(originalFormats));
            var formats = new List<SourceInstructionFormatSlot>(originalFormats);
            var passed = new List<string>();
            Func<string, string, SourceInstructionHudResources> create = (plate, white) =>
                new SourceInstructionHudResources(actualHud, actualRaster, formats, nativeFormatter,
                    actualFonts, actualColors, nativeSpriteLayout, plate, white);

            Check(passed, "ActualAssetAliasesRetainBaseAtlasAndManifest", () => AssertAsset(actualHud));
            Check(passed, "BasePlateUsesActualGeneratedWhite", () =>
                AssertPlate(actualHud, create("interface", "interface-white"), "interface"));
            Check(passed, "ChromaPlateUsesBaseAtlasUvs", () =>
                AssertPlate(actualHud, create("interface-chroma", "interface-white"), "interface-chroma"));
            Check(passed, "WhitePlateUsesBaseAtlasUvs", () =>
                AssertPlate(actualHud, create("interface-white", "interface-white"), "interface-white"));
            Check(passed, "MissingAtlasBindingsRemainFailClosed", () =>
            {
                const string missing = "interface-missing-revision-01";
                Require(!actualHud.Data.Textures.ContainsKey(missing), "The negative texture must not be a manifest entry.");
                bool rejected = false;
                try { actualHud.TextureAtlas(missing); }
                catch (SourceInterfaceException) { rejected = true; }
                Require(rejected, "The actual asset accepted an unknown runtime texture.");
                ExpectMissing(() => { create("interface", missing); });
                ExpectMissing(() => { create(missing, "interface-white"); });
                AssertManifestHasNoAliases(actualHud);
            });
            return passed.AsReadOnly();
        }

        private static void AssertAsset(SourceHudAsset asset)
        {
            SourceInterfaceData data = asset.Data;
            int manifestCount = data.Textures.Count;
            SourceInterfaceTexture atlas = data.Textures["interface"];
            Texture2D original = asset.Texture("interface");
            Texture2D white = asset.Texture("interface-white");
            Texture2D chroma = asset.Texture("interface-chroma");
            Require(!ReferenceEquals(original, white) && !ReferenceEquals(original, chroma)
                && !ReferenceEquals(white, chroma), "Generated white/chroma textures cannot be nonwhite/base substitutes.");
            Require(white.name == "source-interface-white-chromakey-" + atlas.Sha256,
                "The actual generated white-mask texture identity was lost.");
            Require(chroma.name.StartsWith("source-interface-black-chromakey-", StringComparison.Ordinal),
                "The actual generated chroma texture identity was lost.");
            foreach (string id in new[] { "interface", "interface-white", "interface-chroma" })
            {
                Require(ReferenceEquals(atlas, asset.TextureAtlas(id)), id + " did not resolve the base atlas definition.");
                Texture2D texture = asset.Texture(id);
                Require(texture.width == atlas.Width && texture.height == atlas.Height,
                    id + " dimensions differ from its base atlas.");
                Require(ReferenceEquals(texture, asset.Texture(id)), id + " did not retain its actual cached texture.");
            }
            foreach (KeyValuePair<string, SourceInterfaceTexture> entry in data.Textures)
                Require(ReferenceEquals(entry.Value, asset.TextureAtlas(entry.Key)),
                    "An ordinary manifest texture definition was replaced: " + entry.Key);
            Require(ReferenceEquals(data, asset.Data) && manifestCount == data.Textures.Count,
                "Alias resolution changed the immutable source manifest.");
            AssertManifestHasNoAliases(asset);
        }

        private static void AssertPlate(SourceHudAsset asset, SourceInstructionHudResources resources, string texture)
        {
            Require(ReferenceEquals(asset, resources.Asset), "The adapter did not retain this actual asset.");
            int manifestCount = asset.Data.Textures.Count;
            SourceInterfaceTexture atlas = asset.Data.Textures["interface"];
            SourceInterfaceImage original = asset.Data.Image("playresult");
            SourceInstructionImage image = resources.ResolvePlate("playresult", "SCR_PLATE_TID");
            Require(image.Symbol == original.Id && image.Texture == texture && image.WhiteTexture == "interface-white",
                "The concrete adapter changed the actual plate/white identities.");
            Require(image.Width == original.Width && image.Height == original.Height
                && image.AnchorX == original.AnchorX && image.AnchorY == original.AnchorY,
                "The concrete adapter changed the original image dimensions/anchors.");
            Require(image.U0 == original.X / (double)atlas.Width
                && image.V0 == original.Y / (double)atlas.Height
                && image.U1 == (original.X + original.Width) / (double)atlas.Width
                && image.V1 == (original.Y + original.Height) / (double)atlas.Height,
                "The concrete adapter did not derive plate UVs from the actual base atlas.");
            Require(ReferenceEquals(asset.Texture(texture), resources.Asset.Texture(image.Texture))
                && ReferenceEquals(asset.Texture("interface-white"), resources.Asset.Texture(image.WhiteTexture)),
                "The concrete adapter substituted the plate or generated white texture.");
            Require(manifestCount == asset.Data.Textures.Count, "The adapter fabricated a manifest texture entry.");
            AssertManifestHasNoAliases(asset);
        }

        private static void AssertManifestHasNoAliases(SourceHudAsset asset)
        {
            Require(!asset.Data.Textures.ContainsKey("interface-white")
                && !asset.Data.Textures.ContainsKey("interface-chroma"),
                "Generated runtime aliases must not be fabricated manifest entries.");
        }

        private static void ExpectMissing(Action construct)
        {
            bool rejected = false;
            try { construct(); }
            catch (SourceInstructionPresentationException error)
            {
                Require(error.Error == SourceInstructionPresentationError.MissingResource,
                    "An unknown texture must retain the instruction MissingResource classification.");
                rejected = true;
            }
            Require(rejected, "The concrete adapter accepted an unknown texture.");
        }

        private static void Check(List<string> passed, string name, Action assertion)
        {
            try { assertion(); }
            catch (Exception error) { throw new InvalidOperationException(name + " failed.", error); }
            passed.Add(name);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
