using System;

namespace DataMart.Application.Common.Interfaces;

/// <summary>
/// Decides whether a failure from the bulk import is transient (worth retrying).
/// The classification needs the provider's error codes (e.g. PostgreSQL SQLSTATE),
/// which live in Infrastructure — Application only sees <see cref="System.Data.Common.DbException"/>.
/// </summary>
public interface ITransientErrorDetector
{
    bool IsTransient(Exception ex);

    /// <summary>Human-readable motif for a failed batch. Known failure classes
    /// get a plain sentence; anything else a generic one. The raw exception text
    /// must stay in the structured logs: provider messages leak column and table
    /// names, which a user at the wizard's step 5 should never have to parse.</summary>
    string DescribeFailure(Exception ex);
}
