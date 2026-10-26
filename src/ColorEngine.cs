using System;

namespace ClawAmbilight
{
    /// <summary>Screen grid -> 9 LED colors. Algorithms ported 1:1 from claw_ambilight.py.
    /// Zones: rows 1/18..13/18 only (skips the bottom ~30%: on-screen keyboard, taskbar, HUD);
    /// each screen half is split into 4 quadrants mapped to the matching stick-ring quadrant.
    /// LEDs 0..3 right stick (SW, SE, NE, NW), 4..7 left stick (NE, NW, SW, SE), 8 = ABXY (right NE).</summary>
    public static class ColorEngine
    {
        public static float[] Targets(float[] grid, int w, int h, LightMode mode, double sat)
        {
            int y0 = (int)Math.Round(h * 1.0 / 18), y2 = (int)Math.Round(h * 13.0 / 18), y1 = (y0 + y2) / 2;
            int x1 = w / 4, x2 = w / 2, x3 = 3 * w / 4;
            float[] Q(int xa, int xb, int ya, int yb) => mode == LightMode.Vibrant
                ? Vibrant(grid, w, xa, xb, ya, yb) : Ambient(grid, w, xa, xb, ya, yb, sat);

            var lTL = Q(0, x1, y0, y1); var lTR = Q(x1, x2, y0, y1); var lBL = Q(0, x1, y1, y2); var lBR = Q(x1, x2, y1, y2);
            var rTL = Q(x2, x3, y0, y1); var rTR = Q(x3, w, y0, y1); var rBL = Q(x2, x3, y1, y2); var rBR = Q(x3, w, y1, y2);
            var leds = new[] { rBL, rBR, rTR, rTL, lTR, lTL, lBL, lBR, rTR };
            var t = new float[27];
            for (int i = 0; i < 9; i++) Array.Copy(leds[i], 0, t, i * 3, 3);
            return t;
        }

        /// <summary>'ambient': soft chroma weighting (w = 1 + (chroma/32)^1.2) and saturation expanded
        /// around Rec.601 luminance. Calm, colored elements still shine through gray backgrounds.</summary>
        static float[] Ambient(float[] g, int w, int xa, int xb, int ya, int yb, double sat)
        {
            double sw = 0, r = 0, gg = 0, b = 0, maxC = 0;
            for (int y = ya; y < yb; y++)
                for (int x = xa; x < xb; x++)
                {
                    int o = (y * w + x) * 3;
                    double pr = g[o], pg = g[o + 1], pb = g[o + 2];
                    double hi = Math.Max(pr, Math.Max(pg, pb)), lo = Math.Min(pr, Math.Min(pg, pb));
                    maxC = Math.Max(maxC, hi);
                    double wt = 1.0 + Math.Pow((hi - lo) / 32.0, 1.2);
                    sw += wt; r += pr * wt; gg += pg * wt; b += pb * wt;
                }
            if (sw == 0 || maxC < 4) return new float[3];
            r /= sw; gg /= sw; b /= sw;
            double lum = 0.299 * r + 0.587 * gg + 0.114 * b;
            return new[] { C(lum + (r - lum) * sat), C(lum + (gg - lum) * sat), C(lum + (b - lum) * sat) };
        }

        /// <summary>'vibrant': picks the most saturated accent: weights (chroma * sqrt(brightness))^2.5,
        /// near-black ignored, falls back to the mean on gray scenes.</summary>
        static float[] Vibrant(float[] g, int w, int xa, int xb, int ya, int yb)
        {
            double sw = 0, r = 0, gg = 0, b = 0, mr = 0, mg = 0, mb = 0, maxVib = 0; int n = 0;
            for (int y = ya; y < yb; y++)
                for (int x = xa; x < xb; x++)
                {
                    int o = (y * w + x) * 3;
                    double pr = g[o], pg = g[o + 1], pb = g[o + 2];
                    double hi = Math.Max(pr, Math.Max(pg, pb)), lo = Math.Min(pr, Math.Min(pg, pb));
                    if (hi <= 15) continue;
                    n++; mr += pr; mg += pg; mb += pb;
                    double vib = (hi - lo) * Math.Sqrt(hi / 255.0);
                    maxVib = Math.Max(maxVib, vib);
                    double wt = Math.Pow(vib, 2.5);
                    sw += wt; r += pr * wt; gg += pg * wt; b += pb * wt;
                }
            if (n == 0) return new float[3];
            if (maxVib < 12.0 || sw < 1e-4) return new[] { (float)(mr / n), (float)(mg / n), (float)(mb / n) };
            return new[] { (float)(r / sw), (float)(gg / sw), (float)(b / sw) };
        }

        static float C(double v) => (float)Math.Clamp(v, 0, 255);
    }
}
