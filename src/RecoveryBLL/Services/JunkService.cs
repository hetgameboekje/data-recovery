using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Phase 3: applies the junk and keep-together rules (config/dictionaries.xml) to the whole catalog.
/// Read-only: the result only changes where the copy puts files; nothing on any disk is moved or deleted.
/// </summary>
public sealed class JunkService(IReportRepository reports, DictionaryService dictionaries)
{
    /// <param name="junk">False: only keep-together folders (junk detection switched off).</param>
    public FileTagResult Analyze(CancellationToken ct, bool junk = true)
    {
        var facts = new List<FileFactsDto>();
        RepositoryGuard.Run(() => reports.ReadFileFacts(facts.Add, ct), "Cannot read the catalog for the junk check");
        ct.ThrowIfCancellationRequested();
        var d = dictionaries.Current;
        return FileTagger.Tag(facts, d.Junk, d.KeepTogether, junk);
    }
}
