using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Dapper;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Leitura de listas tolerante a linha inválida: a que não converte vai para o Trace e sai do resultado, sem
/// esconder as outras. Por isso converte linha a linha: o <c>QueryAsync</c> do Dapper monta a lista inteira de uma
/// vez, e um tipo errado numa coluna falharia antes de a linha chegar aqui. Falha do próprio banco continua subindo.
/// </summary>
internal static class RowReader
{
    /// <param name="command">O SELECT, com a coluna <c>Id</c>, para identificar no Trace a linha pulada.</param>
    public static async Task<List<T>> ReadValidAsync<TRow, T>(DbConnection connection, CommandDefinition command, string table,
        Func<TRow, T> toDomain)
    {
        var items = new List<T>();
        await using var reader = await connection.ExecuteReaderAsync(command);
        var parse = reader.GetRowParser<TRow>();
        while (await reader.ReadAsync(command.CancellationToken))
        {
            try
            {
                items.Add(toDomain(parse(reader)));
            }
            catch (Exception e) when (IsInvalidRow(e))
            {
                Trace.TraceError($"Linha inválida ignorada em {table} (id {reader["Id"]}): {e.Message}");
            }
        }
        return items;
    }

    /// <summary>
    /// O que a conversão lança: os formatos e valores desconhecidos (<see cref="FormatException"/>), texto nulo
    /// (<see cref="ArgumentException"/>) e, pelo Dapper, tipo errado na coluna (<see cref="DataException"/>).
    /// </summary>
    private static bool IsInvalidRow(Exception e) =>
        e is FormatException or ArgumentException or DataException or InvalidCastException or OverflowException;
}
