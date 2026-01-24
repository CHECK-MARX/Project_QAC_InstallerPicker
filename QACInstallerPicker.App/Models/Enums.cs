namespace QACInstallerPicker.App.Models;

public enum OsType
{
    Windows,
    Linux,
    Unknown
}

public enum TransferStatus
{
    Queued,
    HashingSource,
    Downloading,
    Paused,
    Verifying,
    Completed,
    Failed,
    Canceled
}

public enum VerifyResult
{
    NotChecked,
    Ok,
    Ng
}
