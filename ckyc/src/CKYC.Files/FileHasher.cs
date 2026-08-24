using System.Security.Cryptography;
using CKYC.Core.Abstractions;

namespace CKYC.Files;

/// <summary>SHA-256 file hashing for the CKYC file-level hash value.</summary>
public sealed class FileHasher : IFileHasher
{
    // SHA256.HashData + Convert.ToHexStringLower avoid per-byte string allocation and
    // LINQ; we use the lowercase form to match the FVU's expected hash casing.
    public string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public string ComputeSha256(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
