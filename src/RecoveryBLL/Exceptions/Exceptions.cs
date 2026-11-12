namespace RecoveryBLL.Exceptions;

/// <summary>Thrown by the DAL when the catalog database fails. Services translate it to <see cref="RecoveryServiceException"/>.</summary>
public class RepositoryException : Exception
{
    public RepositoryException(string message) : base(message) { }
    public RepositoryException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown by the services with a message that can be shown to the user as-is.</summary>
public class RecoveryServiceException : Exception
{
    public RecoveryServiceException(string message) : base(message) { }
    public RecoveryServiceException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Configuration problems (.env, dictionaries.xml).</summary>
public class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A read that did not finish in time: typically a bad sector the drive keeps retrying. Thrown by the DAL's file
/// reader; the services record it as an error for that file and stop reading a disk that keeps doing it.
/// </summary>
public class ReadTimeoutException : IOException
{
    public ReadTimeoutException(string message) : base(message) { }
}
