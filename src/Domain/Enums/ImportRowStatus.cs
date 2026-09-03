using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Domain.Enums;

/// <summary>
/// Per-row outcome of an import — one entry per row in the ImportRecord journal.
/// Distinct from <see cref="ImportStatus"/>, which is the per-batch lifecycle.
/// </summary>
public enum ImportRowStatus
{
    Imported = 0,
    Rejected = 1,
}
