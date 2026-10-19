using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Domain.Enums;

/// <summary>
/// What the import executor is currently doing with a Processing batch —
/// the row pass (parse/transform/validate, before any DB write) vs the
/// COPY itself. Surfaced so the wizard's progress bar can say what it is
/// waiting on instead of looking stuck. Null once the batch is terminal.
/// </summary>
public enum ImportPhase
{
    Validating, Copying
}
