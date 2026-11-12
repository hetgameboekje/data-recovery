using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>Holds the loaded dictionaries (config/dictionaries.xml) and reloads them on request.</summary>
public sealed class DictionaryService(IDictionaryRepository repository)
{
    readonly object gate = new();
    RecoveryDictionaries? current;

    public string Location => repository.Location;

    public RecoveryDictionaries Current
    {
        get
        {
            lock (gate) return current ??= LoadChecked();
        }
    }

    public RecoveryDictionaries Reload()
    {
        var fresh = LoadChecked();
        lock (gate) current = fresh;
        return fresh;
    }

    RecoveryDictionaries LoadChecked()
    {
        try
        {
            return repository.Load();
        }
        catch (ConfigurationException ex)
        {
            throw new RecoveryServiceException($"Cannot load the dictionaries ({repository.Location}): {ex.Message}", ex);
        }
    }
}
