using System.IO.Compression;

namespace People.Sim;

/// <summary>외부 라이브러리 없이 RGB PNG 를 저장한다(지도 확인용).</summary>
public static class Png
{
    public static void Save(string path, int w, int h, byte[] rgb)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)w); WriteBE(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 2; // 8비트 RGB
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
        {
            for (int y = 0; y < h; y++)
            {
                z.WriteByte(0);
                z.Write(rgb, y * w * 3, w * 3);
            }
        }
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, (uint)data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFF);
        crc = Crc(data, crc) ^ 0xFFFFFFFF;
        var c = new byte[4]; WriteBE(c, 0, crc); s.Write(c);
    }

    private static void WriteBE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] data, uint crc)
    {
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

/// <summary>지도 그리기. 만들지 않은 청크는 안개색, 밭은 밀색, 집은 점.</summary>
public static class MapRender
{
    private static (byte, byte, byte) Color(TileType t) => t switch
    {
        TileType.Sea => (52, 92, 140),
        TileType.Lake => (70, 120, 170),
        TileType.River => (80, 135, 190),
        TileType.Grass => (128, 168, 88),
        TileType.Forest => (62, 112, 58),
        TileType.Marsh => (104, 128, 96),
        TileType.Hills => (150, 150, 112),
        TileType.Mountain => (128, 120, 112),
        _ => (255, 0, 255),
    };

    /// <summary>x0,y0 부터 w×h 타일을 scale 배로 그린다. generateAll 이면 안 만든 청크도 만든다.</summary>
    public static void Save(World? world, WorldMap map, string path, int x0, int y0, int w, int h, int scale = 1, bool generateAll = false)
    {
        int W = w * scale, H = h * scale;
        var rgb = new byte[W * H * 3];
        for (int ty = 0; ty < h; ty++)
            for (int tx = 0; tx < w; tx++)
            {
                int x = x0 + tx, y = y0 + ty;
                (byte r, byte g, byte b) c;
                if (!map.InBounds(x, y)) c = (20, 20, 24);
                else if (!generateAll && !map.IsGenerated(x / WorldMap.CS, y / WorldMap.CS)) c = (36, 38, 44);
                else
                {
                    int id = map.Id(x, y);
                    c = Color(map.Type(id));
                    if (map.Coarse == null && map.Type(id) == TileType.Grass)
                    {
                        float f = map.BaseFert(id);
                        c = ((byte)(128 - 30 * (f - 0.8f)), (byte)(150 + 30 * (f - 0.8f)), 88);
                    }
                    if (map.Owner(id) >= 0) c = (map.Fert(id) < map.BaseFert(id) - 0.05f) ? ((byte)200, (byte)178, (byte)96) : ((byte)214, (byte)196, (byte)112);
                }
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                    {
                        int o = ((ty * scale + sy) * W + tx * scale + sx) * 3;
                        rgb[o] = c.r; rgb[o + 1] = c.g; rgb[o + 2] = c.b;
                    }
            }
        if (world != null)
        {
            var hh = world.Households;
            for (int i = 0; i < hh.HighWater; i++)
            {
                if (!hh.IsAlive(i)) continue;
                int px = (hh.X[i] - x0) * scale, py = (hh.Y[i] - y0) * scale;
                int r = Math.Max(1, scale);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int xx = px + dx + scale / 2, yy = py + dy + scale / 2;
                        if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                        int o = (yy * W + xx) * 3;
                        rgb[o] = 120; rgb[o + 1] = 40; rgb[o + 2] = 30;
                    }
            }
        }
        Png.Save(path, W, H, rgb);
    }
}
