using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace Devm_items_editor
{
    // Decodes modern Tibia client sprite sheets (client/things/<version>/sprites-*.bmp.lzma),
    // referenced by catalog-content.json, so item sprites can be previewed directly in this tool.
    //
    // File format (reverse engineered from mehah/otclient's SpriteAppearances::loadSpriteSheet):
    //   - A CIP-specific 32-byte-ish header (variable length padding, a magic byte sequence, a
    //     7-bit varint LZMA size, then 1 byte lc/lp/pb + 4 bytes dictionary size + 8 bytes cip size),
    //     immediately followed by a raw (headerless) LZMA1 stream.
    //   - The decompressed payload is a standard 32bpp BMP: a pixel-data-offset field at byte 10,
    //     followed by 384x384 BGRA pixel data, stored bottom-up like any BMP.
    //   - Magenta (0xFF00FF) pixels are a legacy transparency color-key on top of the alpha channel.
    public class CatalogEntry
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("file")]
        public string File { get; set; }

        [JsonProperty("firstspriteid")]
        public int FirstSpriteId { get; set; }

        [JsonProperty("lastspriteid")]
        public int LastSpriteId { get; set; }

        [JsonProperty("spritetype")]
        public int SpriteType { get; set; }
    }

    public class SpriteRenderer
    {
        private const int SheetSize = 384;
        private const int BytesInSheet = SheetSize * SheetSize * 4;
        private const int LzmaUncompressedSize = BytesInSheet + 122;
        private const int SheetWidthBytes = SheetSize * 4;

        // All 36 possible sprite tile sizes within a 384x384 sheet, indexed by the catalog's
        // "spritetype" field. Matches SpriteSheet::getSpriteSize() in otclient exactly - this
        // layout is a client asset format detail, not something that varies per-server.
        private static readonly (int Width, int Height)[] SpriteSizes = {
            (32,32), (32,64), (64,32), (64,64), (32,96), (32,128), (32,192), (32,384),
            (64,96), (64,128), (64,192), (64,384), (96,32), (96,64), (96,96), (96,128),
            (96,192), (96,384), (128,32), (128,64), (128,96), (128,128), (128,192), (128,384),
            (192,32), (192,64), (192,96), (192,128), (192,192), (192,384), (384,32), (384,64),
            (384,96), (384,128), (384,192), (384,384)
        };

        private string _assetsFolder;
        private List<CatalogEntry> _sheets;
        private readonly Dictionary<string, byte[]> _decodedSheetCache = new Dictionary<string, byte[]>();

        public bool IsLoaded => _sheets != null && _sheets.Count > 0;

        public string LoadError { get; private set; }

        public bool LoadCatalog(string assetsFolder)
        {
            LoadError = null;
            _decodedSheetCache.Clear();
            _sheets = null;

            try {
                string catalogPath = Path.Combine(assetsFolder, "catalog-content.json");
                if (!File.Exists(catalogPath)) {
                    LoadError = "catalog-content.json not found next to the appearances file.";
                    return false;
                }

                string json = File.ReadAllText(catalogPath);
                var entries = JsonConvert.DeserializeObject<List<CatalogEntry>>(json);
                _sheets = entries.Where(entry => entry.Type == "sprite").ToList();
                _assetsFolder = assetsFolder;
                return _sheets.Count > 0;
            } catch (Exception ex) {
                LoadError = ex.Message;
                return false;
            }
        }

        public BitmapSource GetSpriteBitmap(int spriteId)
        {
            if (!IsLoaded || spriteId <= 0) {
                return null;
            }

            CatalogEntry sheetEntry = _sheets.FirstOrDefault(entry => spriteId >= entry.FirstSpriteId && spriteId <= entry.LastSpriteId);
            if (sheetEntry == null) {
                return null;
            }

            byte[] sheetPixels = DecodeSheet(sheetEntry);
            if (sheetPixels == null) {
                return null;
            }

            (int tileWidth, int tileHeight) = sheetEntry.SpriteType >= 0 && sheetEntry.SpriteType < SpriteSizes.Length
                ? SpriteSizes[sheetEntry.SpriteType]
                : SpriteSizes[0];

            int columns = SheetSize / tileWidth;
            int rows = SheetSize / tileHeight;
            int spritesPerSheet = columns * rows;
            int offset = spriteId - sheetEntry.FirstSpriteId;
            if (offset < 0 || offset >= spritesPerSheet) {
                return null;
            }

            int spriteRow = offset / columns;
            int spriteColumn = offset % columns;
            int tileWidthBytes = tileWidth * 4;

            byte[] tilePixels = new byte[tileWidth * tileHeight * 4];
            for (int y = 0; y < tileHeight; y++) {
                int srcOffset = ((spriteRow * tileHeight) + y) * SheetWidthBytes + (spriteColumn * tileWidthBytes);
                int dstOffset = y * tileWidthBytes;
                Array.Copy(sheetPixels, srcOffset, tilePixels, dstOffset, tileWidthBytes);
            }

            return BitmapSource.Create(tileWidth, tileHeight, 96, 96, PixelFormats.Bgra32, null, tilePixels, tileWidthBytes);
        }

        private byte[] DecodeSheet(CatalogEntry sheetEntry)
        {
            if (_decodedSheetCache.TryGetValue(sheetEntry.File, out byte[] cached)) {
                return cached;
            }

            string path = Path.Combine(_assetsFolder, sheetEntry.File);
            if (!File.Exists(path)) {
                return null;
            }

            byte[] raw = File.ReadAllBytes(path);
            using (var input = new MemoryStream(raw)) {
                int b;
                // Skip leading NULL padding bytes, then the fixed 5-byte magic (0x70 0x0A 0xFA 0x80 0x24) -
                // one of those 5 bytes is the non-zero byte that terminates the loop below.
                do {
                    b = input.ReadByte();
                } while (b == 0x00);
                input.Seek(4, SeekOrigin.Current);

                // 7-bit-encoded LZMA payload size; we don't need the value, just to skip past it.
                do {
                    b = input.ReadByte();
                } while ((b & 0x80) == 0x80);

                byte lclppb = (byte)input.ReadByte();
                byte[] dictSizeBytes = new byte[4];
                input.Read(dictSizeBytes, 0, 4);
                input.Seek(8, SeekOrigin.Current); // cip compressed size, unused

                byte[] properties = { lclppb, dictSizeBytes[0], dictSizeBytes[1], dictSizeBytes[2], dictSizeBytes[3] };

                var decoder = new SevenZip.Compression.LZMA.Decoder();
                decoder.SetDecoderProperties(properties);

                byte[] decompressed = new byte[LzmaUncompressedSize];
                using (var output = new MemoryStream(decompressed)) {
                    decoder.Code(input, output, raw.Length - input.Position, LzmaUncompressedSize, null);
                }

                int bmpDataOffset = decompressed[10] | (decompressed[11] << 8) | (decompressed[12] << 16) | (decompressed[13] << 24);
                if (bmpDataOffset < 0 || bmpDataOffset + BytesInSheet > LzmaUncompressedSize) {
                    return null;
                }

                byte[] pixels = new byte[BytesInSheet];
                Array.Copy(decompressed, bmpDataOffset, pixels, 0, BytesInSheet);

                // Magenta color-key: treat as fully transparent regardless of the source alpha byte.
                for (int i = 0; i < BytesInSheet; i += 4) {
                    if (pixels[i] == 0xFF && pixels[i + 1] == 0x00 && pixels[i + 2] == 0xFF) {
                        pixels[i] = 0;
                        pixels[i + 1] = 0;
                        pixels[i + 2] = 0;
                        pixels[i + 3] = 0;
                    }
                }

                // BMP pixel data is stored bottom-up; flip to top-down.
                byte[] flipped = new byte[BytesInSheet];
                for (int y = 0; y < SheetSize; y++) {
                    Array.Copy(pixels, (SheetSize - 1 - y) * SheetWidthBytes, flipped, y * SheetWidthBytes, SheetWidthBytes);
                }

                _decodedSheetCache[sheetEntry.File] = flipped;
                return flipped;
            }
        }
    }
}
