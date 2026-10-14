using System;
using System.Data.Common;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using Npgsql;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// Transient-failure classification for the COPY retry loop, based on the
/// PostgreSQL SQLSTATE code instead of the exception message text — messages
/// are localisable/variable, codes are part of the contract.
/// 57P01 admin_shutdown, 40001 serialization_failure, 08006 connection_failure.
/// </summary>
public class TransientErrorDetector : ITransientErrorDetector
{
    public bool IsTransient(Exception ex) => ex switch
    {
        PostgresException pg => pg.SqlState is "57P01" or "40001" or "08006",
        NpgsqlException npg when npg.InnerException is PostgresException inner
            => inner.SqlState is "57P01" or "40001" or "08006",
        DbException => false,
        TimeoutException => true,
        TaskCanceledException => true,
_ => false
    };

    /// <summary>Readable motif per known failure class, keyed on SQLSTATE.
    /// Raw exception text stays in logs, not on the batch. Application calls
    /// this after a COPY failure; the wizard surfaces the string at step 5.</summary>
    public string DescribeFailure(Exception ex)
    {
        if (ex is AppValidationException)
            return ex.Message; // already user-phrased (row + field)

        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;

        if (pg is not null)
        {
            return pg.SqlState switch
            {
                "23514" => $"Violation de contrainte de vérification ({pg.ConstraintName ?? "contrainte base"}) : une valeur ne respecte pas une règle de la table cible.",
                "23503" => $"Violation de clé étrangère ({pg.ConstraintName ?? "contrainte base"}) : une valeur référence une ligne absente.",
                "23505" => "Doublon : une valeur unique de la table cible est déjà présente.",
                "42703" or "42P01" => "La structure de la table cible a changé : une colonne référencée par le mapping n'existe plus. Recharger le schéma et le mapping.",
                "22P02" or "42804" => "Une valeur du fichier est incompatible avec le type de la colonne cible (typiquement un type non couvert par le mapping).",
                _ => "Erreur PostgreSQL pendant l'écriture (détail technique dans les journaux serveur).",
            };
        }

        return ex.Message.Length <= 200
            ? ex.Message
            : "Erreur inattendue pendant l'import (détail technique dans les journaux serveur).";
    }
}
