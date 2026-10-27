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
                ? Vibrant(grid, w, xa, xb, ya, yb, sat) : Ambient(grid, w, xa, xb, ya, yb, sat);

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

        /// <summary>'vibrant': the dominant hue of the zone, not its single most saturated pixel.
        /// Colored pixels (chroma >= 16) vote into 24 hue bins weighted by chroma; the best bin plus its
        /// neighbors gives the color, lifted to near full LED brightness (dark scenes stay a bit dimmer)
        /// and saturated by the slider. Gray scenes blend smoothly toward a neutral tone (no threshold).</summary>
        static float[] Vibrant(float[] g, int w, int xa, int xb, int ya, int yb, double sat)
        {
            const int Bins = 24;
            Span<double> bw = stackalloc double[Bins], br = stackalloc double[Bins], bg = stackalloc double[Bins], bb = stackalloc double[Bins];
            double mr = 0, mg = 0, mb = 0, sumHi = 0; int n = 0, colored = 0;
            for (int y = ya; y < yb; y++)
                for (int x = xa; x < xb; x++)
                {
                    int o = (y * w + x) * 3;
                    double pr = g[o], pg = g[o + 1], pb = g[o + 2];
                    double hi = Math.Max(pr, Math.Max(pg, pb)), lo = Math.Min(pr, Math.Min(pg, pb)), c = hi - lo;
                    if (hi <= 15) continue;
                    n++; mr += pr; mg += pg; mb += pb; sumHi += hi;
                    if (c < 16) continue;
                    colored++;
                    double hue = hi == pr ? (pg - pb) / c : hi == pg ? 2 + (pb - pr) / c : 4 + (pr - pg) / c; // 0..6
                    int bin = ((int)Math.Floor((hue + 6) % 6 / 6 * Bins)) % Bins;
                    bw[bin] += c; br[bin] += pr * c; bg[bin] += pg * c; bb[bin] += pb * c;
                }
            if (n == 0) return new float[3];

            int best = 0; double bestW = -1;
            for (int i = 0; i < Bins; i++)
            {
                double s3 = bw[(i + Bins - 1) % Bins] + bw[i] + bw[(i + 1) % Bins];
                if (s3 > bestW) { bestW = s3; best = i; }
            }
            double r = 0, gg = 0, b = 0, ww = 0;
            for (int d = -1; d <= 1; d++)
            {
                int i = (best + d + Bins) % Bins;
                r += br[i]; gg += bg[i]; b += bb[i]; ww += bw[i];
            }
            // brightness: near full on the LEDs, a dark zone (night scene) stays dimmer
            double v = Math.Clamp(0.45 + sumHi / n / 255.0 * 1.1, 0.45, 1.0);
            float[] Lift(double cr, double cg, double cb)
            {
                double m = Math.Max(cr, Math.Max(cg, cb));
                if (m < 1) return new float[3];
                double k = 255.0 * v / m;
                return new[] { C(cr * k), C(cg * k), C(cb * k) };
            }
            var gray = Lift(mr / n, mg / n, mb / n);
            if (ww < 1e-6) return gray;
            var col = Lift(r / ww, gg / ww, b / ww);
            double lum = 0.299 * col[0] + 0.587 * col[1] + 0.114 * col[2];
            for (int ch = 0; ch < 3; ch++) col[ch] = C(lum + (col[ch] - lum) * sat);
            double f = Math.Clamp(colored / (double)n * 4.0, 0, 1); // >= 25% colored pixels: pure color
            return new[] { C(col[0] * f + gray[0] * (1 - f)), C(col[1] * f + gray[1] * (1 - f)), C(col[2] * f + gray[2] * (1 - f)) };
        }

        static float C(double v) => (float)Math.Clamp(v, 0, 255);
    }
}
