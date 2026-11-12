using RecoveryBLL.Exceptions;
using RecoveryBLL.Models;

namespace RecoveryBLL.Validation;

public static class ScanRequestValidator
{
    public static void Validate(ScanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            throw new RecoveryServiceException("Choose a folder or drive to scan.");
        if (request.Path.StartsWith(@"\\", StringComparison.Ordinal) && request.IdentityOverride == null)
            throw new RecoveryServiceException("Network locations cannot be catalogued yet (planned: network import). Scan a local drive or folder.");
        if (File.Exists(request.Path))
            throw new RecoveryServiceException($"{request.Path} is a file; choose the folder or drive that contains it.");
        foreach (var e in request.Excludes)
            if (e.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new RecoveryServiceException($"Invalid exclude: {e}");
    }

    public static int ValidateThreads(int threads) =>
        threads is >= 1 and <= 64 ? threads : throw new RecoveryServiceException("Readers per disk must be between 1 and 64.");
}
