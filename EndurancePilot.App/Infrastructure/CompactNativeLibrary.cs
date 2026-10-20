using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenCvSharp;

namespace EndurancePilot.App;

internal static class CompactNativeLibrary
{
    private const string LibraryName = "OpenCvSharpExtern";

    internal static void Initialize()
    {
        var packedPath = Path.Combine(AppContext.BaseDirectory, LibraryName + ".dll.br");
        if (!File.Exists(packedPath))
        {
            return;
        }

        var library = new Lazy<nint>(() => UnpackAndLoad(packedPath), LazyThreadSafetyMode.ExecutionAndPublication);
        NativeLibrary.SetDllImportResolver(typeof(Cv2).Assembly, (name, _, _) =>
            name is LibraryName or LibraryName + ".dll" ? library.Value : nint.Zero);
    }

    private static nint UnpackAndLoad(string packedPath)
    {
        var expectedHash = File.ReadAllText(packedPath + ".sha256").Trim();
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Invalid recognition library checksum.");
        }

        var destination = Path.Combine(AppContext.BaseDirectory, LibraryName + ".dll");
        bool Matches(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var input = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(input)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        if (!Matches(destination))
        {
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var input = File.OpenRead(packedPath))
                using (var decompress = new BrotliStream(input, CompressionMode.Decompress))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    decompress.CopyTo(output);
                }

                if (!Matches(temporary))
                {
                    throw new InvalidDataException("Recognition library failed its integrity check.");
                }

                try
                {
                    File.Move(temporary, destination, overwrite: true);
                }
                catch (IOException) when (Matches(destination)) { }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        return NativeLibrary.Load(destination);
    }
}
