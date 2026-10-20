using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace EndurancePilot.App;

internal sealed class BackgroundGameCapture : IDisposable
{
    private readonly GameWindow game;
    private readonly IDirect3DDevice device;
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private bool disposed;

    internal BackgroundGameCapture(GameWindow game)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("Window capture is unavailable on this Windows installation.");
        }

        this.game = game;
        item = CreateItem(game.Handle);
        device = CreateDevice();
        try
        {
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            try
            {
                session = pool.CreateCaptureSession(item);
                try
                {
                    session.IsCursorCaptureEnabled = false;
                    session.StartCapture();
                }
                catch { session.Dispose(); throw; }
            }
            catch { pool.Dispose(); throw; }
        }
        catch { device.Dispose(); throw; }
    }

    internal bool Matches(GameWindow current) => !disposed && current == game;

    internal async Task<Bitmap?> CaptureAsync(CancellationToken token)
    {
        if (disposed || GameWindow.Find() != game)
        {
            return null;
        }

        for (int i = 0; i < 4; i++)
        {
            using var queued = pool.TryGetNextFrame();
            if (queued is null)
            {
                break;
            }
        }
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            token.ThrowIfCancellationRequested();
            using var frame = pool.TryGetNextFrame();
            if (frame is null)
            {
                await Task.Delay(35, token);
                continue;
            }
            if (frame.ContentSize.Width != item.Size.Width || frame.ContentSize.Height != item.Size.Height)
            {
                throw new GameObservationChangedException("Background window size changed. Reacquiring the game.");
            }

            using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask(token);
            if (GameWindow.Find() != game || disposed)
            {
                return null;
            }

            return CopyClient(software, game);
        }
        return null;
    }

    private static Bitmap CopyClient(SoftwareBitmap software, GameWindow game)
    {
        var crop = ClientCrop(game, software.PixelWidth, software.PixelHeight);
        uint length = checked((uint)(software.PixelWidth * software.PixelHeight * 4));
        var buffer = new Windows.Storage.Streams.Buffer(length);
        software.CopyToBuffer(buffer);
        var pixels = new byte[length];
        using (var reader = DataReader.FromBuffer(buffer))
        {
            reader.ReadBytes(pixels);
        }

        var bitmap = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < crop.Height; y++)
                {
                    Marshal.Copy(pixels, ((crop.Y + y) * software.PixelWidth + crop.X) * 4, data.Scan0 + y * data.Stride, crop.Width * 4);
                }
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static Rectangle ClientCrop(GameWindow game, int width, int height)
    {
        if (width == game.ClientBounds.Width && height == game.ClientBounds.Height)
        {
            return new(0, 0, width, height);
        }

        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(game.Handle, 9, out var bounds, Marshal.SizeOf<NativeRect>()));
        var crop = new Rectangle(game.ClientBounds.X - bounds.Left, game.ClientBounds.Y - bounds.Top, game.ClientBounds.Width, game.ClientBounds.Height);
        if (bounds.Right - bounds.Left != width || bounds.Bottom - bounds.Top != height || crop.X < 0 || crop.Y < 0 || crop.Right > width || crop.Bottom > height)
        {
            throw new GameObservationChangedException("Background capture layout is unavailable. Waiting for the game window.");
        }

        return crop;
    }

    private static GraphicsCaptureItem CreateItem(nint window)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var name));
        nint factory = 0, itemPointer = 0;
        try
        {
            var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref interopId, out factory));
            var create = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 3 * IntPtr.Size));
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            Marshal.ThrowExceptionForHR(create(factory, window, ref itemId, out itemPointer));
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally { if (itemPointer != 0) { Marshal.Release(itemPointer); } if (factory != 0) { Marshal.Release(factory); } WindowsDeleteString(name); }
    }

    private static IDirect3DDevice CreateDevice()
    {
        nint d3d = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out d3d, out _, out context));
            var dxgiId = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, ref dxgiId, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0)
            {
                Marshal.Release(inspectable);
            }

            if (dxgi != 0)
            {
                Marshal.Release(dxgi);
            }

            if (context != 0)
            {
                Marshal.Release(context);
            }

            if (d3d != 0)
            {
                Marshal.Release(d3d);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.Dispose();
        pool.Dispose();
        device.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateForWindow(nint self, nint window, ref Guid iid, out nint result);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, int type, nint software, uint flags, nint levels, uint levelCount, uint sdk, out nint device, out uint level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);
}
