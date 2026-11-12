using System.IO.Hashing;
using System.Security.Cryptography;
using RecoveryBLL.Dtos;

namespace RecoveryBLL.Fingerprinting;

public sealed class FileChangedException(string message) : IOException(message);

/// <summary>
/// The content fingerprints. xxHash128 for speed; SHA-256 optional.
///   Quick hash = first 64 KB + last 64 KB + size. For files up to 128 KB that is the whole file,
///   so it doubles as the full hash and the file never needs a second read.
/// </summary>
public static class ContentHasher
{
    public const int Chunk = 64 * 1024;

    public static FingerprintResultDto Quick(long id, Stream s, long expectedSize, bool sha256)
    {
        if (s.Length != expectedSize)
            throw new FileChangedException($"size changed since scan ({expectedSize:N0} -> {s.Length:N0} bytes); scan again");

        var x = new XxHash128();
        if (expectedSize <= 2L * Chunk)
        {
            var all = new byte[expectedSize];
            s.ReadExactly(all);
            x.Append(all);
            var hash = x.GetCurrentHash();
            return new FingerprintResultDto
            {
                Id = id, Kind = FingerprintResultKind.QuickHash, QuickHash = hash, FullHash = hash,
                Sha256 = sha256 ? SHA256.HashData(all) : null,
            };
        }

        var buf = new byte[Chunk];
        s.Seek(0, SeekOrigin.Begin);
        s.ReadExactly(buf);
        x.Append(buf);
        s.Seek(expectedSize - Chunk, SeekOrigin.Begin);
        s.ReadExactly(buf);
        x.Append(buf);
        x.Append(BitConverter.GetBytes(expectedSize));
        return new FingerprintResultDto { Id = id, Kind = FingerprintResultKind.QuickHash, QuickHash = x.GetCurrentHash() };
    }

    public static FingerprintResultDto Full(long id, Stream s, long expectedSize, bool sha256)
    {
        if (s.Length != expectedSize)
            throw new FileChangedException($"size changed since scan ({expectedSize:N0} -> {s.Length:N0} bytes); scan again");
        var x = new XxHash128();
        using var sha = sha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        var buf = new byte[1024 * 1024];
        long total = 0;
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            x.Append(buf.AsSpan(0, n));
            sha?.AppendData(buf, 0, n);
            total += n;
        }
        if (total != expectedSize) throw new FileChangedException("file changed while reading; scan again");
        return new FingerprintResultDto
        {
            Id = id, Kind = FingerprintResultKind.FullHash, FullHash = x.GetCurrentHash(), Sha256 = sha?.GetHashAndReset(),
        };
    }
}
