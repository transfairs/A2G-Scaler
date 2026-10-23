using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace A2GScaler
{
    /// <summary>
    /// "Sharp bilinear" scaling for non-integer factors: every game pixel becomes a crisp block, only the
    /// screen pixel where two blocks meet is blended. Avoids nearest's uneven 1-or-2-pixel columns and
    /// HALFTONE's overall blur. Done in C# because GDI has no bilinear mode: nearest to the next integer
    /// multiple plus HALFTONE down costs 18 ms at 1000x718 and over 100 ms at 4K, this costs 3 and 11 ms
    /// single-threaded (measured 23.10.2026).
    /// </summary>
    sealed class SharpScaler : IDisposable
    {
        IntPtr srcDC = IntPtr.Zero, srcBmp = IntPtr.Zero, srcOld = IntPtr.Zero, srcBits = IntPtr.Zero;
        Size srcSize;
        int[] src = new int[0], rows = new int[0], dst = new int[0];
        int[] xIdx = new int[0], xWt = new int[0], yIdx = new int[0], yWt = new int[0];
        Size weightsFor, weightsTo;

        /// <summary>Scales <paramref name="size"/> pixels of <paramref name="canvasDC"/> onto the given rectangle of <paramref name="dc"/>.</summary>
        public void Draw(IntPtr dc, int dx, int dy, int dw, int dh, IntPtr canvasDC, Size size)
        {
            if (size.Width < 2 || size.Height < 2 || dw <= size.Width || dh <= size.Height)
            {
                // Shrinking (or degenerate): nothing to keep crisp.
                Native.SetStretchBltMode(dc, Native.HALFTONE);
                Native.StretchBlt(dc, dx, dy, dw, dh, canvasDC, 0, 0, size.Width, size.Height, Native.SRCCOPY);
                return;
            }

            EnsureSource(size);
            Native.BitBlt(srcDC, 0, 0, size.Width, size.Height, canvasDC, 0, 0, Native.SRCCOPY);
            Native.GdiFlush();
            Marshal.Copy(srcBits, src, 0, size.Width * size.Height);

            var to = new Size(dw, dh);
            if (size != weightsFor || to != weightsTo)
            {
                xIdx = new int[dw]; xWt = new int[dw]; yIdx = new int[dh]; yWt = new int[dh];
                Weights(size.Width, dw, xIdx, xWt);
                Weights(size.Height, dh, yIdx, yWt);
                weightsFor = size; weightsTo = to;
            }
            if (rows.Length < size.Height * dw) rows = new int[size.Height * dw];
            if (dst.Length < dw * dh) dst = new int[dw * dh];

            int sw = size.Width;
            int[] s = src, r = rows, d = dst, xi = xIdx, xw = xWt, yi = yIdx, yw = yWt;

            // Pass 1: every source row widened to dw.
            ForChunks(size.Height, (from, until) =>
            {
                for (int y = from; y < until; y++)
                {
                    int so = y * sw, ro = y * dw;
                    for (int x = 0; x < dw; x++)
                    {
                        int i = so + xi[x], w = xw[x];
                        r[ro + x] = w == 0 ? s[i] : w == 256 ? s[i + 1] : Mix(s[i], s[i + 1], w);
                    }
                }
            });

            // Pass 2: output rows, mostly plain copies of a widened row.
            ForChunks(dh, (from, until) =>
            {
                for (int y = from; y < until; y++)
                {
                    int a = yi[y] * dw, w = yw[y], o = y * dw;
                    if (w == 0) Array.Copy(r, a, d, o, dw);
                    else if (w == 256) Array.Copy(r, a + dw, d, o, dw);
                    else for (int x = 0; x < dw; x++) d[o + x] = Mix(r[a + x], r[a + dw + x], w);
                }
            });

            var bi = Native.BITMAPINFOHEADER.TopDown32(dw, dh);
            Native.SetDIBitsToDevice(dc, dx, dy, (uint)dw, (uint)dh, 0, 0, 0, (uint)dh, dst, ref bi, Native.DIB_RGB_COLORS);
        }

        /// <summary>
        /// For each of <paramref name="dstLen"/> output pixels: the left/upper source pixel and the weight (0..256)
        /// of its right/lower neighbour. The bilinear fraction is steepened by the scale factor, so the blend
        /// spans exactly one output pixel; at integer factors this is plain nearest neighbour.
        /// </summary>
        internal static void Weights(int srcLen, int dstLen, int[] idx, int[] wt)
        {
            double f = (double)dstLen / srcLen;
            for (int o = 0; o < dstLen; o++)
            {
                double u = (o + 0.5) / f - 0.5;
                int i = (int)Math.Floor(u);
                double t = (u - i - 0.5) * f + 0.5;
                if (i < 0) { i = 0; t = 0; }
                else if (i >= srcLen - 1) { i = srcLen - 2; t = 1; }
                idx[o] = i;
                wt[o] = (int)Math.Round(Math.Max(0, Math.Min(1, t)) * 256);
            }
        }

        /// <summary>Blends two BGRX pixels, <paramref name="w"/> = weight of <paramref name="b"/> in 0..256.</summary>
        internal static int Mix(int a, int b, int w)
        {
            uint ua = (uint)a, ub = (uint)b, uw = (uint)w, iw = 256 - uw;
            return (int)(((((ua & 0xFF00FFu) * iw + (ub & 0xFF00FFu) * uw) >> 8) & 0xFF00FFu)
                       | ((((ua & 0xFF00u) * iw + (ub & 0xFF00u) * uw) >> 8) & 0xFF00u));
        }

        /// <summary>Runs <paramref name="body"/>(from, to) over [0, count) in parallel chunks (twice the core count).</summary>
        static void ForChunks(int count, Action<int, int> body)
        {
            int chunks = Math.Min(count, Environment.ProcessorCount * 2);
            Parallel.For(0, chunks, c => body(count * c / chunks, count * (c + 1) / chunks));
        }

        /// <summary>Creates the top-down 32-bit DIB that receives the canvas, when the size changes.</summary>
        void EnsureSource(Size size)
        {
            if (srcDC != IntPtr.Zero && size == srcSize) return;
            ReleaseSource();
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            srcDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            var bi = Native.BITMAPINFOHEADER.TopDown32(size.Width, size.Height);
            srcBmp = Native.CreateDIBSection(srcDC, ref bi, Native.DIB_RGB_COLORS, out srcBits, IntPtr.Zero, 0);
            srcOld = Native.SelectObject(srcDC, srcBmp);
            src = new int[size.Width * size.Height];
            srcSize = size;
        }

        /// <summary>Frees the source DIB and its DC.</summary>
        void ReleaseSource()
        {
            if (srcDC == IntPtr.Zero) return;
            Native.SelectObject(srcDC, srcOld);
            Native.DeleteObject(srcBmp);
            Native.DeleteDC(srcDC);
            srcDC = IntPtr.Zero;
        }

        /// <summary>Frees the GDI resources.</summary>
        public void Dispose() { ReleaseSource(); }
    }
}
