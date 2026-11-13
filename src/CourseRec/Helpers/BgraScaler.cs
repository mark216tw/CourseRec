namespace Com.VibeCode.CourseRec.Helpers;

public static class BgraScaler
{
    public static (int Width, int Height) ComputePreviewSize(int width, int height, int maxDimension)
    {
        if (width <= 0 || height <= 0 || maxDimension <= 0)
        {
            return (0, 0);
        }

        int longest = Math.Max(width, height);
        if (longest <= maxDimension)
        {
            return (width, height);
        }

        double scale = (double)maxDimension / longest;
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static unsafe void ScaleBgraIntoBuffer(
        ReadOnlySpan<byte> src,
        int srcW,
        int srcH,
        byte[] dst,
        int dstW,
        int dstH)
    {
        if (dstW <= 0 || dstH <= 0)
        {
            return;
        }

        int dstTotalBytes = dstW * dstH * 4;
        if (srcW <= 0 || srcH <= 0 || src.Length < srcW * srcH * 4 || dst.Length < dstTotalBytes)
        {
            Array.Clear(dst, 0, Math.Min(dst.Length, Math.Max(0, dstTotalBytes)));
            return;
        }

        double scale = Math.Min((double)dstW / srcW, (double)dstH / srcH);
        int fitW = Math.Max(1, Math.Min(dstW, (int)Math.Round(srcW * scale)));
        int fitH = Math.Max(1, Math.Min(dstH, (int)Math.Round(srcH * scale)));
        int offsetX = (dstW - fitW) / 2;
        int offsetY = (dstH - fitH) / 2;
        int rightPad = dstW - offsetX - fitW;
        int bottomPad = dstH - offsetY - fitH;

        if (offsetY > 0)
        {
            Array.Clear(dst, 0, offsetY * dstW * 4);
        }

        if (bottomPad > 0)
        {
            Array.Clear(dst, (offsetY + fitH) * dstW * 4, bottomPad * dstW * 4);
        }

        fixed (byte* srcPtr = src)
        fixed (byte* dstPtr = dst)
        {
            if (fitW == srcW && fitH == srcH)
            {
                uint* srcPixels = (uint*)srcPtr;
                uint* dstPixels = (uint*)dstPtr;
                for (int y = 0; y < fitH; y++)
                {
                    uint* dstRow = dstPixels + ((y + offsetY) * dstW);
                    for (int lx = 0; lx < offsetX; lx++)
                    {
                        dstRow[lx] = 0;
                    }

                    Buffer.MemoryCopy(srcPixels + (y * srcW), dstRow + offsetX, fitW * 4, fitW * 4);

                    for (int rx = offsetX + fitW; rx < dstW; rx++)
                    {
                        dstRow[rx] = 0;
                    }
                }

                return;
            }

            int maxSrcX = srcW - 1;
            int maxSrcY = srcH - 1;
            int stepXFp = fitW > 1 ? ((srcW - 1) << 8) / (fitW - 1) : 0;
            int stepYFp = fitH > 1 ? ((srcH - 1) << 8) / (fitH - 1) : 0;

            for (int y = 0; y < fitH; y++)
            {
                int syFp = y * stepYFp;
                int sy0 = Math.Min(maxSrcY, syFp >> 8);
                int sy1 = Math.Min(maxSrcY, sy0 + 1);
                int wy = syFp & 0xFF;
                int invWy = 256 - wy;

                byte* srcRow0 = srcPtr + (sy0 * srcW * 4);
                byte* srcRow1 = srcPtr + (sy1 * srcW * 4);
                byte* dstRowStart = dstPtr + ((y + offsetY) * dstW * 4);

                if (offsetX > 0)
                {
                    new Span<byte>(dstRowStart, offsetX * 4).Clear();
                }

                if (rightPad > 0)
                {
                    new Span<byte>(dstRowStart + ((offsetX + fitW) * 4), rightPad * 4).Clear();
                }

                byte* dstPx = dstRowStart + (offsetX * 4);
                for (int x = 0; x < fitW; x++)
                {
                    int sxFp = x * stepXFp;
                    int sx0 = Math.Min(maxSrcX, sxFp >> 8);
                    int sx1 = Math.Min(maxSrcX, sx0 + 1);
                    int wx = sxFp & 0xFF;
                    int invWx = 256 - wx;

                    byte* p00 = srcRow0 + (sx0 * 4);
                    byte* p01 = srcRow0 + (sx1 * 4);
                    byte* p10 = srcRow1 + (sx0 * 4);
                    byte* p11 = srcRow1 + (sx1 * 4);

                    int w00 = invWx * invWy;
                    int w01 = wx * invWy;
                    int w10 = invWx * wy;
                    int w11 = wx * wy;

                    dstPx[0] = (byte)(((p00[0] * w00) + (p01[0] * w01) + (p10[0] * w10) + (p11[0] * w11) + 32768) >> 16);
                    dstPx[1] = (byte)(((p00[1] * w00) + (p01[1] * w01) + (p10[1] * w10) + (p11[1] * w11) + 32768) >> 16);
                    dstPx[2] = (byte)(((p00[2] * w00) + (p01[2] * w01) + (p10[2] * w10) + (p11[2] * w11) + 32768) >> 16);
                    dstPx[3] = 255;
                    dstPx += 4;
                }
            }
        }
    }
}
