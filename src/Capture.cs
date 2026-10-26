using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace ClawAmbilight
{
    /// <summary>DXGI Desktop Duplication with the downscale done on the GPU: each new frame is
    /// copied into a texture with a full mip chain, GenerateMips box-filters it, and only the mip
    /// level of ~32 px width (about 60x37 on the 1920x1200 panel) is read back to the CPU. The Python
    /// version copied the whole screen through GDI every frame (31% of a core). AcquireNextFrame
    /// only returns when the screen changed, so a static screen costs nothing.</summary>
    public sealed class Capture : IDisposable
    {
        ID3D11Device? _dev;
        ID3D11DeviceContext? _ctx;
        IDXGIOutputDuplication? _dup;
        ID3D11Texture2D? _mip, _staging;
        ID3D11ShaderResourceView? _srv;
        int _level;
        ModeRotation _rotation;

        public int Width { get; private set; }   // of the returned grid, in desktop orientation
        public int Height { get; private set; }

        void Init()
        {
            Release();
            D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out _dev, out _ctx).CheckError();
            using var dxgiDev = _dev!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDev.GetAdapter();
            adapter.EnumOutputs(0, out IDXGIOutput output).CheckError();
            using (output)
            using (var output1 = output.QueryInterface<IDXGIOutput1>())
            {
                _dup = output1.DuplicateOutput(_dev);
            }
            var desc = _dup.Description;
            _rotation = desc.Rotation;
            int w = (int)desc.ModeDescription.Width, h = (int)desc.ModeDescription.Height;  // native panel size
            _level = 0;
            while ((w >> (_level + 1)) >= 32) _level++;
            int mw = Math.Max(1, w >> _level), mh = Math.Max(1, h >> _level);

            _mip = _dev.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 0, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget, MiscFlags = ResourceOptionFlags.GenerateMips,
            });
            _srv = _dev.CreateShaderResourceView(_mip);
            _staging = _dev.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)mw, Height = (uint)mh, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
            bool swap = _rotation == ModeRotation.Rotate90 || _rotation == ModeRotation.Rotate270;
            Width = swap ? mh : mw;
            Height = swap ? mw : mh;
            Log.Write($"capture: {w}x{h} rotation={_rotation}, mip {_level} -> {Width}x{Height}");
        }

        /// <summary>Waits up to timeoutMs for a changed frame. Returns an RGB grid (Height x Width x 3,
        /// desktop orientation) or null when nothing changed. Throws when duplication is lost
        /// (lock screen, UAC, mode change); the caller retries later.</summary>
        public float[]? Next(int timeoutMs)
        {
            if (_dup == null) Init();
            var r = _dup!.AcquireNextFrame((uint)timeoutMs, out OutduplFrameInfo info, out IDXGIResource res);
            if (r == Vortice.DXGI.ResultCode.WaitTimeout) return null;
            if (r.Failure)
            {
                Release();
                throw new InvalidOperationException("AcquireNextFrame: " + r);
            }
            try
            {
                if (info.LastPresentTime == 0) return null;  // only the pointer moved
                using (res)
                using (var tex = res.QueryInterface<ID3D11Texture2D>())
                {
                    _ctx!.CopySubresourceRegion(_mip!, 0, 0, 0, 0, tex, 0);
                }
                _ctx!.GenerateMips(_srv!);
                _ctx.CopySubresourceRegion(_staging!, 0, 0, 0, 0, _mip!, (uint)_level);
                return Read();
            }
            finally
            {
                _dup.ReleaseFrame();
            }
        }

        unsafe float[] Read()
        {
            var sd = _staging!.Description;
            int mw = (int)sd.Width, mh = (int)sd.Height;
            var map = _ctx!.Map(_staging, 0, MapMode.Read);
            var grid = new float[Width * Height * 3];
            try
            {
                byte* basePtr = (byte*)map.DataPointer;
                for (int y = 0; y < mh; y++)
                {
                    byte* row = basePtr + y * map.RowPitch;
                    for (int x = 0; x < mw; x++)
                    {
                        // texture (native) -> desktop orientation
                        int dx, dy;
                        switch (_rotation)
                        {
                            case ModeRotation.Rotate90: dx = mh - 1 - y; dy = x; break;
                            case ModeRotation.Rotate180: dx = mw - 1 - x; dy = mh - 1 - y; break;
                            case ModeRotation.Rotate270: dx = y; dy = mw - 1 - x; break;
                            default: dx = x; dy = y; break;
                        }
                        int o = (dy * Width + dx) * 3;
                        grid[o] = row[x * 4 + 2]; grid[o + 1] = row[x * 4 + 1]; grid[o + 2] = row[x * 4];
                    }
                }
            }
            finally { _ctx.Unmap(_staging, 0); }
            return grid;
        }

        void Release()
        {
            _srv?.Dispose(); _srv = null;
            _mip?.Dispose(); _mip = null;
            _staging?.Dispose(); _staging = null;
            _dup?.Dispose(); _dup = null;
            _ctx?.Dispose(); _ctx = null;
            _dev?.Dispose(); _dev = null;
        }

        public void Dispose() => Release();
    }
}
